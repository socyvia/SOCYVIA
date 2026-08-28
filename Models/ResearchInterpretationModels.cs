using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SOCYVIA.Models;

/// <summary>Provider-neutral, aggregate-only input. Raw participant text and credentials are intentionally absent.</summary>
public sealed record ResearchInterpretationRequest(
    string StudyId,
    string StudyTitle,
    string DatasetHash,
    DateTime GeneratedAtUtc,
    int EligibleN,
    IReadOnlyList<string> Groups,
    DataQualityResult DataQuality,
    IReadOnlyList<AnalysisExecution> Analyses,
    IReadOnlyList<string> ResearcherLimitations,
    string? ResearcherPrompt = null,
    IReadOnlyList<AiConversationMessage>? Conversation = null,
    string AssistantMode = SocyviaAiAssistantModes.ScientificInterpretation,
    SocyviaAiProductContext? ProductContext = null,
    SocyviaAiApplicationState? ApplicationState = null);

public sealed record AiConversationMessage(string Role, string Content, DateTime CreatedAtUtc);

public sealed record AiStudyConversation(
    string Id,
    string StudyId,
    string DatasetHash,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<AiConversationMessage> Messages,
    string? Provider = null,
    string? Model = null);

public sealed record ResearchInterpretationResponse(
    string Status,
    string? Provider,
    string? Model,
    string? Interpretation,
    string InputHash,
    DateTime GeneratedAtUtc,
    IReadOnlyList<string> SafetyNotes)
{
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string Generated = "GENERATED";
    public const string EvidenceUnavailable = "EVIDENCE_UNAVAILABLE";
}

public interface IResearchInterpretationProvider
{
    string ProviderName { get; }
    Task<ResearchInterpretationResponse> InterpretAsync(ResearchInterpretationRequest request, System.Threading.CancellationToken cancellationToken = default);
}
