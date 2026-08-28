using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>AI boundary only. It never computes statistics, sends raw participant content, or fabricates a fallback response.</summary>
public static class ResearchInterpretationService
{
    public static ResearchInterpretationRequest BuildRequest(Study study, AnalysisDataset dataset, DataQualityResult quality,
        System.Collections.Generic.IReadOnlyList<AnalysisExecution> analyses,
        System.Collections.Generic.IReadOnlyList<string>? limitations = null, string? prompt = null,
        System.Collections.Generic.IReadOnlyList<AiConversationMessage>? conversation = null,
        SocyviaAiApplicationState? applicationState = null)
    {
        var mode = SocyviaAiProductKnowledgeService.DetermineMode(prompt, true, dataset.Rows.Count > 0 || analyses.Count > 0);
        return new ResearchInterpretationRequest(
            study.Id, StudyContextLabelService.ForDisplay(study.Title, LocalizationService.IsArabic), dataset.DatasetHash, DateTime.UtcNow, dataset.Rows.Count,
            dataset.Rows.Select(row => row.GroupName ?? row.ConditionName ?? "Unassigned").Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            AggregateQuality(quality), analyses, limitations ?? [], prompt, conversation, mode,
            mode == SocyviaAiAssistantModes.ScientificInterpretation ? null : SocyviaAiProductKnowledgeService.SelectRelevant(prompt, LocalizationService.IsArabic),
            applicationState);
    }

    public static ResearchInterpretationRequest BuildProductHelpRequest(
        string prompt, SocyviaAiApplicationState applicationState,
        System.Collections.Generic.IReadOnlyList<AiConversationMessage>? conversation = null)
    {
        var quality = new DataQualityResult { TotalN = 0, IncludedN = 0, ExcludedN = 0 };
        return new ResearchInterpretationRequest(
            "socyvia-product-help", "SOCYVIA", "socyvia-product-help-v1", DateTime.UtcNow, 0, [], quality, [], [],
            prompt, conversation, SocyviaAiAssistantModes.ProductHelp,
            SocyviaAiProductKnowledgeService.SelectRelevant(prompt, LocalizationService.IsArabic), applicationState);
    }

    private static DataQualityResult AggregateQuality(DataQualityResult quality) => new()
    {
        TotalN = quality.TotalN,
        IncludedN = quality.IncludedN,
        ExcludedN = quality.ExcludedN,
        MissingByVariable = quality.MissingByVariable,
        ConstantVariables = quality.ConstantVariables,
        InsufficientVariationVariables = quality.InsufficientVariationVariables,
        DuplicateParticipantWarnings = CountWarning(quality.DuplicateParticipantWarnings, "Duplicate-participant warning"),
        IncompleteQuestionnaireWarnings = CountWarning(quality.IncompleteQuestionnaireWarnings, "Incomplete-questionnaire warning"),
        SessionWarnings = CountWarning(quality.SessionWarnings, "Session-quality warning"),
        // Participant/session-level exclusion records remain local. ExcludedN is the aggregate AI context.
        Exclusions = []
    };

    private static string[] CountWarning(System.Collections.Generic.IReadOnlyList<string> values, string label) =>
        values.Count == 0 ? [] : [$"{label} count: {values.Count}"];

    public static Task<ResearchInterpretationResponse> InterpretAsync(ResearchInterpretationRequest request, IResearchInterpretationProvider? provider = null)
    {
        if (SocyviaAiScientificGuardrails.Evaluate(request) is { } blocked) return Task.FromResult(blocked);
        if (provider is not null) return provider.InterpretAsync(request);
        var hash = SocyviaAiScientificGuardrails.InputHash(request);
        return Task.FromResult(new ResearchInterpretationResponse(ResearchInterpretationResponse.NotConfigured, null, null, null, hash, DateTime.UtcNow,
            ["SOCYVIA AI is not configured.", "No interpretation was generated.", "Deterministic analysis results remain the source of truth."]));
    }
}
