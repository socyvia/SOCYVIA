using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>Cloudflare implementation details live here, outside provider-neutral experiment contracts.</summary>
public sealed class CloudflareRemoteProvider
{
    private readonly HttpClient _api;
    private readonly CanonicalPublicationRegistryService _canonicalRegistry;
    public CloudflareRemoteProvider(HttpClient? api = null, CanonicalPublicationRegistryService? canonicalRegistry = null)
    {
        _api = api ?? new HttpClient { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        _canonicalRegistry = canonicalRegistry ?? new CanonicalPublicationRegistryService();
    }
    public static string MediaObjectKey(string deploymentId, MediaManifestAsset asset) => $"deployments/{SafeSegment(deploymentId)}/media/{SafeSegment(asset.MediaAssetId)}/{SafeSegment(asset.FileName ?? "asset")}";
    public static string PackageObjectKey(string deploymentId) => $"packages/{SafeSegment(deploymentId)}/experiment-package.json";
    public static string R2ObjectApiPath(CloudflareProviderConfiguration configuration, string objectKey) =>
        $"accounts/{Uri.EscapeDataString(configuration.AccountId)}/r2/buckets/{Uri.EscapeDataString(configuration.R2BucketName)}/objects/" +
        string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
    public static string DeploymentPublicId(ExperimentDeployment deployment) => SafeSegment($"{deployment.ResearcherHandle ?? "researcher"}-{deployment.ExperimentCode ?? deployment.DeploymentId[..8]}");
    public static string EntryConfigurationHash(DeploymentEntryConfiguration configuration, IReadOnlyList<DeploymentTextContent> content, IReadOnlyList<DeploymentQuestionnaireDefinition>? questionnaires = null)
    {
        var canonical = JsonSerializer.Serialize(new { configuration, Content = content.OrderBy(item => item.ConditionId, StringComparer.Ordinal).ThenBy(item => item.SortOrder).ThenBy(item => item.ContentId, StringComparer.Ordinal), Questionnaires = (questionnaires ?? Array.Empty<DeploymentQuestionnaireDefinition>()).OrderBy(item => item.Stage).ThenBy(item => item.VersionId, StringComparer.Ordinal) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
    public static bool RevalidateAsset(MediaManifestAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.OriginalSourceReference) || !File.Exists(asset.OriginalSourceReference) || string.IsNullOrWhiteSpace(asset.Sha256)) return false;
        using var stream = File.OpenRead(asset.OriginalSourceReference); return string.Equals(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public async Task<CloudflarePublishResult> PublishAsync(
        CloudflareProviderConfiguration configuration,
        string token,
        ExperimentPackage package,
        ExperimentDeployment deployment,
        DeploymentEntryConfiguration entry,
        IReadOnlyList<DeploymentTextContent> content,
        IReadOnlyList<DeploymentQuestionnaireDefinition>? questionnaires = null,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.HasRequiredResourceIdentity) return CloudflarePublishResult.Failed("Validating", "Cloudflare resource configuration is incomplete.");
        try
        {
            var publicId = SafeSegment($"{deployment.ResearcherHandle ?? "researcher"}-{deployment.ExperimentCode ?? deployment.DeploymentId[..8]}");
            // A remote record is deliberately non-recruiting while media/package work is incomplete.
            await D1Async(configuration, token, "INSERT OR IGNORE INTO deployments(id,public_id,package_key,package_hash,status,created_at) VALUES(?,?,?,?,?,?)", new object?[] { deployment.DeploymentId, publicId, PackageObjectKey(deployment.DeploymentId), package.ConfigurationHash, "Publishing", deployment.CreatedAtUtc.ToString("O") }, cancellationToken);
            foreach (var asset in package.MediaManifest)
            {
                if (!asset.RequiredForDeployment) continue;
                if (!RevalidateAsset(asset)) return CloudflarePublishResult.Failed("PreparingMedia", "A required local media asset is missing or its SHA-256 changed.");
                var key = MediaObjectKey(deployment.DeploymentId, asset);
                await UploadObjectAsync(configuration, token, key, asset.OriginalSourceReference!, asset.MimeType, asset.Sha256!, cancellationToken);
            }
            var packageKey = PackageObjectKey(deployment.DeploymentId);
            var packageJson = JsonSerializer.Serialize(package);
            await UploadBytesAsync(configuration, token, packageKey, Encoding.UTF8.GetBytes(packageJson), "application/json", package.ConfigurationHash, cancellationToken);
            var publishedContent = content.Select(item => item with { Media = ResolvePublishedMedia(item, package, deployment) }).ToArray();
            await PersistParticipantDefinitionAsync(configuration, token, package, deployment, entry, publishedContent, questionnaires, cancellationToken);
            await D1Async(configuration, token, "UPDATE deployments SET status = ? WHERE id = ? AND package_hash = ?", new object?[] { "Published", deployment.DeploymentId, package.ConfigurationHash }, cancellationToken);
            var url = configuration.WorkerEndpoint.TrimEnd('/') + "/experimentfeed/" + Uri.EscapeDataString(deployment.ResearcherHandle ?? "researcher") + "/" + Uri.EscapeDataString(deployment.ExperimentCode ?? deployment.DeploymentId[..8]);
            var published = deployment with { Status = ExperimentDeploymentStatus.Published, PublishedAtUtc = DateTime.UtcNow };
            await _canonicalRegistry.EnsureRegisteredAsync(configuration, token, published, cancellationToken);
            try { await PublishedExperimentStatusStore.SaveAsync(published, new Uri(url)); }
            catch (Exception exception) { ApplicationDiagnosticsService.LogException(exception, "Persist published experiment status"); }
            return new(true, "Published", null, url, published);
        }
        catch (HttpRequestException) { return CloudflarePublishResult.Failed("UploadingMedia", "Cloudflare upload or deployment registration failed. The deployment remains retryable."); }
        catch (InvalidOperationException error) { return CloudflarePublishResult.Failed("DeployingRuntimeConfig", error.Message); }
    }
    /// <summary>Publishes an immutable deployment without an R2 upload. Text and participant-accessible HTTPS media are supported.</summary>
    public async Task<CloudflarePublishResult> PublishTextOnlyAsync(CloudflareProviderConfiguration configuration, string token, ExperimentPackage package, ExperimentDeployment deployment, DeploymentEntryConfiguration entry, IReadOnlyList<DeploymentTextContent> content, IReadOnlyList<DeploymentQuestionnaireDefinition>? questionnaires = null, CancellationToken cancellationToken = default)
    {
        if (!configuration.HasRequiredTextRuntimeIdentity) return CloudflarePublishResult.Failed("Validating", "Cloudflare account, D1, and Worker endpoint configuration are required.");
        if (package.MediaManifest.Any(asset => asset.RequiredForDeployment)) return CloudflarePublishResult.Failed("MediaRequired", "A participant-accessible HTTPS URL is required for each local media item before publishing. Optional cloud storage may be configured separately.");
        if (string.IsNullOrWhiteSpace(entry.StudyTitle) || (entry.ConsentRequired && string.IsNullOrWhiteSpace(entry.ConsentText)) || content.Count == 0 || content.Any(item =>
                string.IsNullOrWhiteSpace(item.ContentId) ||
                string.IsNullOrWhiteSpace(item.Body) && item.Media is null ||
                item.Media is not null && !ValidParticipantMedia(item.Media)))
            return CloudflarePublishResult.Failed("Validating", "Deployment entry information, required consent, and valid text or participant-accessible HTTPS media are required.");
        questionnaires ??= Array.Empty<DeploymentQuestionnaireDefinition>();
        var conditions = package.Conditions.Select(item => item.ConditionId).ToHashSet(StringComparer.Ordinal);
        if (content.Any(item => item.ConditionId is not null && !conditions.Contains(item.ConditionId))) return CloudflarePublishResult.Failed("Validating", "Text content references a condition that is not part of the immutable deployment package.");
        if (questionnaires.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.VersionId) || item.Items.Count == 0 || item.Items.Any(question => string.IsNullOrWhiteSpace(question.Id) || string.IsNullOrWhiteSpace(question.Question)))) return CloudflarePublishResult.Failed("Validating", "Each deployment questionnaire requires stable IDs, a version, and complete items.");
        if (entry.PreQuestionnaireConfigured != questionnaires.Any(item => item.Stage == QuestionnaireStage.Pre) || entry.PostQuestionnaireConfigured != questionnaires.Any(item => item.Stage == QuestionnaireStage.Post)) return CloudflarePublishResult.Failed("Validating", "Participant-flow questionnaire stages must match the immutable deployment questionnaire definitions.");
        var publicId = DeploymentPublicId(deployment);
        var created = deployment.CreatedAtUtc.ToString("O");
        try
        {
            await D1Async(configuration, token, "INSERT OR IGNORE INTO deployments(id,public_id,package_key,package_hash,status,created_at) VALUES(?,?,?,?,?,?)", new object?[] { deployment.DeploymentId, publicId, "text-only/" + deployment.DeploymentId, package.ConfigurationHash, "Publishing", created }, cancellationToken);
            await PersistParticipantDefinitionAsync(configuration, token, package, deployment, entry, content, questionnaires, cancellationToken);
            await D1Async(configuration, token, "UPDATE deployments SET status=? WHERE id=? AND package_hash=?", new object?[] { "Published", deployment.DeploymentId, package.ConfigurationHash }, cancellationToken);
            var url = configuration.WorkerEndpoint.TrimEnd('/') + "/experimentfeed/" + Uri.EscapeDataString(deployment.ResearcherHandle ?? "researcher") + "/" + Uri.EscapeDataString(deployment.ExperimentCode ?? deployment.DeploymentId[..8]);
            var published = deployment with { Status = ExperimentDeploymentStatus.Published, PublishedAtUtc = DateTime.UtcNow };
            await _canonicalRegistry.EnsureRegisteredAsync(configuration, token, published, cancellationToken);
            try { await PublishedExperimentStatusStore.SaveAsync(published, new Uri(url)); }
            catch (Exception exception) { ApplicationDiagnosticsService.LogException(exception, "Persist published experiment status"); }
            return new(true, "Published", null, url, published);
        }
        catch (HttpRequestException) { return CloudflarePublishResult.Failed("Publishing", "Cloudflare could not persist the text deployment. The deployment remains retryable."); }
        catch (InvalidOperationException error) { return CloudflarePublishResult.Failed("Publishing", error.Message); }
    }
    public async Task<RemoteSyncPullResult> PullAsync(CloudflareProviderConfiguration configuration, string token, RemoteSyncCursor cursor, CancellationToken cancellationToken = default)
    {
        // The D1 REST API is used only for researcher-controlled administrative sync. Participant traffic stays on the Worker.
        var checkpoint = cursor.Checkpoint ?? "";
        var document = await D1Async(configuration, token, "SELECT s.id,s.participant_id,s.deployment_id,s.condition_id,c.group_id,COALESCE(s.run_type,'Main') AS run_type,s.started_at,s.completed_at,s.completion_state,s.lifecycle_state,s.feed_end_at,s.post_questionnaire_completed_at,e.configuration_json FROM sessions s LEFT JOIN deployment_conditions c ON c.deployment_id=s.deployment_id AND c.condition_id=s.condition_id LEFT JOIN deployment_entry_config e ON e.deployment_id=s.deployment_id WHERE COALESCE(s.completed_at,s.started_at) > ? ORDER BY COALESCE(s.completed_at,s.started_at), s.id LIMIT 500", new object?[] { checkpoint }, cancellationToken);
        var sessions = new List<RemoteParticipantSessionContract>(); string? next = checkpoint;
        foreach (var row in ResultRows(document)) { var completed = Get(row, "completed_at"); var started = Get(row, "started_at"); next = completed ?? started ?? next; sessions.Add(new RemoteParticipantSessionContract { SessionId = Get(row,"id") ?? "", ParticipantId = Get(row,"participant_id") ?? "", StudyId = StudyIdFromEntry(Get(row,"configuration_json")) ?? string.Empty, DeploymentId = Get(row,"deployment_id") ?? "", ConditionId = Get(row,"condition_id") ?? "", GroupId = Get(row,"group_id"), RunType = ParseRunType(Get(row,"run_type")), StartedAtUtc = Parse(started), CompletedAtUtc = Parse(completed), FeedEndedAtUtc = Parse(Get(row,"feed_end_at")), PostQuestionnaireCompletedAtUtc = Parse(Get(row,"post_questionnaire_completed_at")), LifecycleState = Enum.TryParse<RemoteParticipantLifecycleState>(Get(row,"lifecycle_state"), true, out var lifecycle) ? lifecycle : RemoteParticipantLifecycleState.Incomplete, CompletionState = Enum.TryParse<RemoteParticipantCompletionState>(Get(row,"completion_state"), out var state) ? state : RemoteParticipantCompletionState.Incomplete }); }
        var sessionIds = sessions.Select(item => item.SessionId).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        var events = Array.Empty<RemoteTelemetryEvent>();
        if (sessionIds.Length > 0)
        {
            var placeholders = string.Join(',', sessionIds.Select(_ => '?'));
            var eventsDocument = await D1Async(configuration, token, $"SELECT e.id,e.session_id,e.deployment_id,e.condition_id,e.content_id,e.event_type,e.client_timestamp,e.relative_ms,e.payload_json,e.schema_version,s.participant_id FROM events e JOIN sessions s ON s.id=e.session_id WHERE e.session_id IN ({placeholders}) ORDER BY e.relative_ms,e.id", sessionIds.Cast<object?>().ToArray(), cancellationToken);
            events = ResultRows(eventsDocument).Select(row => new RemoteTelemetryEvent { EventId = Get(row,"id") ?? string.Empty, SessionId = Get(row,"session_id") ?? string.Empty, ParticipantId = Get(row,"participant_id") ?? string.Empty, DeploymentId = Get(row,"deployment_id") ?? string.Empty, ConditionId = Get(row,"condition_id") ?? string.Empty, ContentId = Get(row,"content_id"), EventType = Get(row,"event_type") ?? string.Empty, ClientTimestampUtc = Parse(Get(row,"client_timestamp")) ?? DateTime.UnixEpoch, ClientRelativeMilliseconds = long.TryParse(Get(row,"relative_ms"), out var relative) ? relative : 0, PayloadJson = Get(row,"payload_json"), SchemaVersion = Get(row,"schema_version") ?? "SOCYVIA.RemoteTelemetry/2" }).ToArray();
        }
        // PRE responses intentionally have no session at the time of submission, so they
        // are pulled by their own immutable submission checkpoint rather than session IDs.
        var responsesDocument = await D1Async(configuration, token, "SELECT id,deployment_id,participant_id,session_id,questionnaire_id,questionnaire_version_id,stage,response_json,submitted_at FROM participant_questionnaire_responses WHERE submitted_at > ? ORDER BY submitted_at,id LIMIT 500", new object?[] { checkpoint }, cancellationToken);
        var responses = ResultRows(responsesDocument).Select(row => new RemoteQuestionnaireResponseContract { ResponseId = Get(row,"id") ?? string.Empty, DeploymentId = Get(row,"deployment_id") ?? string.Empty, ParticipantId = Get(row,"participant_id") ?? string.Empty, SessionId = Get(row,"session_id"), QuestionnaireId = Get(row,"questionnaire_id") ?? string.Empty, QuestionnaireVersionId = Get(row,"questionnaire_version_id") ?? string.Empty, Stage = string.Equals(Get(row,"stage"), "PRE", StringComparison.OrdinalIgnoreCase) ? QuestionnaireStage.Pre : QuestionnaireStage.Post, ResponseJson = Get(row,"response_json") ?? "{}", SubmittedAtUtc = Parse(Get(row,"submitted_at")) }).ToArray();
        return new(new RemoteSyncCursor(next, DateTime.UtcNow), sessions, events, responses);
    }
    /// <summary>Changes only recruitment admission for an already published deployment.</summary>
    public async Task SetRecruitmentPausedAsync(CloudflareProviderConfiguration configuration, string token, string deploymentId, bool paused, CancellationToken cancellationToken = default)
    {
        if (!configuration.HasRequiredTextRuntimeIdentity) throw new InvalidOperationException("Cloud publishing is not configured.");
        if (string.IsNullOrWhiteSpace(deploymentId)) throw new ArgumentException("A published deployment is required.", nameof(deploymentId));
        await D1Async(configuration, token,
            "UPDATE deployments SET status=? WHERE id=? AND run_type='Main' AND status IN ('Published','Recruiting','Paused')",
            new object?[] { paused ? "Paused" : "Recruiting", deploymentId }, cancellationToken);
    }

    /// <summary>Changes only the server-authoritative admission mode for an existing deployment.</summary>
    public async Task StartPilotAsync(CloudflareProviderConfiguration configuration, string token, string deploymentId, CancellationToken cancellationToken = default)
    {
        await SetAdmissionAsync(configuration, token, deploymentId,
            "UPDATE deployments SET status='Pilot',run_type='Pilot' WHERE id=? AND status IN ('Published','Recruiting','Paused') RETURNING id", cancellationToken);
    }

    /// <summary>Closes admission without changing the immutable provenance of pilot sessions.</summary>
    public async Task EndPilotAsync(CloudflareProviderConfiguration configuration, string token, string deploymentId, CancellationToken cancellationToken = default)
    {
        await SetAdmissionAsync(configuration, token, deploymentId,
            "UPDATE deployments SET status='Paused' WHERE id=? AND status='Pilot' AND run_type='Pilot' RETURNING id", cancellationToken);
    }

    /// <summary>Main recruitment is deliberately explicit after an optional pilot phase.</summary>
    public async Task StartMainRecruitmentAsync(CloudflareProviderConfiguration configuration, string token, string deploymentId, CancellationToken cancellationToken = default)
    {
        await SetAdmissionAsync(configuration, token, deploymentId,
            "UPDATE deployments SET status='Recruiting',run_type='Main' WHERE id=? AND status IN ('Published','Paused','Pilot') RETURNING id", cancellationToken);
    }

    private async Task SetAdmissionAsync(CloudflareProviderConfiguration configuration, string token, string deploymentId, string sql, CancellationToken cancellationToken)
    {
        if (!configuration.HasRequiredTextRuntimeIdentity) throw new InvalidOperationException("Cloud publishing is not configured.");
        if (string.IsNullOrWhiteSpace(deploymentId)) throw new ArgumentException("A published deployment is required.", nameof(deploymentId));
        using var response = await D1Async(configuration, token, sql, new object?[] { deploymentId }, cancellationToken);
        if (!ResultRows(response).Any()) throw new InvalidOperationException("The deployment is not in a compatible recruitment state for this action.");
    }

    private static DeploymentContentMedia? ResolvePublishedMedia(
        DeploymentTextContent content,
        ExperimentPackage package,
        ExperimentDeployment deployment)
    {
        if (content.Media is not null) return content.Media;
        var stimulus = package.OrderedStimuli.FirstOrDefault(item =>
            string.Equals(item.ContentId, content.ContentId, StringComparison.Ordinal) &&
            (item.ConditionId is null || string.Equals(item.ConditionId, content.ConditionId, StringComparison.Ordinal)));
        var asset = stimulus?.MediaAssetId is null
            ? null
            : package.MediaManifest.FirstOrDefault(item => string.Equals(item.MediaAssetId, stimulus.MediaAssetId, StringComparison.Ordinal));
        if (asset is null) return null;
        if (!asset.RequiredForDeployment && PublishedMediaUrlValidator.TryValidateDirectMedia(
                asset.DeploymentUrl ?? asset.OriginalSourceReference, out var external, out _))
            return new DeploymentContentMedia(MediaKind(asset), external!.AbsoluteUri, content.Title);
        if (!asset.RequiredForDeployment) return null;
        return new DeploymentContentMedia(
            MediaKind(asset),
            "/experimentfeed/media/" + Uri.EscapeDataString(MediaObjectKey(deployment.DeploymentId, asset)),
            content.Title);
    }

    private static bool ValidParticipantMedia(DeploymentContentMedia media) =>
        media.Kind.Equals("external", StringComparison.OrdinalIgnoreCase)
            ? PublishedMediaUrlValidator.TryValidate(media.Url, out _, out _)
            : PublishedMediaUrlValidator.TryValidateDirectMedia(media.Url, out _, out _);

    private async Task PersistParticipantDefinitionAsync(
        CloudflareProviderConfiguration configuration,
        string token,
        ExperimentPackage package,
        ExperimentDeployment deployment,
        DeploymentEntryConfiguration entry,
        IReadOnlyList<DeploymentTextContent> content,
        IReadOnlyList<DeploymentQuestionnaireDefinition>? questionnaires,
        CancellationToken cancellationToken)
    {
        questionnaires ??= Array.Empty<DeploymentQuestionnaireDefinition>();
        var entryHash = EntryConfigurationHash(entry, content, questionnaires);
        var created = deployment.CreatedAtUtc.ToString("O");
        var interfaceLanguages = entry.ParticipantInterfaceLanguages
            .Where(language => language is "en" or "ar")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (interfaceLanguages.Length == 0) interfaceLanguages = [entry.Language is "ar" ? "ar" : "en"];
        var defaultInterfaceLanguage = interfaceLanguages.Contains(entry.DefaultParticipantInterfaceLanguage ?? string.Empty, StringComparer.Ordinal)
            ? entry.DefaultParticipantInterfaceLanguage
            : interfaceLanguages[0];
        var entryJson = JsonSerializer.Serialize(new { schemaVersion = entry.SchemaVersion, studyId = package.StudyId, language = defaultInterfaceLanguage, interfaceLanguages, defaultInterfaceLanguage, researcher = new { name = entry.ResearcherName, role = entry.ResearcherRole, affiliation = entry.ResearcherAffiliation }, study = new { title = entry.StudyTitle, description = entry.StudyDescription, studyInformation = entry.StudyInformation, instructions = entry.ParticipantInstructions, privacy = entry.PrivacyText, estimatedDuration = entry.EstimatedDuration, estimatedDurationMinutes = entry.EstimatedDurationMinutes, consentRequired = entry.ConsentRequired, consentText = entry.ConsentText }, participantFlow = new { preQuestionnaire = entry.PreQuestionnaireConfigured, postQuestionnaire = entry.PostQuestionnaireConfigured }, deviceRules = entry.DeviceRulesJson });
        await D1Async(configuration, token, "INSERT OR IGNORE INTO deployment_entry_config(deployment_id,configuration_json,configuration_hash,schema_version,created_at) VALUES(?,?,?,?,?)", new object?[] { deployment.DeploymentId, entryJson, entryHash, entry.SchemaVersion, created }, cancellationToken);
        using (var stored = await D1Async(configuration, token, "SELECT configuration_hash FROM deployment_entry_config WHERE deployment_id=?", new object?[] { deployment.DeploymentId }, cancellationToken))
            if (!string.Equals(Get(ResultRows(stored).FirstOrDefault(), "configuration_hash"), entryHash, StringComparison.Ordinal))
                throw new InvalidOperationException("This deployment ID is already bound to a different immutable entry configuration.");
        foreach (var condition in package.Conditions)
            await D1Async(configuration, token, "INSERT OR IGNORE INTO deployment_conditions(deployment_id,condition_id,group_id,sort_order,configuration_json) VALUES(?,?,?,?,?)", new object?[] { deployment.DeploymentId, condition.ConditionId, condition.GroupId, condition.SortOrder, condition.ManipulationJson ?? "{}" }, cancellationToken);
        foreach (var item in content.OrderBy(item => item.ConditionId, StringComparer.Ordinal).ThenBy(item => item.SortOrder).ThenBy(item => item.ContentId, StringComparer.Ordinal))
        {
            var participantMedia = item.Media is null
                ? null
                : new { kind = item.Media.Kind, url = item.Media.Url, alt = item.Media.Alt };
            var payload = JsonSerializer.Serialize(new { title = item.Title, body = item.Body, media = participantMedia });
            var interactions = JsonSerializer.Serialize(new { like = item.LikeEnabled, comment = item.CommentEnabled, readMore = item.ReadMoreEnabled, save = item.SaveEnabled, share = item.ShareEnabled, collectCommentText = item.CollectCommentText });
            await D1Async(configuration, token, "INSERT OR IGNORE INTO deployment_content(id,deployment_id,condition_id,content_id,sort_order,content_type,language,payload_json,interaction_config_json,configuration_hash,created_at) VALUES(?,?,?,?,?,?,?,?,?,?,?)", new object?[] { item.Id, deployment.DeploymentId, item.ConditionId, item.ContentId, item.SortOrder, "Text", item.Language, payload, interactions, entryHash, created }, cancellationToken);
        }
        foreach (var definition in questionnaires)
        {
            var definitionJson = JsonSerializer.Serialize(new { id = definition.Id, versionId = definition.VersionId, stage = definition.Stage.ToString().ToUpperInvariant(), title = definition.Title, description = definition.Description, instructions = definition.Instructions, localizations = definition.Localizations, required = definition.Required, schemaVersion = definition.SchemaVersion, items = definition.Items.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => new { id = item.Id, type = item.Type.ToString().ToUpperInvariant(), question = item.Question, description = item.Description, localizations = item.Localizations, required = item.Required, order = item.Order, configuration = JsonSerializer.Deserialize<JsonElement>(item.ConfigurationJson) }) });
            await D1Async(configuration, token, "INSERT OR IGNORE INTO deployment_questionnaires(deployment_id,questionnaire_id,questionnaire_version_id,stage,definition_json,configuration_hash,schema_version,created_at) VALUES(?,?,?,?,?,?,?,?)", new object?[] { deployment.DeploymentId, definition.Id, definition.VersionId, definition.Stage.ToString().ToUpperInvariant(), definitionJson, entryHash, definition.SchemaVersion, created }, cancellationToken);
        }
    }

    private static string MediaKind(MediaManifestAsset asset)
    {
        var value = $"{asset.MediaType} {asset.MimeType}".ToLowerInvariant();
        if (value.Contains("video", StringComparison.Ordinal)) return "video";
        if (value.Contains("audio", StringComparison.Ordinal)) return "audio";
        if (value.Contains("image", StringComparison.Ordinal)) return "image";
        return "external";
    }
    private async Task UploadObjectAsync(CloudflareProviderConfiguration c, string token, string key, string path, string? mime, string hash, CancellationToken ct) { await using var input = File.OpenRead(path); using var content = new StreamContent(input); content.Headers.ContentType = new MediaTypeHeaderValue(mime ?? "application/octet-stream"); content.Headers.Add("x-socyvia-sha256", hash); await SendAsync(HttpMethod.Put, R2ObjectApiPath(c, key), token, content, ct); }
    private async Task UploadBytesAsync(CloudflareProviderConfiguration c, string token, string key, byte[] bytes, string mime, string hash, CancellationToken ct) { using var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue(mime); content.Headers.Add("x-socyvia-sha256", hash); await SendAsync(HttpMethod.Put, R2ObjectApiPath(c, key), token, content, ct); }
    private async Task<JsonDocument> D1Async(CloudflareProviderConfiguration c, string token, string sql, object?[] parameters, CancellationToken ct) { using var content = JsonContent.Create(new { sql, @params = parameters }); return JsonDocument.Parse(await SendAsync(HttpMethod.Post, $"accounts/{Uri.EscapeDataString(c.AccountId)}/d1/database/{Uri.EscapeDataString(c.D1DatabaseId)}/query", token, content, ct)); }
    private async Task<string> SendAsync(HttpMethod method, string path, string token, HttpContent content, CancellationToken ct) { using var request = new HttpRequestMessage(method,path) { Content=content }; request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token); using var response=await _api.SendAsync(request,ct); if(!response.IsSuccessStatusCode) throw new HttpRequestException("Cloudflare rejected the provider operation."); var body=await response.Content.ReadAsStringAsync(ct); using var document=JsonDocument.Parse(body); if(!document.RootElement.TryGetProperty("success",out var success)||!success.GetBoolean()) throw new InvalidOperationException("Cloudflare could not complete the provider operation."); return body; }
    private static string SafeSegment(string value) { var builder=new StringBuilder(); foreach(var c in value.ToLowerInvariant()) builder.Append(char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.'?c:'-'); return builder.ToString().Trim('-'); }
    private static IEnumerable<JsonElement> ResultRows(JsonDocument d) { if (!d.RootElement.TryGetProperty("result",out var result)||result.GetArrayLength()==0) yield break; var first=result[0]; if(first.TryGetProperty("results",out var rows)) foreach(var row in rows.EnumerateArray()) yield return row; }
    private static string? Get(JsonElement e,string property)=>e.TryGetProperty(property,out var value)&&value.ValueKind!=JsonValueKind.Null?(value.ValueKind==JsonValueKind.String?value.GetString():value.ToString()):null;
    private static DateTime? Parse(string? value)=>DateTime.TryParse(value,out var parsed)?parsed:null;
    private static string? StudyIdFromEntry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.TryGetProperty("studyId", out var studyId) && studyId.ValueKind == JsonValueKind.String
                ? studyId.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }
    public static ExperimentRunType ParseRunType(string? value) => string.Equals(value, "Pilot", StringComparison.OrdinalIgnoreCase) ? ExperimentRunType.Pilot : ExperimentRunType.Main;
}
public sealed record CloudflarePublishResult(bool Succeeded,string Stage,string? Error,string? ParticipantUrl,ExperimentDeployment? Deployment)
{
    /// <summary>
    /// Human-facing public link. It is available only after remote publication
    /// and canonical SOCYVIA route registration have both succeeded.
    /// </summary>
    public PublishedExperimentLink? CanonicalParticipantLink =>
        Succeeded ? PublicExperimentLinkService.ForPublishedDeployment(Deployment) : null;

    public static CloudflarePublishResult Failed(string stage,string error)=>new(false,stage,error,null,null);
}
