using System;

namespace SOCYVIA.Models;

public sealed class ExperimentalFeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public string? ConditionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? PresentationJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ExperimentalFeedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FeedId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string? LegacyStimulusId { get; set; }
    public string? EngagementObservationId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ItemManipulationJson { get; set; }
    public string? PresentationJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}


public sealed class ResolvedExperimentalFeedItem
{
    public ExperimentalFeed Feed { get; init; } = new();
    public ExperimentalFeedItem FeedItem { get; init; } = new();
    public ContentItem Content { get; init; } = new();
    public EngagementObservation? Observation { get; init; }
}
