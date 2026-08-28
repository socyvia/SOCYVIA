using System;

namespace SOCYVIA.Models;

public class ExperimentalCondition
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString();

    public string StudyId { get; set; } =
        string.Empty;

    public string? GroupId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public string? Description { get; set; }

    public string ConditionType { get; set; } =
        "Custom";

    public int SortOrder { get; set; }

    public bool IsControlCondition { get; set; }

    public bool IsActive { get; set; } =
        true;

    public string? ManipulationJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;
}
