using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

/// <summary>Async provider boundary used by Desktop views; raw Cloudflare SQL never reaches a view.</summary>
public sealed class RemoteResearchSynchronizationService
{
    private readonly CloudflareRemoteProvider _provider;
    public RemoteResearchSynchronizationService(CloudflareRemoteProvider? provider = null) => _provider = provider ?? new CloudflareRemoteProvider();

    public async Task<RemoteSyncPullResult> SynchronizeAsync(CloudflareProviderConfiguration configuration, string token, RemoteSyncCursor cursor, CancellationToken cancellationToken = default)
    {
        if (!configuration.HasRequiredTextRuntimeIdentity || string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("A verified Cloudflare D1 connection is required before remote data can be synchronized.");
        var pull = await _provider.PullAsync(configuration, token, cursor, cancellationToken);
        pull = await AssociateStudyIdsAsync(pull, cancellationToken);
        await RemoteResearchRepository.ImportAsync(pull);
        await RemoteResearchRepository.SaveCursorAsync(pull.NextCursor);
        return pull;
    }

    /// <summary>
    /// Resolves legacy remote deployments which predate the immutable entry-level
    /// study identifier. New publications carry StudyId in their entry contract;
    /// this local publication mirror is the non-destructive compatibility fallback.
    /// </summary>
    public static async Task<RemoteSyncPullResult> AssociateStudyIdsAsync(RemoteSyncPullResult pull, CancellationToken cancellationToken = default)
    {
        var studyByDeployment = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in pull.Sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(session.StudyId))
            {
                studyByDeployment[session.DeploymentId] = session.StudyId;
                continue;
            }
            if (!studyByDeployment.ContainsKey(session.DeploymentId) &&
                await PublishedExperimentStatusStore.GetStudyIdByDeploymentAsync(session.DeploymentId) is { Length: > 0 } studyId)
                studyByDeployment[session.DeploymentId] = studyId;
        }

        var sessions = pull.Sessions.Select(session => string.IsNullOrWhiteSpace(session.StudyId) && studyByDeployment.TryGetValue(session.DeploymentId, out var studyId)
            ? session with { StudyId = studyId }
            : session).ToArray();
        var events = pull.Events.Select(item => string.IsNullOrWhiteSpace(item.StudyId) && studyByDeployment.TryGetValue(item.DeploymentId, out var studyId)
            ? item with { StudyId = studyId }
            : item).ToArray();
        return pull with { Sessions = sessions, Events = events };
    }
}
