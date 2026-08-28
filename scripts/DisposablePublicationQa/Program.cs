using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services;

var configuration = await new CloudflareProviderConfigurationStore().LoadAsync()
                    ?? throw new InvalidOperationException("The existing Cloudflare connection metadata is unavailable.");
var oauth = CloudflareOAuthClientConfiguration.LoadReleaseConfiguration();
if (args.Contains("--ai-production-check", StringComparer.Ordinal))
{
    if (configuration.ConnectionMode != CloudflareConnectionMode.OAuth)
        throw new InvalidOperationException("The production AI check requires the normal Desktop OAuth connection.");

    var desktopToken = await new CloudflareOAuthConnectionService().GetAccessTokenAsync(configuration, oauth)
                       ?? throw new InvalidOperationException("Fresh normal Desktop OAuth authorization is required.");
    var gateway = SocyviaAiGatewayConfiguration.LoadManagedConfiguration()
                  ?? throw new InvalidOperationException("The managed SOCYVIA AI gateway is unavailable.");
    var serviceStatus = await SocyviaAiService.GetStatusAsync();
    if (serviceStatus.State != SocyviaAiServiceState.Ready)
        throw new InvalidOperationException($"SOCYVIA AI is not ready: {serviceStatus.Reason}.");

    var provider = new SocyviaAiGatewayClient(gateway, bearerToken: desktopToken);
    var applicationState = await SocyviaAiApplicationContextService.WithoutStudyAsync("SOCYVIA AI");
    var firstPrompt = "What is SOCYVIA and how do I publish an experiment?";
    var firstRequest = ResearchInterpretationService.BuildProductHelpRequest(firstPrompt, applicationState);
    if (!AiConversationService.IsAggregateSafe(firstRequest))
        throw new InvalidOperationException("The Product Help request was not aggregate-safe.");
    var first = await provider.InterpretAsync(firstRequest);
    if (first.Status != ResearchInterpretationResponse.Generated || string.IsNullOrWhiteSpace(first.Interpretation))
        throw new InvalidOperationException("Production Product Help did not return a generated response.");

    var history = new AiConversationMessage[]
    {
        new("user", firstPrompt, DateTime.UtcNow),
        new("assistant", first.Interpretation, DateTime.UtcNow)
    };
    var followUpRequest = ResearchInterpretationService.BuildProductHelpRequest(
        "What should I check if Publish Experiment is disabled?", applicationState, history);
    if (!AiConversationService.IsAggregateSafe(followUpRequest) || followUpRequest.Conversation?.Count != 2)
        throw new InvalidOperationException("The bounded Product Help conversation context was not preserved safely.");
    var followUp = await provider.InterpretAsync(followUpRequest);
    if (followUp.Status != ResearchInterpretationResponse.Generated || string.IsNullOrWhiteSpace(followUp.Interpretation))
        throw new InvalidOperationException("Production multi-turn Product Help did not return a generated response.");

    var noEvidenceRequest = new ResearchInterpretationRequest(
        "qa-no-evidence", "No-evidence QA", "qa-no-evidence-v1", DateTime.UtcNow, 0, [],
        new DataQualityResult { TotalN = 0, IncludedN = 0, ExcludedN = 0 }, [], [],
        "Interpret this result cautiously.", [], SocyviaAiAssistantModes.ScientificInterpretation);
    var noEvidence = await provider.InterpretAsync(noEvidenceRequest);
    if (noEvidence.Status != ResearchInterpretationResponse.EvidenceUnavailable ||
        !noEvidence.Interpretation!.Contains("No participant evidence", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The n=0 scientific safeguard did not stop inference.");

    var combinedOutput = string.Join('\n', first.Interpretation, followUp.Interpretation);
    if (combinedOutput.Contains("Groq", StringComparison.OrdinalIgnoreCase) ||
        combinedOutput.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) ||
        combinedOutput.Contains("GROQ_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        first.Model is not null || followUp.Model is not null)
        throw new InvalidOperationException("The provider-neutral response boundary exposed implementation or credential material.");

    Console.WriteLine("VERIFIED - production SOCYVIA.AI/1 status is Ready through the Desktop service path.");
    Console.WriteLine("VERIFIED - Product Help returned a real generated response without participant evidence.");
    Console.WriteLine("VERIFIED - bounded multi-turn Product Help returned a follow-up response.");
    Console.WriteLine("VERIFIED - n=0 scientific interpretation was blocked before remote inference.");
    Console.WriteLine("VERIFIED - provider identity, model identity, and credential material were absent from Desktop responses.");
    return;
}
if (args.Contains("--credential-check", StringComparer.Ordinal))
{
    if (configuration.ConnectionMode != CloudflareConnectionMode.OAuth)
    {
        Console.WriteLine("BLOCKED — the current Cloudflare connection is not the normal Desktop OAuth connection.");
        Environment.ExitCode = 2;
        return;
    }
    var desktopToken = await new CloudflareOAuthConnectionService().GetAccessTokenAsync(configuration, oauth);
    if (string.IsNullOrWhiteSpace(desktopToken))
    {
        Console.WriteLine("BLOCKED — fresh normal Desktop OAuth authorization required.");
        Environment.ExitCode = 2;
        return;
    }
    Console.WriteLine("VERIFIED — a current normal Desktop OAuth authorization is available. No remote mutation was performed.");
    return;
}

if (!string.Equals(Environment.GetEnvironmentVariable("SOCYVIA_ALLOW_DISPOSABLE_PUBLICATION_QA"), "YES", StringComparison.Ordinal))
    throw new InvalidOperationException("Disposable publication QA requires SOCYVIA_ALLOW_DISPOSABLE_PUBLICATION_QA=YES.");

var qaToken = Environment.GetEnvironmentVariable("SOCYVIA_QA_CLOUDFLARE_TOKEN");
var accessToken = qaToken
                  ?? await new CloudflareOAuthConnectionService().GetAccessTokenAsync(configuration, oauth)
                  ?? throw new InvalidOperationException("No authorized Cloudflare token is available for disposable QA.");
if (!string.IsNullOrWhiteSpace(qaToken))
{
    configuration = configuration with
    {
        AccountId = Environment.GetEnvironmentVariable("SOCYVIA_QA_CLOUDFLARE_ACCOUNT")
                    ?? throw new InvalidOperationException("The disposable QA account is required."),
        D1DatabaseId = Environment.GetEnvironmentVariable("SOCYVIA_QA_CLOUDFLARE_D1")
                       ?? throw new InvalidOperationException("The disposable QA D1 database is required."),
        WorkerEndpoint = Environment.GetEnvironmentVariable("SOCYVIA_QA_CLOUDFLARE_RUNTIME")
                         ?? throw new InvalidOperationException("The disposable QA runtime is required.")
    };
}

const string imageUrl = "https://socyvia.com/experimentfeed/demo-media/sport-supporters.jpg";
const string videoUrl = "https://socyvia.com/experimentfeed/demo-media/socyvia-demo-video.mp4";
var api = new CloudflareApiClient();
var before = await CountsAsync(api, configuration, accessToken);
var pullCheckpoint = DateTime.UtcNow.AddMinutes(-1).ToString("O");
var qaStudyId = $"qa-disposable-{Guid.NewGuid():N}";
var deploymentId = string.Empty;
var publicId = string.Empty;
try
{
    const string groupId = "qa-group";
    const string conditionId = "qa-condition";
    const string imageContentId = "qa-image";
    const string videoContentId = "qa-video";
    const string preQuestionnaireId = "qa-pre";
    const string preVersionId = "qa-pre-v1";
    const string postQuestionnaireId = "qa-post";
    const string postVersionId = "qa-post-v1";
    var package = new ExperimentPackage
    {
        StudyId = qaStudyId,
        CreatedAtUtc = DateTime.UtcNow,
        Study = new("Disposable media publication QA", "Experiment", "BetweenGroups", false, 2),
        Groups = [new(groupId, "QA Group", 0, true, true)],
        Conditions = [new(conditionId, groupId, "QA Condition", "Control", 0, true, true, "{}")],
        Assignment = new("BalancedRandom", 20260825, "SOCYVIA.SplitMix64/1"),
        OrderedStimuli =
        [
            new("qa-image-stimulus", imageContentId, 0, "Image", null, imageUrl, null, groupId, conditionId),
            new("qa-video-stimulus", videoContentId, 1, "Video", null, videoUrl, null, groupId, conditionId)
        ],
        ParticipantFlow = new(false, true, true, true, "StartExperiment"),
        QuestionnaireVersions =
        [
            new(preQuestionnaireId, preVersionId, "1.0", "en"),
            new(postQuestionnaireId, postVersionId, "1.0", "en")
        ],
        RuntimeRules = new(true, true, "SOCYVIA.SplitMix64/1"),
        DefaultRuntimeLanguage = "en"
    };
    package = package with { ConfigurationHash = RemoteExperimentFoundationService.ComputeConfigurationHash(package) };
    var deployment = RemoteExperimentFoundationService.CreateDraftDeployment(package, "socyvia-qa");
    deploymentId = deployment.DeploymentId;
    if (deployment.ExperimentCode is null || deployment.ExperimentCode.Length != 8 || !deployment.ExperimentCode.All(char.IsDigit))
        throw new InvalidOperationException("The real publication pipeline did not produce an eight-digit research number.");
    publicId = CloudflareRemoteProvider.DeploymentPublicId(deployment);
    if ((await api.QueryD1RowsAsync(configuration.AccountId, configuration.D1DatabaseId, accessToken,
            $"SELECT id FROM deployments WHERE public_id='{Sql(publicId)}' LIMIT 1")).Count != 0)
        throw new InvalidOperationException("The disposable canonical identity already exists; no publication was attempted.");

    var entry = new DeploymentEntryConfiguration
    {
        ResearcherName = "SOCYVIA QA",
        ResearcherRole = "Researcher",
        StudyTitle = "Disposable media publication QA",
        StudyDescription = "Disposable public-HTTPS media and research-data round-trip validation.",
        Language = "en",
        ParticipantInterfaceLanguages = ["en", "ar"],
        DefaultParticipantInterfaceLanguage = "en",
        ConsentRequired = false,
        PreQuestionnaireConfigured = true,
        PostQuestionnaireConfigured = true
    };
    var content = new[]
    {
        new DeploymentTextContent
        {
            Id = Guid.NewGuid().ToString(), ContentId = imageContentId, ConditionId = conditionId,
            SortOrder = 0, Language = "en", Title = "Disposable QA image", Body = "Public HTTPS image validation.",
            Media = new DeploymentContentMedia("image", imageUrl, "Disposable QA image"),
            LikeEnabled = true, CommentEnabled = false, ReadMoreEnabled = true, SaveEnabled = true, ShareEnabled = true
        },
        new DeploymentTextContent
        {
            Id = Guid.NewGuid().ToString(), ContentId = videoContentId, ConditionId = conditionId,
            SortOrder = 1, Language = "en", Title = "Disposable QA video", Body = "Public HTTPS video validation.",
            Media = new DeploymentContentMedia("video", videoUrl, "Disposable QA video"),
            LikeEnabled = false, CommentEnabled = false, ReadMoreEnabled = false, SaveEnabled = false, ShareEnabled = false
        }
    };
    var questionnaires = new[]
    {
        new DeploymentQuestionnaireDefinition
        {
            Id = preQuestionnaireId, VersionId = preVersionId, Stage = QuestionnaireStage.Pre, Title = "Disposable pre measure",
            Items = [new DeploymentQuestionnaireItem { Id = "pre-score", Type = QuestionnaireItemType.Likert, Question = "Pre score", Required = true, Order = 0, ConfigurationJson = "{\"minimum\":1,\"maximum\":5}" }]
        },
        new DeploymentQuestionnaireDefinition
        {
            Id = postQuestionnaireId, VersionId = postVersionId, Stage = QuestionnaireStage.Post, Title = "Disposable post measure",
            Items = [new DeploymentQuestionnaireItem { Id = "post-score", Type = QuestionnaireItemType.Likert, Question = "Post score", Required = true, Order = 0, ConfigurationJson = "{\"minimum\":1,\"maximum\":5}" }]
        }
    };

    var publish = await new CloudflareRemoteProvider().PublishTextOnlyAsync(configuration, accessToken, package, deployment, entry, content, questionnaires);
    if (!publish.Succeeded || publish.Deployment is null)
        throw new InvalidOperationException($"Disposable publication failed at {publish.Stage}: {publish.Error}");
    var link = publish.CanonicalParticipantLink ?? throw new InvalidOperationException("Publication returned no canonical link.");
    if (!PublicExperimentLinkService.IsCanonicalRouteLive(link.RoutingStatus))
        throw new InvalidOperationException("Publication did not mark the canonical route live.");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    using var page = await http.GetAsync(link.CanonicalUri);
    if (!page.IsSuccessStatusCode || page.Headers.GetValues("x-socyvia-participant-route").SingleOrDefault() != "canonical" ||
        !(await page.Content.ReadAsStringAsync()).Contains("feed-shell.js", StringComparison.Ordinal))
        throw new InvalidOperationException("The canonical URL did not serve the rich SOCYVIA participant shell.");
    await AssertMediaAsync(http, imageUrl, "image/");
    await AssertMediaAsync(http, videoUrl, "video/");

    var participantBase = new Uri(link.CanonicalUri.AbsoluteUri.TrimEnd('/') + "/");
    var entryDto = await http.GetFromJsonAsync<JsonElement>(new Uri(participantBase, $"api/entry/socyvia-qa/{deployment.ExperimentCode}"));
    if (entryDto.GetProperty("deploymentPublicId").GetString() != publicId)
        throw new InvalidOperationException("The canonical publication did not resolve the published study definition.");
    var preDefinition = await http.GetFromJsonAsync<JsonElement>(new Uri(participantBase, $"api/questionnaires/{publicId}/PRE"));
    using var preResponse = await http.PostAsJsonAsync(new Uri(participantBase, "api/questionnaires/submit"), new
    {
        deploymentPublicId = publicId, stage = "PRE", questionnaireVersionId = preDefinition.GetProperty("versionId").GetString(),
        responseId = Guid.NewGuid(), responses = new Dictionary<string, object> { ["pre-score"] = 3 }
    });
    preResponse.EnsureSuccessStatusCode();
    var pre = await preResponse.Content.ReadFromJsonAsync<JsonElement>();
    var participantId = pre.GetProperty("participantId").GetString() ?? throw new InvalidOperationException("No QA participant was created.");
    var preSessionToken = pre.GetProperty("preSessionToken").GetString() ?? throw new InvalidOperationException("No PRE handoff token was created.");
    using var beginResponse = await http.PostAsJsonAsync(new Uri(participantBase, "api/begin"), new { deploymentPublicId = publicId, participantId, preSessionToken });
    beginResponse.EnsureSuccessStatusCode();
    var begin = await beginResponse.Content.ReadFromJsonAsync<JsonElement>();
    var sessionId = begin.GetProperty("sessionId").GetString() ?? throw new InvalidOperationException("No QA session was created.");
    var condition = begin.GetProperty("conditionId").GetString() ?? throw new InvalidOperationException("No QA condition was assigned.");
    var items = await http.GetFromJsonAsync<JsonElement>(new Uri(participantBase, $"api/session/{sessionId}/content"));
    var publishedItems = items.GetProperty("items");
    if (publishedItems.GetArrayLength() != 2 ||
        !publishedItems.EnumerateArray().Any(item => item.GetProperty("media").GetProperty("kind").GetString() == "image" && item.GetProperty("media").GetProperty("url").GetString() == imageUrl) ||
        !publishedItems.EnumerateArray().Any(item => item.GetProperty("media").GetProperty("kind").GetString() == "video" && item.GetProperty("media").GetProperty("url").GetString() == videoUrl))
        throw new InvalidOperationException("The rich participant feed did not receive the published image/video media contract.");

    var now = DateTime.UtcNow.ToString("O");
    using var events = await http.PostAsJsonAsync(new Uri(participantBase, "api/events"), new
    {
        sessionId,
        events = new object[]
        {
            new { eventId = Guid.NewGuid(), eventType = "content_impression", contentId = imageContentId, clientTimestampUtc = now, clientRelativeMilliseconds = 100L, payloadJson = "{\"qualifiedVisibleMs\":1500}", schemaVersion = "SOCYVIA.RemoteTelemetry/2" },
            new { eventId = Guid.NewGuid(), eventType = "like", contentId = imageContentId, clientTimestampUtc = now, clientRelativeMilliseconds = 200L, schemaVersion = "SOCYVIA.RemoteTelemetry/2" },
            new { eventId = Guid.NewGuid(), eventType = "content_impression", contentId = videoContentId, clientTimestampUtc = now, clientRelativeMilliseconds = 300L, payloadJson = "{\"qualifiedVisibleMs\":1200}", schemaVersion = "SOCYVIA.RemoteTelemetry/2" },
            new { eventId = Guid.NewGuid(), eventType = "experiment_feed_end", clientTimestampUtc = now, clientRelativeMilliseconds = 400L, schemaVersion = "SOCYVIA.RemoteTelemetry/2" }
        }
    });
    events.EnsureSuccessStatusCode();
    var postDefinition = await http.GetFromJsonAsync<JsonElement>(new Uri(participantBase, $"api/questionnaires/{publicId}/POST"));
    using var postResponse = await http.PostAsJsonAsync(new Uri(participantBase, "api/questionnaires/submit"), new
    {
        deploymentPublicId = publicId, stage = "POST", questionnaireVersionId = postDefinition.GetProperty("versionId").GetString(),
        responseId = Guid.NewGuid(), sessionId, responses = new Dictionary<string, object> { ["post-score"] = 4 }
    });
    postResponse.EnsureSuccessStatusCode();
    using var completed = await http.PostAsJsonAsync(new Uri(participantBase, "api/complete"), new { sessionId });
    completed.EnsureSuccessStatusCode();

    var publishedStatus = await PublishedExperimentStatusStore.GetAsync(qaStudyId)
                          ?? throw new InvalidOperationException("The Desktop publication outcome was not persisted.");
    string? copied = null;
    Uri? opened = null;
    await ParticipantLinkActionService.CopyAsync(publishedStatus, value => { copied = value; return Task.CompletedTask; });
    ParticipantLinkActionService.Open(publishedStatus, uri => opened = uri);
    if (copied != link.CanonicalUri.AbsoluteUri || opened != link.CanonicalUri)
        throw new InvalidOperationException("Copy Link or Open Experiment did not receive the canonical URL.");

    var pull = await new CloudflareRemoteProvider().PullAsync(configuration, accessToken, new RemoteSyncCursor(pullCheckpoint));
    pull = pull with
    {
        Sessions = pull.Sessions.Where(item => item.DeploymentId == deploymentId).ToArray(),
        Events = pull.Events.Where(item => item.DeploymentId == deploymentId).ToArray(),
        QuestionnaireResponses = (pull.QuestionnaireResponses ?? []).Where(item => item.DeploymentId == deploymentId).ToArray()
    };
    pull = await RemoteResearchSynchronizationService.AssociateStudyIdsAsync(pull);
    if (pull.Sessions.Count != 1 || pull.Events.Count != 4 || pull.QuestionnaireResponses?.Count != 2 ||
        pull.Sessions[0].ParticipantId != participantId || pull.Sessions[0].SessionId != sessionId || pull.Sessions[0].StudyId != qaStudyId ||
        pull.Events.Any(item => item.ParticipantId != participantId || item.SessionId != sessionId) ||
        pull.QuestionnaireResponses.Any(item => item.ParticipantId != participantId))
        throw new InvalidOperationException("Remote participant/session/event/questionnaire identities did not round-trip correctly.");
    await RemoteResearchRepository.ImportAsync(pull);
    var dataset = await RemoteAnalysisDatasetService.BuildCompletedEligibleAsync(qaStudyId);
    var quality = DataQualityService.Evaluate(dataset);
    var report = ResearchReportService.Build(new Study { Id = qaStudyId, Title = "Disposable media publication QA" }, dataset, quality, [], ["Study Overview", "Sample / Participation", "Data Quality"]);
    var preCsv = await RemoteResearchExportService.ExportQuestionnaireCsvAsync(QuestionnaireStage.Pre, studyId: qaStudyId, completedOnly: true);
    var postCsv = await RemoteResearchExportService.ExportQuestionnaireCsvAsync(QuestionnaireStage.Post, studyId: qaStudyId, completedOnly: true);
    var behaviorCsv = await RemoteResearchExportService.ExportBehavioralEventsCsvAsync(studyId: qaStudyId, completedOnly: true);
    if (dataset.Rows.Count != 1 || dataset.Rows[0].ParticipantId != participantId || dataset.Rows[0].SessionId != sessionId ||
        dataset.Rows[0].NumericValues.GetValueOrDefault("qualified_impressions") != 2 || dataset.Rows[0].NumericValues.GetValueOrDefault("likes") != 1 ||
        !dataset.Rows[0].NumericValues.ContainsKey("pre:qa-pre-v1:pre-score") || !dataset.Rows[0].NumericValues.ContainsKey("post:qa-post-v1:post-score") ||
        report.StudyId != qaStudyId || report.DatasetHash != dataset.DatasetHash ||
        !preCsv.Contains(participantId, StringComparison.Ordinal) || !postCsv.Contains(sessionId, StringComparison.Ordinal) ||
        !behaviorCsv.Contains(imageContentId, StringComparison.Ordinal) || !behaviorCsv.Contains(videoContentId, StringComparison.Ordinal))
        throw new InvalidOperationException("Desktop sync, analysis, report, or separate export verification failed.");

    Console.WriteLine($"VERIFIED disposable publication: {link.CanonicalUri}");
    Console.WriteLine($"VERIFIED media/questionnaire/session pipeline: {participantId[..8]} / {sessionId[..8]} / {condition}");
    Console.WriteLine("VERIFIED Desktop synchronization, analysis, report, and PRE/POST/behavior export provenance.");
}
finally
{
    await DeleteLocalQaCacheAsync(deploymentId);
    if (!string.IsNullOrWhiteSpace(deploymentId))
    {
        var safeId = Sql(deploymentId);
        foreach (var table in new[] { "events", "participant_questionnaire_responses", "sessions", "participants", "deployment_content", "deployment_questionnaires", "deployment_conditions", "deployment_entry_config" })
            await api.ExecuteD1StatementAsync(configuration.AccountId, configuration.D1DatabaseId, accessToken, $"DELETE FROM {table} WHERE deployment_id='{safeId}'");
        if (!string.IsNullOrWhiteSpace(publicId))
            await api.ExecuteD1StatementAsync(configuration.AccountId, configuration.D1DatabaseId, accessToken, $"DELETE FROM public_deployment_routes WHERE public_id='{Sql(publicId)}'");
        await api.ExecuteD1StatementAsync(configuration.AccountId, configuration.D1DatabaseId, accessToken, $"DELETE FROM deployments WHERE id='{safeId}'");
    }
    await PublishedExperimentStatusStore.RemoveAsync(qaStudyId);
    var after = await CountsAsync(api, configuration, accessToken);
    if (!before.SequenceEqual(after))
        throw new InvalidOperationException($"D1 preservation audit failed. Before={string.Join(',', before)} After={string.Join(',', after)}");
    Console.WriteLine("VERIFIED disposable QA cleanup and D1 preservation.");
}

static async Task AssertMediaAsync(HttpClient http, string url, string expectedTypePrefix)
{
    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not { } mediaType || !mediaType.StartsWith(expectedTypePrefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Published media did not return the expected {expectedTypePrefix} response.");
}

static async Task DeleteLocalQaCacheAsync(string deploymentId)
{
    if (string.IsNullOrWhiteSpace(deploymentId)) return;
    await RemoteResearchRepository.EnsureSchemaAsync();
    await using var connection = await DatabaseService.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    foreach (var table in new[] { "RemoteBehavioralEvents", "RemoteQuestionnaireResponses", "RemoteParticipantSessions" })
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"DELETE FROM {table} WHERE DeploymentId=$deployment";
        command.Parameters.AddWithValue("$deployment", deploymentId);
        await command.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
}

static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

static async Task<long[]> CountsAsync(CloudflareApiClient api, CloudflareProviderConfiguration configuration, string token)
{
    var rows = await api.QueryD1RowsAsync(configuration.AccountId, configuration.D1DatabaseId, token,
        "SELECT (SELECT COUNT(*) FROM deployments) deployments,(SELECT COUNT(*) FROM public_deployment_routes) routes,(SELECT COUNT(*) FROM participants) participants,(SELECT COUNT(*) FROM sessions) sessions,(SELECT COUNT(*) FROM events) events,(SELECT COUNT(*) FROM participant_questionnaire_responses) responses");
    var row = rows.Single();
    return new[] { "deployments", "routes", "participants", "sessions", "events", "responses" }
        .Select(name => long.Parse(row[name] ?? "-1", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
