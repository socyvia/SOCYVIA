using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

/// <summary>
/// An immutable, researcher-reviewed capture of public source material. It is
/// deliberately separate from participant-generated comments and from the live
/// source so a published deployment remains reproducible.
/// </summary>
public sealed record SourceContentSnapshot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int SchemaVersion { get; init; } = 1;
    public required string OriginalUrl { get; init; }
    public string? Publisher { get; init; }
    public string? Author { get; init; }
    public string? Title { get; init; }
    public string? Text { get; init; }
    public string? MediaReference { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
    public SourceEngagementCounts Engagement { get; init; } = new();
    public IReadOnlyList<SourceStimulusComment> AvailableComments { get; init; } = Array.Empty<SourceStimulusComment>();
    public string? SourceMetadataJson { get; init; }
    public required string SnapshotHash { get; init; }
}

/// <summary>Unavailable source measures remain null; zero is never used as a stand-in for unavailable data.</summary>
public sealed record SourceEngagementCounts(long? Likes = null, long? Comments = null, long? Shares = null, long? Views = null);

public sealed record SourceStimulusComment(string Id, string? Author, string Text, DateTime? PublishedAtUtc = null, long? Reactions = null);

public enum SourceCommentSelectionStrategy { None, Top, MostRecent, SeededRandom, Manual }

/// <summary>Deployment presentation choices. These never mutate the frozen source snapshot.</summary>
public sealed record SourceCommentPresentation(
    SourceCommentSelectionStrategy Strategy,
    int DisplayCount,
    IReadOnlyList<string>? ManuallySelectedCommentIds = null,
    int? ReproducibleSeed = null);
