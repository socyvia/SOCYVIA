using System;

namespace SOCYVIA.Models;

public class StudyGroup
{
    // =========================================================
    // IDENTITY
    // =========================================================

    public string Id { get; set; } =
        Guid.NewGuid().ToString();


    public string StudyId { get; set; } =
        string.Empty;


    // =========================================================
    // GROUP INFORMATION
    // =========================================================

    public string Name { get; set; } =
        string.Empty;


    public string? Description { get; set; }


    // =========================================================
    // VISUAL IDENTITY
    //
    // Researcher can customize the group color
    // inside Study Builder.
    // =========================================================

    public string? ColorHex { get; set; } =
        "#6259EA";


    // =========================================================
    // EXPERIMENTAL ROLE
    //
    // Allows SOCYVIA to identify a control group
    // when the researcher explicitly defines one.
    // =========================================================

    public bool IsControlGroup { get; set; } =
        false;


    // =========================================================
    // ORDER
    //
    // Keep the existing property name because it may already
    // be referenced by repositories/services.
    // =========================================================

    public int SortOrder { get; set; }


    // =========================================================
    // SAMPLE PLANNING
    //
    // Example:
    // Group A = 30
    // Group B = 30
    // Group C = 30
    //
    // Null = no predefined target.
    // =========================================================

    public int? TargetSampleSize { get; set; }


    // =========================================================
    // STATUS
    //
    // Allows disabling a group without deleting its data.
    // Important once a study has already collected data.
    // =========================================================

    public bool IsActive { get; set; } =
        true;


    // =========================================================
    // TIMESTAMPS
    // =========================================================

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;


    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;
}