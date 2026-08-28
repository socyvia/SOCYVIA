using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ExperimentFeedService
{
    public static async Task<ExperimentalFeed> GetOrCreateAsync(
        Study study,
        StudyGroup? group,
        ExperimentalCondition? condition)
    {
        var existing = await ExperimentalFeedRepository.GetForScopeAsync(
            study.Id, group?.Id, condition?.Id);
        if (existing is not null)
        {
            return existing;
        }

        var feed = new ExperimentalFeed
        {
            StudyId = study.Id,
            GroupId = group?.Id,
            ConditionId = condition?.Id,
            Name = condition?.Name ?? group?.Name ?? "All participants",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await ExperimentalFeedRepository.CreateAsync(feed);
        return feed;
    }

    public static async Task AddContentAsync(
        ExperimentalFeed feed,
        ContentItem content)
    {
        var current = await ExperimentalFeedRepository.GetItemsAsync(feed.Id);
        if (current.Any(item => item.ContentItemId == content.Id))
        {
            return;
        }

        var observation = await EngagementObservationRepository.GetLatestAsync(content.Id);
        await ExperimentalFeedRepository.AddItemAsync(new ExperimentalFeedItem
        {
            FeedId = feed.Id,
            ContentItemId = content.Id,
            LegacyStimulusId = content.LegacyStimulusId,
            EngagementObservationId = observation?.Id,
            SortOrder = current.Count,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    public static Task RemoveContentAsync(string feedItemId) =>
        ExperimentalFeedRepository.RemoveItemAsync(feedItemId);

    public static async Task MoveAsync(
        string feedId,
        string feedItemId,
        int direction)
    {
        var items = await ExperimentalFeedRepository.GetItemsAsync(feedId);
        var index = items.FindIndex(item => item.Id == feedItemId);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        for (var order = 0; order < items.Count; order++)
        {
            await ExperimentalFeedRepository
                .UpdateItemOrderAsync(items[order].Id, order);
        }
    }

    public static async Task<List<ResolvedExperimentalFeedItem>> ResolveAsync(
        Study study,
        StudyGroup group,
        ExperimentalCondition condition)
    {
        await LegacyContentCompatibilityService
            .SynchronizeStudyAsync(study.Id);

        var feeds = (await ExperimentalFeedRepository.GetByStudyAsync(study.Id))
            .Where(feed => feed.IsActive &&
                (feed.GroupId is null || feed.GroupId == group.Id) &&
                (feed.ConditionId is null || feed.ConditionId == condition.Id))
            .OrderBy(feed => ScopeRank(feed, group.Id, condition.Id))
            .ThenBy(feed => feed.SortOrder)
            .ToList();

        var result = new List<ResolvedExperimentalFeedItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            foreach (var feedItem in await ExperimentalFeedRepository.GetItemsAsync(feed.Id))
            {
                if (!feedItem.IsActive || !seen.Add(feedItem.ContentItemId))
                {
                    continue;
                }
                var content = await ContentItemRepository.GetByIdAsync(feedItem.ContentItemId);
                if (content is null || !content.IsActive)
                {
                    continue;
                }
                result.Add(new ResolvedExperimentalFeedItem
                {
                    Feed = feed,
                    FeedItem = feedItem,
                    Content = content,
                    Observation = feedItem.EngagementObservationId is { Length: > 0 }
                        ? await EngagementObservationRepository.GetByIdAsync(feedItem.EngagementObservationId)
                        : await EngagementObservationRepository.GetLatestAsync(content.Id)
                });
            }
        }
        return result;
    }

    public static async Task<List<StimulusPost>> ResolveStimuliAsync(
        Study study,
        StudyGroup group,
        ExperimentalCondition condition)
    {
        var resolved = await ResolveAsync(study, group, condition);
        if (resolved.Count == 0)
        {
            return (await StimulusPostRepository.GetForGroupAsync(study.Id, group.Id))
                .Where(item => item.IsActive)
                .ToList();
        }

        return resolved.Select((item, index) => ToStimulus(item, study.Id, group.Id, index))
            .ToList();
    }

    private static StimulusPost ToStimulus(
        ResolvedExperimentalFeedItem resolved,
        string studyId,
        string groupId,
        int fallbackOrder)
    {
        var content = resolved.Content;
        var observation = resolved.Observation;
        return new StimulusPost
        {
            Id = content.Id,
            ContentItemId = content.Id,
            ExperimentalFeedItemId = resolved.FeedItem.Id,
            EngagementObservationId = observation?.Id,
            SourceCapturedAtUtc = observation?.CapturedAtUtc ?? content.CapturedAtUtc,
            SourceMetadataJson = content.SourceMetadataJson,
            ItemManipulationJson = resolved.FeedItem.ItemManipulationJson,
            StudyId = studyId,
            GroupId = groupId,
            Title = content.Title,
            BodyText = content.BodyText,
            ContentType = content.ContentType,
            Platform = content.Platform,
            SourceName = content.SourceName,
            AuthorName = content.AuthorName,
            OriginalUrl = content.OriginalUrl,
            PublishedAtUtc = content.PublishedAtUtc,
            MediaPath = content.MediaPath,
            ThumbnailPath = content.ThumbnailPath,
            PublishedMediaUrl = content.PublishedMediaUrl,
            Category = content.Category,
            Topic = content.Topic,
            ExperimentalTag = content.Tags,
            ObservedLikes = observation?.Likes,
            ObservedComments = observation?.Comments,
            ObservedShares = observation?.Shares,
            ObservedSaves = observation?.Saves,
            OriginalLikes = ToInt(observation?.Likes),
            OriginalComments = ToInt(observation?.Comments),
            OriginalShares = ToInt(observation?.Shares),
            OriginalSaves = ToInt(observation?.Saves),
            OriginalViews = observation?.Views,
            OrderIndex = resolved.FeedItem.SortOrder == 0
                ? fallbackOrder
                : resolved.FeedItem.SortOrder,
            IsActive = content.IsActive,
            CustomMetadataJson = resolved.FeedItem.PresentationJson,
            ResearcherNotes = content.ResearcherNotes,
            CreatedAtUtc = content.CreatedAtUtc,
            UpdatedAtUtc = content.UpdatedAtUtc
        };
    }

    private static int ScopeRank(
        ExperimentalFeed feed,
        string groupId,
        string conditionId)
    {
        var groupMatch = feed.GroupId == groupId;
        var conditionMatch = feed.ConditionId == conditionId;
        return (groupMatch, conditionMatch) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => 2,
            _ => 3
        };
    }

    private static int? ToInt(long? value) => value switch
    {
        null => null,
        > int.MaxValue => int.MaxValue,
        < int.MinValue => int.MinValue,
        _ => (int)value.Value
    };
}
