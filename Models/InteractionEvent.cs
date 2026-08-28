using System;

namespace SOCYVIA.Models;

public class InteractionEvent
{
    // =========================================================
    // IDENTITY
    // =========================================================

    public string Id { get; set; } =
        Guid.NewGuid().ToString();


    // =========================================================
    // RELATIONSHIPS
    // =========================================================

    public string StudyId { get; set; } =
        string.Empty;


    public string SessionId { get; set; } =
        string.Empty;


    public string ParticipantId { get; set; } =
        string.Empty;


    public string? GroupId { get; set; }


    public string? ExperimentBlockId { get; set; }


    public string? StimulusPostId { get; set; }


    // Immutable stimulus identity captured by a session snapshot.
    // Unlike StimulusPostId this is intentionally not a foreign key.
    public string? SnapshotStimulusId { get; set; }


    // =========================================================
    // EVENT TYPE
    //
    // Examples:
    //
    // SessionStarted
    // SessionEnded
    //
    // PostShown
    // PostEntered
    // PostExited
    // PostHidden
    //
    // Click
    // Like
    // Unlike
    // Share
    // Save
    //
    // ScrollStarted
    // ScrollStopped
    // ScrollDepth
    //
    // VideoPlay
    // VideoPause
    // VideoEnded
    //
    // QuestionnaireOpened
    // QuestionnaireAnswered
    //
    // FocusLost
    // FocusReturned
    //
    // Custom
    // =========================================================

    public string EventType { get; set; } =
        string.Empty;


    // =========================================================
    // PRECISE TIMING
    // =========================================================

    public DateTime TimestampUtc { get; set; } =
        DateTime.UtcNow;


    // Time since session start.
    public long SessionElapsedMilliseconds { get; set; }


    // Time since current stimulus became visible.
    public long? StimulusElapsedMilliseconds { get; set; }


    public long? DurationMilliseconds { get; set; }


    // =========================================================
    // EVENT ORDER
    //
    // Critical for exact reconstruction of participant
    // behaviour inside a session.
    // =========================================================

    public int SequenceNumber { get; set; }


    // =========================================================
    // EVENT VALUES
    // =========================================================

    public string? Target { get; set; }


    public string? ValueText { get; set; }


    public string? PreviousValueText { get; set; }


    public double? ValueNumber { get; set; }


    public bool? ValueBoolean { get; set; }


    // =========================================================
    // POINTER
    // =========================================================

    public double? PointerX { get; set; }


    public double? PointerY { get; set; }


    // =========================================================
    // SCROLL
    // =========================================================

    public double? ScrollPosition { get; set; }


    public double? ScrollDepthPercent { get; set; }


    // =========================================================
    // DISPLAY CONTEXT
    // =========================================================

    public int? StimulusOrderIndex { get; set; }


    public int? ScreenWidth { get; set; }


    public int? ScreenHeight { get; set; }


    // =========================================================
    // PHYSIOLOGICAL SYNCHRONIZATION
    //
    // Future OpenBCI / EmotiBit markers.
    // =========================================================

    public string? SyncMarker { get; set; }


    // =========================================================
    // FLEXIBLE EVENT METADATA
    // =========================================================

    public string? MetadataJson { get; set; }
}
