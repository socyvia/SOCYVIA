using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

public enum ParticipantPresentationMode
{
    Feed,
    VerticalFeed,
    SinglePost,
    NeutralContent
}

public sealed class RuntimePostPresentation
{
    public required SnapshotStimulus Source { get; init; }
    public int PresentationOrder => Source.PresentationOrder;
    public long? Likes { get; set; }
    public long? Comments { get; init; }
    public long? Shares { get; init; }
    public long? Saves { get; init; }
    public long? Views { get; init; }
    public bool ShowAuthor { get; init; }
    public bool ShowTimestamp { get; init; }
    public bool ShowPlatformIdentity { get; init; }
    public bool IsRightToLeftContent { get; init; }
    public bool IsLiked { get; set; }
    public bool IsExpanded { get; set; }
}

public sealed class ExperimentRuntimeContext
{
    public ExperimentSession Session { get; init; } = new();
    public Participant Participant { get; init; } = new();
    public ExperimentConfigurationSnapshot Snapshot { get; init; } = new();
    public IReadOnlyList<RuntimePostPresentation> Posts { get; init; } =
        Array.Empty<RuntimePostPresentation>();
    public ParticipantPresentationMode PresentationMode { get; init; } =
        ParticipantPresentationMode.Feed;
}

public sealed class ParticipantSessionSummary
{
    public string SessionId { get; init; } = string.Empty;
    public string ParticipantCode { get; init; } = string.Empty;
    public string? GroupName { get; init; }
    public string ConditionName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long DurationMilliseconds { get; init; }
    public int StimuliExposed { get; init; }
    public int InteractionCount { get; init; }
    public long TotalExposureMilliseconds { get; init; }
}
