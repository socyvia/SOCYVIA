using System.Collections.Generic;

namespace SOCYVIA.Models;

public enum ConditionAssignmentStrategy
{
    Manual,
    Random,
    BalancedRandom
}


public class ConditionAssignmentFailure
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? RelatedEntityId { get; init; }
}


public class ConditionAssignmentResult
{
    public bool IsSuccessful { get; init; }
    public bool WasCreated { get; init; }
    public ParticipantConditionAssignment? Assignment { get; init; }
    public ExperimentalCondition? Condition { get; init; }
    public IReadOnlyList<ConditionAssignmentFailure> Failures { get; init; } =
        new List<ConditionAssignmentFailure>();
}
