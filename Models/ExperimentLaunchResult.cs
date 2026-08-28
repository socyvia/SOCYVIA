using System.Collections.Generic;

namespace SOCYVIA.Models;

public class ExperimentLaunchRequest
{
    public string StudyId { get; init; } = string.Empty;
    public string ParticipantId { get; init; } = string.Empty;
    public ConditionAssignmentStrategy AssignmentStrategy { get; init; } =
        ConditionAssignmentStrategy.Manual;
    public string? ManualConditionId { get; init; }
    public int? RandomizationSeed { get; init; }
}


public class ExperimentLaunchFailure
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? RelatedEntityId { get; init; }
}


public class ExperimentLaunchContext
{
    public Study Study { get; init; } = new();
    public Participant Participant { get; init; } = new();
    public StudyGroup Group { get; init; } = new();
    public ExperimentalCondition Condition { get; init; } = new();
    public ParticipantConditionAssignment ConditionAssignment { get; init; } =
        new();
    public ExperimentSession Session { get; init; } = new();
    public ExperimentConfigurationSnapshot Snapshot { get; init; } = new();
    public IReadOnlyList<StimulusPost> ResolvedStimuli { get; init; } =
        new List<StimulusPost>();
    public ConditionManipulationSettings ManipulationSettings { get; init; } =
        new();
    public ExperimentReadinessResult Readiness { get; init; } = new();
}


public class ExperimentLaunchResult
{
    public bool IsSuccessful { get; init; }
    public ExperimentLaunchContext? Context { get; init; }
    public ExperimentReadinessResult? Readiness { get; init; }
    public IReadOnlyList<ExperimentLaunchFailure> Failures { get; init; } =
        new List<ExperimentLaunchFailure>();
}
