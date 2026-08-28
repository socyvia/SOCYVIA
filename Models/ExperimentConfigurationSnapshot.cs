using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SOCYVIA.Models;

public class ExperimentConfigurationSnapshot
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string SessionId { get; init; } = string.Empty;
    public string SnapshotVersion { get; init; } =
        "SOCYVIA.ExperimentConfigurationSnapshot/2";
    public string StudyId { get; init; } = string.Empty;
    public string StudyDesign { get; init; } = string.Empty;
    public string ParticipantId { get; init; } = string.Empty;
    public string ParticipantCode { get; init; } = string.Empty;
    public string? GroupId { get; init; }
    public string? GroupName { get; init; }
    public string ConditionId { get; init; } = string.Empty;
    public string ConditionAssignmentId { get; init; } = string.Empty;
    public string AssignmentMethod { get; init; } = string.Empty;
    public string ConditionName { get; init; } = string.Empty;
    public string ConditionType { get; init; } = string.Empty;
    public ConditionManipulationSettings ManipulationSettings { get; init; } =
        new();
    public int RandomizationSeed { get; init; }
    public string RandomizationAlgorithm { get; init; } =
        "SOCYVIA.SplitMix64/1";
    public List<SnapshotStimulus> Stimuli { get; init; } = new();
    public bool ConsentRequired { get; init; }
    public bool UsesStimuli { get; init; }
    public bool QuestionnaireModuleEnabled { get; init; }
    public bool PhysiologicalModuleEnabled { get; init; }
    public bool EegEnabled { get; init; }
    public bool GsrEnabled { get; init; }
    public bool AllowSessionResume { get; init; }
    public int? ExpectedSessionDurationMinutes { get; init; }
    public string? StudyMetadataJson { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? IntegrityHash { get; set; }

    [JsonIgnore]
    public string? IntegrityHashAlgorithm { get; set; }

    [JsonIgnore]
    public string? PersistedSnapshotJson { get; set; }
}


public class SnapshotStimulus
{
    public string StimulusId { get; init; } = string.Empty;
    public string? ContentItemId { get; init; }
    public string? ExperimentalFeedItemId { get; init; }
    public string? EngagementObservationId { get; init; }
    public DateTime? SourceCapturedAtUtc { get; init; }
    public string? SourceMetadataJson { get; init; }
    public string? ItemManipulationJson { get; init; }
    public int PresentationOrder { get; init; }
    public string? GroupId { get; init; }
    public string? ConditionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string BodyText { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string? SourceName { get; init; }
    public string? AuthorName { get; init; }
    public string? OriginalUrl { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public string? MediaPath { get; init; }
    public string? ThumbnailPath { get; init; }
    public string? PublishedMediaUrl { get; init; }
    public string? Category { get; init; }
    public string? Topic { get; init; }
    public string? ConditionLabel { get; init; }
    public string? ExperimentalTag { get; init; }
    public long? OriginalLikes { get; init; }
    public long? OriginalComments { get; init; }
    public long? OriginalShares { get; init; }
    public long? OriginalSaves { get; init; }
    public long? OriginalViews { get; init; }
    public int MinimumExposureMilliseconds { get; init; }
    public int? MaximumExposureMilliseconds { get; init; }
    public string? CustomMetadataJson { get; init; }
}
