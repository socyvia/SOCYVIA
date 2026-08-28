using System;

namespace SOCYVIA.Models;

public class StimulusPost
{
    // Runtime provenance for Content Library-backed stimuli. These values
    // are deliberately not persisted to the legacy Stimuli table.
    public string? ContentItemId { get; set; }

    public string? ExperimentalFeedItemId { get; set; }
    public string? EngagementObservationId { get; set; }

    public DateTime? SourceCapturedAtUtc { get; set; }

    public string? SourceMetadataJson { get; set; }

    public string? ItemManipulationJson { get; set; }

    public long? ObservedLikes { get; set; }

    public long? ObservedComments { get; set; }

    public long? ObservedShares { get; set; }

    public long? ObservedSaves { get; set; }

    // =========================================================
    // IDENTITY
    // =========================================================

    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string StudyId { get; set; } =
        string.Empty;

    // Null = visible/available to every study group.
    public string? GroupId { get; set; }


    // =========================================================
    // CONTENT
    // =========================================================

    public string Title { get; set; } =
        string.Empty;

    public string BodyText { get; set; } =
        string.Empty;


    // Text / Image / Video / Audio / Link / Mixed
    public string ContentType { get; set; } =
        "Text";


    // =========================================================
    // PRESENTATION / PLATFORM
    // =========================================================

    // Generic / Facebook / Instagram / TikTok /
    // X / News / Custom
    public string Platform { get; set; } =
        "Generic";


    // =========================================================
    // SOURCE
    // =========================================================

    public string? SourceName { get; set; }

    public string? AuthorName { get; set; }

    public string? OriginalUrl { get; set; }

    public DateTime? PublishedAtUtc { get; set; }


    // =========================================================
    // MEDIA
    // =========================================================

    public string? MediaPath { get; set; }

    public string? ThumbnailPath { get; set; }

    /// <summary>Participant-accessible HTTPS media used when this stimulus is published remotely.</summary>
    public string? PublishedMediaUrl { get; set; }


    // =========================================================
    // RESEARCH CLASSIFICATION
    // =========================================================

    public string? Category { get; set; }

    public string? Topic { get; set; }

    public string? ConditionLabel { get; set; }

    public string? ExperimentalTag { get; set; }


    // =========================================================
    // ORIGINAL POST METRICS
    //
    // These describe the stimulus/post supplied by researcher.
    // They are NOT participant behaviour.
    // =========================================================

    public int? OriginalLikes { get; set; }

    public int? OriginalComments { get; set; }

    public int? OriginalShares { get; set; }

    public int? OriginalSaves { get; set; }

    public long? OriginalViews { get; set; }


    // =========================================================
    // EXPERIMENT PRESENTATION
    // =========================================================

    public int OrderIndex { get; set; }

    public bool IsActive { get; set; } =
        true;


    // Zero means participant may continue immediately.
    public int MinimumExposureMilliseconds { get; set; }


    // Null means no forced maximum exposure.
    public int? MaximumExposureMilliseconds { get; set; }


    // =========================================================
    // RANDOMIZATION
    // =========================================================

    public bool AllowRandomization { get; set; } =
        true;


    // =========================================================
    // CUSTOM RESEARCH DATA
    // =========================================================

    public string? CustomMetadataJson { get; set; }


    // =========================================================
    // INTERNAL RESEARCHER NOTES
    // Never shown to participant.
    // =========================================================

    public string? ResearcherNotes { get; set; }


    // =========================================================
    // TIMESTAMPS
    // =========================================================

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;
}
