using System;

namespace SOCYVIA.Models;

public class ExperimentSession
{
    // =========================================================
    // IDENTITY
    // =========================================================

    public string Id { get; set; } =
        Guid.NewGuid().ToString();


    public string StudyId { get; set; } =
        string.Empty;


    public string ParticipantId { get; set; } =
        string.Empty;


    public string? GroupId { get; set; }


    public string? ConditionId { get; set; }


    public string? ConfigurationSnapshotId { get; set; }


    // =========================================================
    // SESSION STATE
    // =========================================================

    public string Status { get; set; } =
        "Created";

    // Created
    // Running
    // Paused
    // Completed
    // Cancelled
    // Interrupted


    // =========================================================
    // TIMING
    // =========================================================

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;


    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;


    public DateTime? StartedAtUtc { get; set; }


    public DateTime? CompletedAtUtc { get; set; }


    public int LifecycleVersion { get; set; } = 2;


    // =========================================================
    // SESSION DURATION
    // =========================================================

    public long? DurationMilliseconds { get; set; }


    // =========================================================
    // EXPERIMENT PROGRESS
    // =========================================================

    public int CurrentStimulusIndex { get; set; }


    public int CompletedStimulusCount { get; set; }


    // =========================================================
    // DEVICE / ENVIRONMENT
    // =========================================================

    public string? DeviceName { get; set; }


    public string? OperatingSystem { get; set; }


    public int? ScreenWidth { get; set; }


    public int? ScreenHeight { get; set; }


    // =========================================================
    // PHYSIOLOGICAL INTEGRATION
    //
    // Reserved for future OpenBCI / EmotiBit modules.
    // =========================================================

    public bool EegEnabled { get; set; }


    public bool GsrEnabled { get; set; }


    public string? EegDeviceId { get; set; }


    public string? GsrDeviceId { get; set; }


    // =========================================================
    // SYNCHRONIZATION
    // =========================================================

    public string? SynchronizationSessionId { get; set; }


    // =========================================================
    // INTERRUPTION / QUALITY NOTES
    // =========================================================

    public bool WasInterrupted { get; set; }


    public string? InterruptionReason { get; set; }


    public string? ResearcherNotes { get; set; }


    // =========================================================
    // FLEXIBLE METADATA
    // =========================================================

    public string? MetadataJson { get; set; }
}
