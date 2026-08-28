using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ExperimentConfigurationService
{
    public static async Task<ExperimentConfigurationSummary>
        BuildSummaryAsync(
            Study study)
    {
        var groupsTask =
            GroupRepository.GetByStudyAsync(study.Id);

        var conditionsTask =
            ExperimentalConditionRepository
                .GetActiveByStudyAsync(study.Id);

        var stimuliTask =
            StimulusPostRepository
                .GetByStudyAsync(study.Id);

        await Task.WhenAll(
            groupsTask,
            conditionsTask,
            stimuliTask);

        var groups = groupsTask.Result;
        var conditions = conditionsTask.Result;
        var stimuli = stimuliTask.Result;

        var activeGroups =
            groups.Where(group => group.IsActive).ToList();

        var targetSample =
            study.TargetSampleSize ??
            (activeGroups.Count > 0 &&
             activeGroups.All(group => group.TargetSampleSize.HasValue)
                ? activeGroups.Sum(group => group.TargetSampleSize!.Value)
                : null);

        return new ExperimentConfigurationSummary
        {
            StudyDesign = study.DesignType,
            GroupCount = groups.Count,
            ActiveConditionCount = conditions.Count,
            ControlGroupName = groups
                .FirstOrDefault(group => group.IsControlGroup)?.Name,
            ControlConditionName = conditions
                .FirstOrDefault(condition =>
                    condition.IsControlCondition)?.Name,
            TargetSampleSize = targetSample,
            AssignmentMethod = study.AssignmentMethod,
            StimulusCount = stimuli.Count,
            RandomizeStimuli = study.RandomizeStimuli,
            QuestionnaireModuleEnabled = study.UsesQuestionnaires,
            PhysiologicalModuleEnabled =
                study.UsesPhysiologicalData ||
                study.EegEnabled ||
                study.GsrEnabled
        };
    }
}
