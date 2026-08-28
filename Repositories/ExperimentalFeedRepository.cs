using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ExperimentalFeedRepository
{
    public static async Task CreateAsync(ExperimentalFeed feed)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExperimentalFeeds
            (Id, StudyId, GroupId, ConditionId, Name, SortOrder, IsActive,
             PresentationJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES
            ($id, $studyId, $groupId, $conditionId, $name, $sortOrder,
             $isActive, $presentationJson, $createdAtUtc, $updatedAtUtc);
            """;
        AddFeedParameters(command, feed);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<ExperimentalFeed>> GetByStudyAsync(string studyId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StudyId, GroupId, ConditionId, Name, SortOrder,
                   IsActive, PresentationJson, CreatedAtUtc, UpdatedAtUtc
            FROM ExperimentalFeeds
            WHERE StudyId = $studyId
            ORDER BY SortOrder, CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$studyId", studyId);
        return await ReadFeedsAsync(command);
    }

    public static async Task<ExperimentalFeed?> GetForScopeAsync(
        string studyId,
        string? groupId,
        string? conditionId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StudyId, GroupId, ConditionId, Name, SortOrder,
                   IsActive, PresentationJson, CreatedAtUtc, UpdatedAtUtc
            FROM ExperimentalFeeds
            WHERE StudyId = $studyId
              AND GroupId IS $groupId
              AND ConditionId IS $conditionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$studyId", studyId);
        command.Parameters.AddWithValue("$groupId", Db(groupId));
        command.Parameters.AddWithValue("$conditionId", Db(conditionId));
        var feeds = await ReadFeedsAsync(command);
        return feeds.Count == 0 ? null : feeds[0];
    }

    public static async Task AddItemAsync(ExperimentalFeedItem item)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO ExperimentalFeedItems
            (Id, FeedId, ContentItemId, LegacyStimulusId, EngagementObservationId, SortOrder, IsActive,
             ItemManipulationJson, PresentationJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES
            ($id, $feedId, $contentItemId, $legacyStimulusId, $engagementObservationId, $sortOrder,
             $isActive, $itemManipulationJson, $presentationJson,
             $createdAtUtc, $updatedAtUtc);
            """;
        AddItemParameters(command, item);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<ExperimentalFeedItem>> GetItemsAsync(string feedId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FeedId, ContentItemId, LegacyStimulusId, EngagementObservationId, SortOrder,
                   IsActive, ItemManipulationJson, PresentationJson,
                   CreatedAtUtc, UpdatedAtUtc
            FROM ExperimentalFeedItems
            WHERE FeedId = $feedId
            ORDER BY SortOrder, CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$feedId", feedId);
        return await ReadItemsAsync(command);
    }

    public static async Task RemoveItemAsync(string feedItemId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ExperimentalFeedItems WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", feedItemId);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task UpdateItemOrderAsync(string id, int sortOrder)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ExperimentalFeedItems
            SET SortOrder = $sortOrder, UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<int> CountActiveItemsByStudyAsync(string studyId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM ExperimentalFeedItems fi
            JOIN ExperimentalFeeds f ON f.Id = fi.FeedId
            JOIN ContentItems c ON c.Id = fi.ContentItemId
            WHERE f.StudyId = $studyId
              AND f.IsActive = 1 AND fi.IsActive = 1 AND c.IsActive = 1;
            """;
        command.Parameters.AddWithValue("$studyId", studyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<List<ExperimentalFeed>> ReadFeedsAsync(SqliteCommand command)
    {
        var feeds = new List<ExperimentalFeed>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            feeds.Add(new ExperimentalFeed
            {
                Id = reader.GetString(0), StudyId = reader.GetString(1),
                GroupId = StringOrNull(reader, 2), ConditionId = StringOrNull(reader, 3),
                Name = reader.GetString(4), SortOrder = reader.GetInt32(5),
                IsActive = reader.GetInt32(6) == 1,
                PresentationJson = StringOrNull(reader, 7),
                CreatedAtUtc = DateTime.Parse(reader.GetString(8)),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(9))
            });
        }
        return feeds;
    }

    private static async Task<List<ExperimentalFeedItem>> ReadItemsAsync(SqliteCommand command)
    {
        var items = new List<ExperimentalFeedItem>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new ExperimentalFeedItem
            {
                Id = reader.GetString(0), FeedId = reader.GetString(1),
                ContentItemId = reader.GetString(2), LegacyStimulusId = StringOrNull(reader, 3),
                EngagementObservationId = StringOrNull(reader, 4),
                SortOrder = reader.GetInt32(5), IsActive = reader.GetInt32(6) == 1,
                ItemManipulationJson = StringOrNull(reader, 7),
                PresentationJson = StringOrNull(reader, 8),
                CreatedAtUtc = DateTime.Parse(reader.GetString(9)),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(10))
            });
        }
        return items;
    }

    private static void AddFeedParameters(SqliteCommand command, ExperimentalFeed feed)
    {
        command.Parameters.AddWithValue("$id", feed.Id);
        command.Parameters.AddWithValue("$studyId", feed.StudyId);
        command.Parameters.AddWithValue("$groupId", Db(feed.GroupId));
        command.Parameters.AddWithValue("$conditionId", Db(feed.ConditionId));
        command.Parameters.AddWithValue("$name", feed.Name);
        command.Parameters.AddWithValue("$sortOrder", feed.SortOrder);
        command.Parameters.AddWithValue("$isActive", feed.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$presentationJson", Db(feed.PresentationJson));
        command.Parameters.AddWithValue("$createdAtUtc", feed.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", feed.UpdatedAtUtc.ToString("O"));
    }

    private static void AddItemParameters(SqliteCommand command, ExperimentalFeedItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$feedId", item.FeedId);
        command.Parameters.AddWithValue("$contentItemId", item.ContentItemId);
        command.Parameters.AddWithValue("$legacyStimulusId", Db(item.LegacyStimulusId));
        command.Parameters.AddWithValue("$engagementObservationId", Db(item.EngagementObservationId));
        command.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        command.Parameters.AddWithValue("$isActive", item.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$itemManipulationJson", Db(item.ItemManipulationJson));
        command.Parameters.AddWithValue("$presentationJson", Db(item.PresentationJson));
        command.Parameters.AddWithValue("$createdAtUtc", item.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", item.UpdatedAtUtc.ToString("O"));
    }

    private static object Db(object? value) => value ?? DBNull.Value;
    private static string? StringOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);
}
