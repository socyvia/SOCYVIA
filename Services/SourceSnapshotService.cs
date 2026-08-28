using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

/// <summary>
/// Freezes safe, already-acquired source metadata. Acquisition remains behind
/// the existing provider boundary; this service never bypasses authentication,
/// CAPTCHAs, or other access controls.
/// </summary>
public static class SourceSnapshotService
{
    public static SourceContentSnapshot Freeze(
        AcquiredContentMetadata metadata,
        SourceEngagementCounts? engagement = null,
        IReadOnlyList<SourceStimulusComment>? comments = null,
        DateTime? capturedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Uri.TryCreate(metadata.OriginalUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A public HTTP or HTTPS source URL is required.", nameof(metadata));

        var capturedAt = capturedAtUtc ?? DateTime.UtcNow;
        var normalizedComments = (comments ?? Array.Empty<SourceStimulusComment>())
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Id) && !string.IsNullOrWhiteSpace(comment.Text))
            .OrderBy(comment => comment.Id, StringComparer.Ordinal)
            .ToArray();
        var draft = new
        {
            schemaVersion = 1,
            originalUrl = uri.AbsoluteUri,
            metadata.Title,
            metadata.BodyText,
            metadata.AuthorName,
            metadata.SourceName,
            metadata.PublishedAtUtc,
            metadata.ThumbnailUrl,
            engagement = engagement ?? new SourceEngagementCounts(),
            comments = normalizedComments,
            metadata.SourceMetadataJson,
            capturedAt
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft)))).ToLowerInvariant();
        return new SourceContentSnapshot
        {
            OriginalUrl = uri.AbsoluteUri,
            Publisher = metadata.SourceName,
            Author = metadata.AuthorName,
            Title = metadata.Title,
            Text = metadata.BodyText,
            MediaReference = metadata.ThumbnailUrl,
            PublishedAtUtc = metadata.PublishedAtUtc,
            CapturedAtUtc = capturedAt,
            Engagement = engagement ?? new SourceEngagementCounts(),
            AvailableComments = normalizedComments,
            SourceMetadataJson = metadata.SourceMetadataJson,
            SnapshotHash = hash
        };
    }

    public static IReadOnlyList<SourceStimulusComment> SelectDisplayedComments(
        SourceContentSnapshot snapshot,
        SourceCommentPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(presentation);
        var count = Math.Max(0, presentation.DisplayCount);
        if (count == 0 || presentation.Strategy == SourceCommentSelectionStrategy.None) return Array.Empty<SourceStimulusComment>();

        var comments = snapshot.AvailableComments;
        return presentation.Strategy switch
        {
            SourceCommentSelectionStrategy.Top => comments.OrderByDescending(comment => comment.Reactions ?? long.MinValue).ThenBy(comment => comment.Id, StringComparer.Ordinal).Take(count).ToArray(),
            SourceCommentSelectionStrategy.MostRecent => comments.OrderByDescending(comment => comment.PublishedAtUtc ?? DateTime.MinValue).ThenBy(comment => comment.Id, StringComparer.Ordinal).Take(count).ToArray(),
            SourceCommentSelectionStrategy.Manual => comments.Where(comment => presentation.ManuallySelectedCommentIds?.Contains(comment.Id, StringComparer.Ordinal) == true).Take(count).ToArray(),
            SourceCommentSelectionStrategy.SeededRandom => DeterministicShuffle(comments, presentation.ReproducibleSeed ?? 0).Take(count).ToArray(),
            _ => Array.Empty<SourceStimulusComment>()
        };
    }

    private static IEnumerable<SourceStimulusComment> DeterministicShuffle(IEnumerable<SourceStimulusComment> comments, int seed)
    {
        return comments.Select(comment => new
            {
                Comment = comment,
                Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + comment.Id)))
            })
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Comment);
    }
}
