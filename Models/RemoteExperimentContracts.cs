using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Models;

/// <summary>Runtime-neutral, immutable publication representation. It contains no researcher UI state or analysis output.</summary>
public sealed record ExperimentPackage
{
    public string ExperimentPackageId { get; init; } = Guid.NewGuid().ToString();
    public string StudyId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = "1";
    public DateTime CreatedAtUtc { get; init; }
    public string ConfigurationHash { get; init; } = string.Empty;
    public string ConfigurationHashAlgorithm { get; init; } = "SHA-256";
    public ExperimentPackageStudyMetadata Study { get; init; } = new();
    public IReadOnlyList<ExperimentPackageGroup> Groups { get; init; } = Array.Empty<ExperimentPackageGroup>();
    public IReadOnlyList<ExperimentPackageCondition> Conditions { get; init; } = Array.Empty<ExperimentPackageCondition>();
    public ExperimentPackageAssignment Assignment { get; init; } = new("", null, "");
    public IReadOnlyList<ExperimentPackageStimulus> OrderedStimuli { get; init; } = Array.Empty<ExperimentPackageStimulus>();
    public ParticipantFlowContract ParticipantFlow { get; init; } = new();
    public IReadOnlyList<QuestionnaireVersionReference> QuestionnaireVersions { get; init; } = Array.Empty<QuestionnaireVersionReference>();
    public ExperimentRuntimeRules RuntimeRules { get; init; } = new();
    public AllowedDevicePolicyContract AllowedDevicePolicy { get; init; } = new();
    public IReadOnlyList<MediaManifestAsset> MediaManifest { get; init; } = Array.Empty<MediaManifestAsset>();
    public string TelemetrySchemaVersion { get; init; } = "SOCYVIA.RemoteTelemetry/1";
    public string DefaultRuntimeLanguage { get; init; } = "ar";
}

public sealed record ExperimentPackageStudyMetadata(
    string Title = "",
    string StudyType = "",
    string DesignType = "",
    bool ConsentRequired = true,
    int? ExpectedSessionDurationMinutes = null,
    string? StudyMetadataJson = null);

public sealed record ExperimentPackageGroup(string GroupId, string Name, int SortOrder, bool IsControlGroup, bool IsActive);
public sealed record ExperimentPackageCondition(string ConditionId, string? GroupId, string Name, string ConditionType, int SortOrder, bool IsControlCondition, bool IsActive, string? ManipulationJson);
public sealed record ExperimentPackageAssignment(string Method, int? RandomizationSeed, string AlgorithmVersion);
public sealed record ExperimentPackageStimulus(string StimulusId, string? ContentId, int PresentationOrder, string ContentType, string? MediaAssetId, string? MediaReference, string? ItemManipulationJson, string? GroupId = null, string? ConditionId = null);
public sealed record QuestionnaireVersionReference(string QuestionnaireId, string QuestionnaireVersionId, string VersionLabel, string Language);
public sealed record ParticipantFlowContract(bool ConsentRequired = true, bool PreMeasureEnabled = false, bool FeedEnabled = true, bool PostMeasureEnabled = false, string BehavioralStartBoundary = "StartExperiment");
public sealed record ExperimentRuntimeRules(bool AllowSessionResume = true, bool ShowEngagementMetrics = true, string RandomizationAlgorithm = "SOCYVIA.SplitMix64/1");
public sealed record AllowedDevicePolicyContract(string PolicyVersion = "SOCYVIA.DevicePolicy/1", bool IsPlaceholder = true, string? Notes = null);

/// <summary>Deployment copy contract; local source references remain informational until a provider creates deployment copies.</summary>
public sealed record MediaManifestAsset
{
    public string MediaAssetId { get; init; } = string.Empty;
    public string? ContentId { get; init; }
    public string MediaType { get; init; } = string.Empty;
    public string OriginalSourceType { get; init; } = string.Empty;
    public string? OriginalSourceReference { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public double? DurationSeconds { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool RequiredForDeployment { get; init; }
    public string? DeploymentObjectKey { get; init; }
    public string? DeploymentUrl { get; init; }
}

public enum ExperimentDeploymentStatus { Draft, Validated, Publishing, Published, Paused, Closed, Failed }
public enum ExperimentRunType { Main, Pilot }
public enum PilotLifecycleState { NotStarted, Running, Completed }

/// <summary>A deployment is permanently bound to the exact package ID and hash supplied at creation.</summary>
public sealed record ExperimentDeployment
{
    public string DeploymentId { get; init; } = Guid.NewGuid().ToString();
    public string StudyId { get; init; } = string.Empty;
    public string ExperimentPackageId { get; init; } = string.Empty;
    public int DeploymentVersion { get; init; } = 1;
    public DateTime CreatedAtUtc { get; init; }
    public ExperimentDeploymentStatus Status { get; init; } = ExperimentDeploymentStatus.Draft;
    public string ConfigurationHash { get; init; } = string.Empty;
    public string? ResearcherHandle { get; init; }
    public string? ExperimentCode { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
}

/// <summary>Participant-facing immutable snapshot used by text-only remote deployments before media storage is configured.</summary>
public sealed record DeploymentEntryConfiguration
{
    public string SchemaVersion { get; init; } = "SOCYVIA.DeploymentEntry/1";
    public string ResearcherName { get; init; } = string.Empty;
    public string? ResearcherRole { get; init; }
    public string? ResearcherAffiliation { get; init; }
    public string StudyTitle { get; init; } = string.Empty;
    public string? StudyDescription { get; init; }
    public string? StudyInformation { get; init; }
    public string? ParticipantInstructions { get; init; }
    public string? PrivacyText { get; init; }
    public string? EstimatedDuration { get; init; }
    /// <summary>Numeric duration is kept separate from its localized presentation unit.</summary>
    public int? EstimatedDurationMinutes { get; init; }
    public string Language { get; init; } = "en";
    /// <summary>Languages for SOCYVIA-owned participant controls. Stimulus content is never translated from this setting.</summary>
    public IReadOnlyList<string> ParticipantInterfaceLanguages { get; init; } = Array.Empty<string>();
    public string? DefaultParticipantInterfaceLanguage { get; init; }
    public bool ConsentRequired { get; init; } = true;
    public string ConsentText { get; init; } = string.Empty;
    public bool PreQuestionnaireConfigured { get; init; }
    public bool PostQuestionnaireConfigured { get; init; }
    public string? DeviceRulesJson { get; init; }
}

public sealed record DeploymentTextContent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ContentId { get; init; } = string.Empty;
    public string? ConditionId { get; init; }
    public int SortOrder { get; init; }
    public string Language { get; init; } = "en";
    public string? Title { get; init; }
    public string Body { get; init; } = string.Empty;
    public DeploymentContentMedia? Media { get; init; }
    public bool LikeEnabled { get; init; }
    public bool CommentEnabled { get; init; }
    public bool ReadMoreEnabled { get; init; }
    public bool SaveEnabled { get; init; }
    public bool ShareEnabled { get; init; }
    public bool CollectCommentText { get; init; }
}

/// <summary>Participant-safe media presentation data. Local filesystem paths never cross this boundary.</summary>
public sealed record DeploymentContentMedia(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("alt")] string? Alt = null);

/// <summary>Immutable deployment questionnaire definition. Questionnaire text never acts as its identifier.</summary>
public sealed record DeploymentQuestionnaireDefinition
{
    public string Id { get; init; } = string.Empty;
    public string VersionId { get; init; } = string.Empty;
    public QuestionnaireStage Stage { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Instructions { get; init; }
    /// <summary>Optional researcher-authored localized UI/instrument copy, keyed by BCP-47 language tag.</summary>
    public IReadOnlyDictionary<string, DeploymentQuestionnaireLocalization> Localizations { get; init; } = new Dictionary<string, DeploymentQuestionnaireLocalization>();
    public bool Required { get; init; } = true;
    public IReadOnlyList<DeploymentQuestionnaireItem> Items { get; init; } = Array.Empty<DeploymentQuestionnaireItem>();
    public string SchemaVersion { get; init; } = "SOCYVIA.Questionnaire/1";
}

public sealed record DeploymentQuestionnaireItem
{
    public string Id { get; init; } = string.Empty;
    public QuestionnaireItemType Type { get; init; }
    public string Question { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Required { get; init; }
    public int Order { get; init; }
    /// <summary>Type-specific immutable JSON, e.g. Likert range/labels or choice options.</summary>
    public string ConfigurationJson { get; init; } = "{}";
    /// <summary>Researcher-authored translations only; the participant UI must never machine-translate an item.</summary>
    public IReadOnlyDictionary<string, DeploymentQuestionnaireItemLocalization> Localizations { get; init; } = new Dictionary<string, DeploymentQuestionnaireItemLocalization>();
}

public sealed record DeploymentQuestionnaireLocalization(string Title, string? Description = null, string? Instructions = null);
public sealed record DeploymentQuestionnaireItemLocalization(string Question, string? Description = null, string? ConfigurationJson = null);

public enum QuestionnaireStage { Pre, Post }
public enum QuestionnaireItemType { Likert, SingleChoice, MultipleChoice, ShortText, LongText, Number, YesNo }

public enum RemoteParticipantCompletionState { CompletedEligible, CompletedFlagged, Incomplete, TechnicalFailure, Excluded }
public enum RemoteIngestionState { Pending, Imported, Acknowledged, Failed }
public enum RemoteParticipantLifecycleState { PreStarted, PreCompleted, SessionStarted, FeedInProgress, FeedEndReached, PostStarted, PostCompleted, Completed, Incomplete }

public sealed record RemoteParticipantSessionContract
{
    public ExperimentRunType RunType { get; init; } = ExperimentRunType.Main;
    public string ParticipantId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string StudyId { get; init; } = string.Empty;
    public string DeploymentId { get; init; } = string.Empty;
    public int DeploymentVersion { get; init; }
    public string ConditionId { get; init; } = string.Empty;
    public string? GroupId { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? FeedEndedAtUtc { get; init; }
    public DateTime? PostQuestionnaireCompletedAtUtc { get; init; }
    public RemoteParticipantLifecycleState LifecycleState { get; init; } = RemoteParticipantLifecycleState.Incomplete;
    public RemoteParticipantCompletionState CompletionState { get; init; } = RemoteParticipantCompletionState.Incomplete;
    public string? DeviceEnvironmentJson { get; init; }
    public string? ConsentVersionReference { get; init; }
    public IReadOnlyList<string> QuestionnaireResponseReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RawEventBatchReferences { get; init; } = Array.Empty<string>();
    public RemoteIngestionState IngestionState { get; init; } = RemoteIngestionState.Pending;
}

public sealed record RemoteTelemetryEvent
{
    /// <summary>Authoritative session provenance resolved by the synchronized session join.</summary>
    public ExperimentRunType RunType { get; init; } = ExperimentRunType.Main;
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public string ParticipantId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string StudyId { get; init; } = string.Empty;
    public string DeploymentId { get; init; } = string.Empty;
    public int DeploymentVersion { get; init; }
    public string ConditionId { get; init; } = string.Empty;
    public string? ContentId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTime ClientTimestampUtc { get; init; }
    public long ClientRelativeMilliseconds { get; init; }
    public string? PayloadJson { get; init; }
    public string SchemaVersion { get; init; } = "SOCYVIA.RemoteTelemetry/1";
}

public sealed record RemoteSyncCursor(string? Checkpoint = null, DateTime? LastSuccessfulSyncAtUtc = null);
public sealed record RemoteSyncPullRequest(RemoteSyncCursor Cursor, IReadOnlyList<string> KnownSessionIds);
public sealed record RemoteSyncPullResult(
    RemoteSyncCursor NextCursor,
    IReadOnlyList<RemoteParticipantSessionContract> Sessions,
    IReadOnlyList<RemoteTelemetryEvent> Events,
    IReadOnlyList<RemoteQuestionnaireResponseContract>? QuestionnaireResponses = null);

/// <summary>Normalized remote records. This remains a provider-neutral boundary for Desktop synchronization and exports.</summary>
public sealed record RemoteQuestionnaireResponseContract
{
    /// <summary>Authoritative session provenance resolved by the synchronized session join.</summary>
    public ExperimentRunType RunType { get; init; } = ExperimentRunType.Main;
    public string ResponseId { get; init; } = string.Empty;
    public string DeploymentId { get; init; } = string.Empty;
    public string ParticipantId { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string? ConditionId { get; init; }
    public string? GroupId { get; init; }
    public string QuestionnaireId { get; init; } = string.Empty;
    public string QuestionnaireVersionId { get; init; } = string.Empty;
    public QuestionnaireStage Stage { get; init; }
    public string ResponseJson { get; init; } = "{}";
    public DateTime? SubmittedAtUtc { get; init; }
}

public sealed record RemoteResearchSyncPullResult(
    RemoteSyncCursor NextCursor,
    IReadOnlyList<RemoteParticipantSessionContract> Sessions,
    IReadOnlyList<RemoteTelemetryEvent> Events,
    IReadOnlyList<RemoteQuestionnaireResponseContract> QuestionnaireResponses);

public interface IRemoteResearchDataSyncProvider
{
    Task<RemoteResearchSyncPullResult> PullResearchDataAsync(RemoteSyncCursor cursor, CancellationToken cancellationToken = default);
}
