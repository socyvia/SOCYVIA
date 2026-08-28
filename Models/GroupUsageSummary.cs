namespace SOCYVIA.Models;

public class GroupUsageSummary
{
    public string GroupId { get; init; } = string.Empty;
    public int ParticipantCount { get; init; }
    public int AssignmentCount { get; init; }
    public int SessionCount { get; init; }
    public int EventCount { get; init; }
    public int StimulusCount { get; init; }
    public int ConditionCount { get; init; }

    public bool HasAnyUsage =>
        ParticipantCount > 0 ||
        AssignmentCount > 0 ||
        SessionCount > 0 ||
        EventCount > 0 ||
        StimulusCount > 0 ||
        ConditionCount > 0;

    public bool HasHistoricalResearchData =>
        ParticipantCount > 0 ||
        AssignmentCount > 0 ||
        SessionCount > 0 ||
        EventCount > 0;
}
