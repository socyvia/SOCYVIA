using System;

namespace SOCYVIA.Models;

public class ParticipantAssignment
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


    public string GroupId { get; set; } =
        string.Empty;


    // =========================================================
    // ASSIGNMENT METHOD
    //
    // Manual
    // Random
    // BalancedRandom
    // Imported
    // =========================================================

    public string AssignmentMethod { get; set; } =
        "Manual";


    // =========================================================
    // RANDOMIZATION INFO
    // =========================================================

    public int? RandomizationSeed { get; set; }


    public int? AssignmentOrder { get; set; }


    // =========================================================
    // STATUS
    // =========================================================

    public bool IsActive { get; set; } =
        true;


    // =========================================================
    // TIMESTAMP
    // =========================================================

    public DateTime AssignedAtUtc { get; set; } =
        DateTime.UtcNow;


    // =========================================================
    // RESEARCHER NOTES
    // =========================================================

    public string? Notes { get; set; }
}