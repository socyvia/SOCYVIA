using System;

namespace SOCYVIA.Models;

public class Participant
{
    // =========================================================
    // IDENTITY
    // =========================================================

    public string Id { get; set; } =
        Guid.NewGuid().ToString();


    public string StudyId { get; set; } =
        string.Empty;


    // =========================================================
    // CURRENT GROUP
    //
    // Kept for compatibility and quick access.
    //
    // Detailed assignment information is stored separately
    // in ParticipantAssignment.
    // =========================================================

    public string? GroupId { get; set; }


    // =========================================================
    // PARTICIPANT CODE
    //
    // Example:
    // P001
    // P002
    //
    // Research datasets should generally use this code instead
    // of identifying information.
    // =========================================================

    public string ParticipantCode { get; set; } =
        string.Empty;


    // =========================================================
    // STATUS
    //
    // Active
    // Ready
    // InProgress
    // Completed
    // Withdrawn
    // Excluded
    // =========================================================

    public string Status { get; set; } =
        "Active";


    // =========================================================
    // OPTIONAL DEMOGRAPHIC DATA
    //
    // These fields remain optional because each study may have
    // different ethical and methodological requirements.
    // =========================================================

    public int? Age { get; set; }


    public string? Gender { get; set; }


    public string? EducationLevel { get; set; }


    public string? Occupation { get; set; }


    // =========================================================
    // RESEARCH ELIGIBILITY
    // =========================================================

    public bool IsEligible { get; set; } =
        true;


    public string? EligibilityNotes { get; set; }


    // =========================================================
    // CONSENT
    // =========================================================

    public bool ConsentAccepted { get; set; }


    public DateTime? ConsentAcceptedAtUtc { get; set; }


    // =========================================================
    // EXPERIMENTAL PROGRESS
    // =========================================================

    public bool HasStartedStudy { get; set; }


    public bool HasCompletedStudy { get; set; }


    public DateTime? StudyStartedAtUtc { get; set; }


    public DateTime? StudyCompletedAtUtc { get; set; }


    // =========================================================
    // EXCLUSION / WITHDRAWAL
    // =========================================================

    public bool IsExcluded { get; set; }


    public string? ExclusionReason { get; set; }


    public bool HasWithdrawn { get; set; }


    public string? WithdrawalReason { get; set; }


    // =========================================================
    // RESEARCHER NOTES
    //
    // Internal only.
    // =========================================================

    public string? ResearcherNotes { get; set; }


    // =========================================================
    // FLEXIBLE METADATA
    // =========================================================

    public string? MetadataJson { get; set; }


    // =========================================================
    // TIMESTAMPS
    // =========================================================

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;


    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;
}