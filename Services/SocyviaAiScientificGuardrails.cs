using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public static class StudyContextLabelService
{
    public static string ForDisplay(string? value, bool arabic)
    {
        var text = System.Net.WebUtility.HtmlDecode(value ?? string.Empty).Trim();
        return text.Any(char.IsLetterOrDigit) ? text : arabic ? "دراسة بلا عنوان" : "Untitled Study";
    }
}

/// <summary>Deterministic preflight rules which run before any remote inference.</summary>
public static class SocyviaAiScientificGuardrails
{
    public static ResearchInterpretationResponse? Evaluate(ResearchInterpretationRequest request)
    {
        var scientific = request.AssistantMode == SocyviaAiAssistantModes.ScientificInterpretation ||
                         SocyviaAiProductKnowledgeService.ContainsScientificIntent(request.ResearcherPrompt ?? string.Empty);
        if (scientific && request.EligibleN <= 0)
            return Unavailable(request, "No participant evidence exists in the current analytical sample.");

        var prompt = request.ResearcherPrompt ?? string.Empty;
        var requestsComparison = ContainsAny(prompt, "compare", "comparison", "difference", "قارن", "مقارنة", "الفرق");
        if (scientific && requestsComparison && request.Analyses.Count == 0)
            return Unavailable(request, "The requested comparison has not been computed by SOCYVIA's deterministic analysis engine.");

        var requestsInference = ContainsAny(prompt, "p-value", "significance", "significant", "effect size", "confidence interval", "الدلالة", "قيمة p", "حجم الأثر", "فاصل الثقة");
        if (scientific && requestsInference && request.Analyses.Count == 0)
            return Unavailable(request, "No deterministic inferential result is available for this question.");

        return null;
    }

    public static string InputHash(ResearchInterpretationRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)))).ToLowerInvariant();

    private static ResearchInterpretationResponse Unavailable(ResearchInterpretationRequest request, string reason) =>
        new(ResearchInterpretationResponse.EvidenceUnavailable, "SOCYVIA AI", null, reason, InputHash(request), DateTime.UtcNow,
            ["No result was inferred or recalculated.", "Deterministic statistics remain unchanged and authoritative."]);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

#if DEBUG
/// <summary>
/// Explicit development-only adapter for exercising the Desktop conversation
/// without network inference. It is never selected by the production factory.
/// </summary>
public sealed class SocyviaAiDevelopmentAdapter : IResearchInterpretationProvider
{
    public string ProviderName => "SOCYVIA AI — DEVELOPMENT ONLY";

    public Task<ResearchInterpretationResponse> InterpretAsync(
        ResearchInterpretationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blocked = SocyviaAiScientificGuardrails.Evaluate(request);
        if (blocked is not null) return Task.FromResult(blocked);

        var answer = $"DEVELOPMENT ONLY — observed aggregate context: n={request.EligibleN}; groups={request.Groups.Count}; deterministic analyses={request.Analyses.Count}. No new statistical result was calculated.";
        return Task.FromResult(new ResearchInterpretationResponse(
            ResearchInterpretationResponse.Generated, ProviderName, null, answer,
            SocyviaAiScientificGuardrails.InputHash(request), DateTime.UtcNow,
            ["DEVELOPMENT ONLY — not production inference.", "Deterministic statistics remain unchanged and authoritative."]));
    }
}
#endif
