namespace SOCYVIA.Services;

public enum PublicationWorkspaceState
{
    NotReady,
    Ready,
    Publishing,
    Failed,
    Published,
    PublishedAwaitingCanonicalRoute
}

/// <summary>
/// Pure presentation state derived from the same authoritative readiness and
/// persisted remote publication evidence used by the publish command.
/// </summary>
public static class PublicationWorkspaceStateService
{
    public static PublicationWorkspaceState Resolve(
        PublicationReadinessResult readiness,
        PublishedExperimentStatus? publication,
        bool currentConfiguration,
        bool isPublishing,
        string? failure)
    {
        if (isPublishing) return PublicationWorkspaceState.Publishing;
        if (!string.IsNullOrWhiteSpace(failure)) return PublicationWorkspaceState.Failed;
        if (publication is not null && currentConfiguration)
            return PublicExperimentLinkService.IsCanonicalRouteLive(publication.RoutingStatus)
                ? PublicationWorkspaceState.Published
                : PublicationWorkspaceState.PublishedAwaitingCanonicalRoute;
        return readiness.CanPublish ? PublicationWorkspaceState.Ready : PublicationWorkspaceState.NotReady;
    }
}
