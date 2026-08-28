namespace SOCYVIA.Models;

public class ExperimentConfigurationSummary
{
    public string StudyDesign { get; init; } = string.Empty;
    public int GroupCount { get; init; }
    public int ActiveConditionCount { get; init; }
    public string? ControlGroupName { get; init; }
    public string? ControlConditionName { get; init; }
    public int? TargetSampleSize { get; init; }
    public string AssignmentMethod { get; init; } = string.Empty;
    public int StimulusCount { get; init; }
    public bool RandomizeStimuli { get; init; }
    public bool QuestionnaireModuleEnabled { get; init; }
    public bool PhysiologicalModuleEnabled { get; init; }
}
