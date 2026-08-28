using System;

namespace SOCYVIA.Models;

public class Study
{
    // =========================================================
    // IDENTITY
    // =========================================================

    public string Id { get; set; } =
        Guid.NewGuid().ToString();


    public string ResearcherId { get; set; } =
        string.Empty;


    // =========================================================
    // BASIC INFORMATION
    // =========================================================

    public string Title { get; set; } =
        string.Empty;


    public string? Description { get; set; }


    // =========================================================
    // STUDY STATUS
    //
    // Draft
    // Ready
    // Running
    // Paused
    // Completed
    // Archived
    // =========================================================

    public string Status { get; set; } =
        "Draft";


    // =========================================================
    // STUDY DESIGN
    //
    // Experimental
    // Observational
    // Survey
    // Mixed
    // =========================================================

    public string StudyType { get; set; } =
        "Experimental";


    // =========================================================
    // EXPERIMENTAL DESIGN
    //
    // BetweenSubjects
    // WithinSubjects
    // Mixed
    // SingleGroup
    // =========================================================

    public string DesignType { get; set; } =
        "BetweenSubjects";


    // =========================================================
    // PARTICIPANT ASSIGNMENT
    //
    // Manual
    // Random
    // BalancedRandom
    // Imported
    // =========================================================

    public string AssignmentMethod { get; set; } =
        "Manual";


    // =========================================================
    // RANDOMIZATION
    // =========================================================

    public bool RandomizeStimuli { get; set; }


    public int? RandomizationSeed { get; set; }


    // =========================================================
    // STUDY MODULES
    //
    // SOCYVIA studies do not have to use every module.
    //
    // Example:
    // Questionnaire only
    // Stimuli only
    // Stimuli + questionnaire
    // Stimuli + EEG + GSR
    // =========================================================

    public bool UsesStimuli { get; set; } =
        true;


    public bool UsesQuestionnaires { get; set; }


    public bool UsesPhysiologicalData { get; set; }


    // =========================================================
    // PHYSIOLOGICAL MODULES
    //
    // Reserved for future OpenBCI / EmotiBit integration.
    // =========================================================

    public bool EegEnabled { get; set; }


    public bool GsrEnabled { get; set; }


    // =========================================================
    // PARTICIPANT PLANNING
    // =========================================================

    public int? TargetSampleSize { get; set; }


    // =========================================================
    // SESSION CONFIGURATION
    // =========================================================

    public int? ExpectedSessionDurationMinutes { get; set; }


    public bool AllowSessionResume { get; set; } =
        true;


    // =========================================================
    // ETHICS / CONSENT
    // =========================================================

    public bool RequireParticipantConsent { get; set; } =
        true;


    public string? ConsentText { get; set; }


    // =========================================================
    // RESEARCH METADATA
    // =========================================================

    public string? ResearchQuestion { get; set; }


    public string? Hypothesis { get; set; }


    public string? PopulationDescription { get; set; }


    public string? InclusionCriteria { get; set; }


    public string? ExclusionCriteria { get; set; }


    // =========================================================
    // FLEXIBLE METADATA
    //
    // Allows future SOCYVIA versions to store extra study
    // settings without continuously modifying the schema.
    // =========================================================

    public string? MetadataJson { get; set; }


    // =========================================================
    // TIMESTAMPS
    // =========================================================

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;


    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;


    public DateTime? StartedAtUtc { get; set; }


    public DateTime? CompletedAtUtc { get; set; }


    // =========================================================
    // ARCHIVE
    // =========================================================

    public bool IsArchived { get; set; }
}