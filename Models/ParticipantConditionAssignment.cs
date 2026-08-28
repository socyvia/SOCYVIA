using System;

namespace SOCYVIA.Models;

public class ParticipantConditionAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string ParticipantId { get; set; } = string.Empty;
    public string ConditionId { get; set; } = string.Empty;
    public string AssignmentMethod { get; set; } = "Manual";
    public int? RandomizationSeed { get; set; }
    public string? AssignmentMetadataJson { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
