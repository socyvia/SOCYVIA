using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public enum ParticipantSessionStagePlacement
{
    BeforeExperiment,
    AfterExperiment
}

public sealed record ParticipantSessionStageContext(
    ExperimentSession Session,
    ExperimentConfigurationSnapshot Snapshot,
    ParticipantSessionStagePlacement Placement);

/// <summary>
/// Extension point for future questionnaire and other participant stages.
/// This sprint does not provide questionnaire content or scoring.
/// </summary>
public interface IParticipantSessionStageProvider
{
    string ProviderId { get; }
    Task<IReadOnlyList<ParticipantSessionStageDescriptor>> ResolveAsync(
        ParticipantSessionStageContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ParticipantSessionStageDescriptor(
    string Id,
    string StageType,
    ParticipantSessionStagePlacement Placement,
    int SortOrder,
    bool IsRequired,
    string ConfigurationJson);
