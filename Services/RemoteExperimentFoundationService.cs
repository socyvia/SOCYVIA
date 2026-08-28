using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class RemoteExperimentFoundationService
{
    public static ExperimentPackage BuildPackage(
        ExperimentConfigurationSnapshot snapshot,
        IReadOnlyList<StudyGroup> groups,
        IReadOnlyList<ExperimentalCondition> conditions,
        IReadOnlyList<QuestionnaireVersionReference>? questionnaireVersions = null,
        DateTime? createdAtUtc = null,
        string packageVersion = "1",
        string defaultRuntimeLanguage = "ar")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var media = BuildMediaManifest(snapshot.Stimuli);
        var package = new ExperimentPackage
        {
            StudyId = snapshot.StudyId,
            PackageVersion = packageVersion,
            CreatedAtUtc = (createdAtUtc ?? DateTime.UtcNow).ToUniversalTime(),
            Study = new ExperimentPackageStudyMetadata("", snapshot.StudyDesign, snapshot.StudyDesign, snapshot.ConsentRequired, snapshot.ExpectedSessionDurationMinutes, snapshot.StudyMetadataJson),
            Groups = groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ExperimentPackageGroup(item.Id, item.Name, item.SortOrder, item.IsControlGroup, item.IsActive)).ToArray(),
            Conditions = conditions.OrderBy(item => item.SortOrder).ThenBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ExperimentPackageCondition(item.Id, item.GroupId, item.Name, item.ConditionType, item.SortOrder, item.IsControlCondition, item.IsActive, item.ManipulationJson)).ToArray(),
            Assignment = new ExperimentPackageAssignment(snapshot.AssignmentMethod, snapshot.RandomizationSeed, snapshot.RandomizationAlgorithm),
            OrderedStimuli = snapshot.Stimuli.OrderBy(item => item.PresentationOrder).ThenBy(item => item.StimulusId, StringComparer.Ordinal)
                .Select(item => new ExperimentPackageStimulus(item.StimulusId, item.ContentItemId, item.PresentationOrder, item.ContentType, MediaAssetId(item), MediaReference(item), item.ItemManipulationJson, item.GroupId, item.ConditionId)).ToArray(),
            ParticipantFlow = new ParticipantFlowContract(snapshot.ConsentRequired, false, snapshot.UsesStimuli, snapshot.QuestionnaireModuleEnabled, "StartExperiment"),
            QuestionnaireVersions = (questionnaireVersions ?? Array.Empty<QuestionnaireVersionReference>()).OrderBy(item => item.QuestionnaireId, StringComparer.Ordinal).ThenBy(item => item.QuestionnaireVersionId, StringComparer.Ordinal).ToArray(),
            RuntimeRules = new ExperimentRuntimeRules(snapshot.AllowSessionResume, snapshot.ManipulationSettings.ShowEngagementMetrics, snapshot.RandomizationAlgorithm),
            MediaManifest = media,
            DefaultRuntimeLanguage = defaultRuntimeLanguage
        };
        return package with { ConfigurationHash = ComputeConfigurationHash(package) };
    }

    public static string ComputeConfigurationHash(ExperimentPackage package)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            package.StudyId, package.PackageVersion, package.Study, package.Groups, package.Conditions,
            package.Assignment, package.OrderedStimuli, package.ParticipantFlow, package.QuestionnaireVersions,
            package.RuntimeRules, package.AllowedDevicePolicy, package.MediaManifest,
            package.TelemetrySchemaVersion, package.DefaultRuntimeLanguage
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static IReadOnlyList<MediaManifestAsset> BuildMediaManifest(IEnumerable<SnapshotStimulus> stimuli) =>
        stimuli.Where(item => !string.IsNullOrWhiteSpace(MediaReference(item)))
            .Select(CreateMediaAsset)
            .GroupBy(item => item.MediaAssetId, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(item => item.MediaAssetId, StringComparer.Ordinal).ToArray();

    public static ExperimentDeployment CreateDraftDeployment(ExperimentPackage package, string? researcherHandle = null, string? experimentCode = null, DateTime? createdAtUtc = null)
    {
        var deploymentId = Guid.NewGuid().ToString();
        return new ExperimentDeployment
        {
            DeploymentId = deploymentId,
            StudyId = package.StudyId,
            ExperimentPackageId = package.ExperimentPackageId,
            ConfigurationHash = package.ConfigurationHash,
            CreatedAtUtc = (createdAtUtc ?? DateTime.UtcNow).ToUniversalTime(),
            ResearcherHandle = string.IsNullOrWhiteSpace(researcherHandle) ? null : PublicExperimentLinkService.CreateResearcherHandle(researcherHandle),
            ExperimentCode = string.IsNullOrWhiteSpace(experimentCode) ? PublicExperimentLinkService.CreateResearchNumber(deploymentId) : experimentCode,
            Status = ExperimentDeploymentStatus.Draft
        };
    }

    private static MediaManifestAsset CreateMediaAsset(SnapshotStimulus stimulus)
    {
        var path = stimulus.MediaPath ?? stimulus.ThumbnailPath ?? stimulus.PublishedMediaUrl!;
        var exists = File.Exists(path);
        Uri? directUri = null;
        var hasPublishedUrl = PublishedMediaUrlValidator.TryValidateDirectMedia(
            stimulus.PublishedMediaUrl, out var publishedUri, out _);
        var directExternal = !exists && PublishedMediaUrlValidator.TryValidateDirectMedia(path, out directUri, out _);
        var externalUri = hasPublishedUrl ? publishedUri : directExternal ? directUri : null;
        var file = exists ? new FileInfo(path) : null;
        return new MediaManifestAsset
        {
            MediaAssetId = HashId(stimulus.StimulusId, path), ContentId = stimulus.ContentItemId,
            MediaType = stimulus.ContentType,
            OriginalSourceType = externalUri is not null
                ? exists ? "LocalPreviewWithExternalUrl" : "ExternalUrl"
                : exists ? "LocalFile" : "LocalReference",
            OriginalSourceReference = path,
            FileName = exists ? Path.GetFileName(path) : Path.GetFileName(externalUri?.AbsolutePath ?? path),
            MimeType = Mime(externalUri?.AbsolutePath ?? path),
            SizeBytes = file?.Length, Sha256 = exists ? HashFile(path) : null,
            RequiredForDeployment = externalUri is null,
            DeploymentUrl = externalUri?.AbsoluteUri
        };
    }

    private static string? MediaReference(SnapshotStimulus stimulus) =>
        stimulus.MediaPath ?? stimulus.ThumbnailPath ?? stimulus.PublishedMediaUrl;
    private static string? MediaAssetId(SnapshotStimulus stimulus) => string.IsNullOrWhiteSpace(MediaReference(stimulus)) ? null : HashId(stimulus.StimulusId, MediaReference(stimulus)!);
    private static string HashId(string id, string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{value}"))).ToLowerInvariant();
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", ".mp4" => "video/mp4", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", _ => "application/octet-stream" };
}

public interface IRemoteExperimentProvider
{
    Task<ExperimentDeployment> CreateDeploymentAsync(ExperimentPackage package, CancellationToken cancellationToken = default);
    Task<ExperimentDeployment?> GetDeploymentAsync(string deploymentId, CancellationToken cancellationToken = default);
}

public interface IMediaDeploymentProvider
{
    Task<IReadOnlyList<MediaManifestAsset>> PrepareMediaAsync(ExperimentPackage package, CancellationToken cancellationToken = default);
}

public interface IRemoteTelemetryProvider
{
    Task SubmitEventsAsync(IReadOnlyList<RemoteTelemetryEvent> events, CancellationToken cancellationToken = default);
}

public interface IRemoteSessionSyncProvider
{
    Task<RemoteSyncPullResult> PullAsync(RemoteSyncPullRequest request, CancellationToken cancellationToken = default);
    Task AcknowledgeImportedSessionsAsync(IReadOnlyList<string> sessionIds, RemoteSyncCursor cursor, CancellationToken cancellationToken = default);
}
