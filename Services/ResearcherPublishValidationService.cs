using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>Researcher-facing readiness checks. It deliberately returns actions, never infrastructure exceptions.</summary>
public static class ResearcherPublishValidationService
{
    public static async Task<ResearcherPublishReadiness> EvaluateStudyAsync(
        Study study,
        DeploymentEntryConfiguration entry,
        IReadOnlyList<DeploymentTextContent> content,
        IReadOnlyList<DeploymentQuestionnaireDefinition> questionnaires)
    {
        var groups = await GroupRepository.GetByStudyAsync(study.Id);
        var conditions = await ExperimentalConditionRepository.GetByStudyAsync(study.Id);
        var checks = new List<ResearcherPublishCheck>
        {
            Check("Study information", !string.IsNullOrWhiteSpace(entry.StudyTitle) && (!entry.ConsentRequired || !string.IsNullOrWhiteSpace(entry.ConsentText)), "Add a study title and required participant consent wording."),
            Check("Researcher information", !string.IsNullOrWhiteSpace(entry.ResearcherName), "Add the researcher display name."),
            Check("Participant languages", ValidLanguages(entry), "Enable at least one participant interface language and choose a default."),
            Check("Conditions", conditions.Any(item => item.IsActive), "Add at least one active experimental condition."),
            Check("Condition content", ConditionsHaveContent(conditions, content), "Each active condition needs participant content."),
            Check("PRE questionnaire", QuestionnaireValid(entry.PreQuestionnaireConfigured, QuestionnaireStage.Pre, questionnaires, entry), "Fix the PRE questionnaire or disable it."),
            Check("POST questionnaire", QuestionnaireValid(entry.PostQuestionnaireConfigured, QuestionnaireStage.Post, questionnaires, entry), "Fix the POST questionnaire or disable it.")
        };
        return new ResearcherPublishReadiness(checks);
    }

    public static async Task<ResearcherPublishReadiness> EvaluateAsync(
        Study study,
        DeploymentEntryConfiguration entry,
        IReadOnlyList<DeploymentTextContent> content,
        IReadOnlyList<DeploymentQuestionnaireDefinition> questionnaires,
        CloudflareProviderConfiguration? cloud = null,
        IReadOnlyList<MediaManifestAsset>? media = null)
    {
        var studyReadiness = await EvaluateStudyAsync(study, entry, content, questionnaires);
        var checks = studyReadiness.Checks.ToList();
        checks.Add(Check("Cloud connection", cloud?.HasRequiredTextRuntimeIdentity == true && cloud.ProviderStatus == CloudflareProviderConnectionState.Ready, "Connect and verify the researcher-owned Cloudflare workspace before publishing."));
        var mediaReadiness = await CloudMediaReadinessService.EvaluateAsync(media, cloud);
        checks.Add(Check("Media", mediaReadiness.CanPublish, mediaReadiness.Message));
        return new ResearcherPublishReadiness(checks);
    }

    private static ResearcherPublishCheck Check(string area, bool ready, string guidance) => new(area, ready, ready ? "Ready" : guidance);
    private static bool ValidLanguages(DeploymentEntryConfiguration entry)
    {
        var languages = entry.ParticipantInterfaceLanguages.Where(value => value is "en" or "ar").Distinct(StringComparer.Ordinal).ToArray();
        return languages.Length > 0 && languages.Contains(entry.DefaultParticipantInterfaceLanguage ?? entry.Language, StringComparer.Ordinal);
    }
    private static bool ConditionsHaveContent(IEnumerable<ExperimentalCondition> conditions, IReadOnlyList<DeploymentTextContent> content) => conditions.Where(item => item.IsActive).All(condition => content.Any(item => item.ConditionId == condition.Id || item.ConditionId is null));
    private static bool QuestionnaireValid(bool enabled, QuestionnaireStage stage, IReadOnlyList<DeploymentQuestionnaireDefinition> all, DeploymentEntryConfiguration entry)
    {
        if (!enabled) return true;
        var definitions = all.Where(item => item.Stage == stage).ToArray();
        if (definitions.Length == 0 || definitions.Any(definition => definition.Items.Count == 0 || definition.Items.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Question)))) return false;
        var languages = entry.ParticipantInterfaceLanguages.Distinct().ToArray();
        return definitions.All(definition => languages.All(language => language == "en" || definition.Localizations.ContainsKey(language)));
    }
}

public sealed record ResearcherPublishCheck(string Area, bool IsReady, string Message);
public sealed record ResearcherPublishReadiness(IReadOnlyList<ResearcherPublishCheck> Checks)
{
    public bool IsReady => Checks.All(item => item.IsReady);
}
