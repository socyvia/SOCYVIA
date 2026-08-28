using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ExperimentReadinessService
{
    public static async Task<ExperimentReadinessResult> EvaluateAsync(
        Study study)
    {
        await LegacyContentCompatibilityService
            .SynchronizeStudyAsync(study.Id);

        var groupsTask =
            GroupRepository.GetByStudyAsync(study.Id);

        var conditionsTask =
            ExperimentalConditionRepository
                .GetByStudyAsync(study.Id);

        var stimuliTask =
            StimulusPostRepository
                .GetByStudyAsync(study.Id);

        var feedItemsTask =
            ExperimentalFeedRepository
                .CountActiveItemsByStudyAsync(study.Id);

        await Task.WhenAll(
            groupsTask,
            conditionsTask,
            stimuliTask,
            feedItemsTask);

        var activeGroups =
            groupsTask.Result
                .Where(group => group.IsActive)
                .ToList();

        var activeConditions =
            conditionsTask.Result
                .Where(condition => condition.IsActive)
                .ToList();

        var activeStimuli =
            stimuliTask.Result
                .Where(stimulus => stimulus.IsActive)
                .ToList();

        var activePresentationItems =
            feedItemsTask.Result > 0
                ? feedItemsTask.Result
                : activeStimuli.Count;

        var activeGroupIds =
            activeGroups
                .Select(group => group.Id)
                .ToHashSet();

        var invalidConditionLinks =
            activeConditions
                .Where(condition =>
                    condition.GroupId is not null &&
                    !activeGroupIds.Contains(condition.GroupId))
                .ToList();

        var targetConfigured =
            study.TargetSampleSize > 0 ||
            (activeGroups.Count > 0 &&
             activeGroups.All(group =>
                 group.TargetSampleSize > 0));

        var checks =
            new List<ExperimentReadinessCheck>
            {
                Check(
                    "study.title",
                    ExperimentReadinessSeverity.Error,
                    !string.IsNullOrWhiteSpace(study.Title),
                    "Readiness.StudyTitle",
                    "Study title is configured.",
                    study.Id),

                Check(
                    "groups.active",
                    ExperimentReadinessSeverity.Error,
                    activeGroups.Count > 0,
                    "Readiness.ActiveGroup",
                    "At least one active group is required.",
                    study.Id),

                Check(
                    "conditions.active",
                    ExperimentReadinessSeverity.Error,
                    activeConditions.Count > 0,
                    "Readiness.ActiveCondition",
                    "At least one active experimental condition is required.",
                    study.Id),

                Check(
                    "stimuli.active",
                    ExperimentReadinessSeverity.Error,
                    !study.UsesStimuli || activePresentationItems > 0,
                    "Readiness.ActiveStimulus",
                    "At least one active stimulus is required when stimuli are enabled.",
                    study.Id),

                Check(
                    "conditions.links",
                    ExperimentReadinessSeverity.Error,
                    invalidConditionLinks.Count == 0,
                    "Readiness.ConditionLinks",
                    "Active conditions may only link to active groups in this study.",
                    invalidConditionLinks.FirstOrDefault()?.Id),

                Check(
                    "sample.target",
                    ExperimentReadinessSeverity.Warning,
                    targetConfigured,
                    "Readiness.TargetSample",
                    "A study-level or per-group target sample should be configured.",
                    study.Id),

                Check(
                    "assignment.method",
                    ExperimentReadinessSeverity.Error,
                    !string.IsNullOrWhiteSpace(study.AssignmentMethod),
                    "Readiness.AssignmentMethod",
                    "A participant assignment method is required.",
                    study.Id),

                Check(
                    "questionnaires.module",
                    ExperimentReadinessSeverity.Info,
                    true,
                    "Readiness.QuestionnaireModule",
                    study.UsesQuestionnaires
                        ? "Questionnaire module is enabled; detailed checks will be added later."
                        : "Questionnaire module is disabled.",
                    study.Id),

                Check(
                    "physiological.module",
                    ExperimentReadinessSeverity.Info,
                    true,
                    "Readiness.PhysiologicalModule",
                    study.UsesPhysiologicalData
                        ? "Physiological module is enabled; detailed checks will be added later."
                        : "Physiological module is disabled.",
                    study.Id)
            };

        return new ExperimentReadinessResult
        {
            Checks = checks
        };
    }


    private static ExperimentReadinessCheck Check(
        string code,
        ExperimentReadinessSeverity severity,
        bool isPassed,
        string messageKey,
        string canonicalMessage,
        string? relatedEntityId)
    {
        return new ExperimentReadinessCheck
        {
            Code = code,
            Severity = severity,
            IsPassed = isPassed,
            MessageKey = messageKey,
            CanonicalMessage = canonicalMessage,
            RelatedEntityId = relatedEntityId
        };
    }
}
