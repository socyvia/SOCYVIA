using System.Collections.Generic;

namespace SOCYVIA.Models;

public static class SocyviaAiAssistantModes
{
    public const string ProductHelp = "product_help";
    public const string ContextualGuidance = "contextual_guidance";
    public const string ScientificInterpretation = "scientific_interpretation";
}

/// <summary>A maintainable, provider-neutral subset of actual SOCYVIA product guidance.</summary>
public sealed record SocyviaAiProductHelpTopic(
    string Id,
    string Section,
    string Guidance,
    IReadOnlyList<string> AvailableActions);

public sealed record SocyviaAiProductContext(
    string Version,
    IReadOnlyList<SocyviaAiProductHelpTopic> Topics);

/// <summary>
/// Aggregate-safe application state. It intentionally contains no credentials,
/// cloud identifiers, participant identifiers, or raw participant responses.
/// </summary>
public sealed record SocyviaAiApplicationState(
    string CurrentSection,
    bool StudyOpen,
    string? StudyTitle,
    string? WorkflowStage,
    bool CloudflareConnected,
    bool ResearchDatabaseReady,
    bool ExperimentRuntimeReady,
    bool StudyReady,
    string? PublishBlockingReason,
    int AggregateEnrollmentCount,
    int SessionCount,
    bool AnalysisAvailable);
