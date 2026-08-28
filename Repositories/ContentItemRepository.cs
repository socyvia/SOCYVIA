using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ContentItemRepository
{
    private const string SelectColumns = """
        Id, ResearcherId, LegacyStimulusId, Title, BodyText,
        ContentType, Platform, SourceName, AuthorName, OriginalUrl,
        PublishedAtUtc, CapturedAtUtc, MediaPath, ThumbnailPath,
        SourceMetadataJson, Category, Topic, Tags, ResearcherNotes,
        AcquisitionProvider, AcquisitionStatus, IsDemo, IsActive,
        CreatedAtUtc, UpdatedAtUtc, PublishedMediaUrl
        """;

    public static async Task CreateAsync(ContentItem item)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ContentItems
            (
                Id, ResearcherId, LegacyStimulusId, Title, BodyText,
                ContentType, Platform, SourceName, AuthorName, OriginalUrl,
                PublishedAtUtc, CapturedAtUtc, MediaPath, ThumbnailPath,
                SourceMetadataJson, Category, Topic, Tags, ResearcherNotes,
                AcquisitionProvider, AcquisitionStatus, IsDemo, IsActive,
                CreatedAtUtc, UpdatedAtUtc, PublishedMediaUrl
            )
            VALUES
            (
                $id, $researcherId, $legacyStimulusId, $title, $bodyText,
                $contentType, $platform, $sourceName, $authorName, $originalUrl,
                $publishedAtUtc, $capturedAtUtc, $mediaPath, $thumbnailPath,
                $sourceMetadataJson, $category, $topic, $tags, $researcherNotes,
                $acquisitionProvider, $acquisitionStatus, $isDemo, $isActive,
                $createdAtUtc, $updatedAtUtc, $publishedMediaUrl
            );
            """;
        AddParameters(command, item);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task UpdateAsync(ContentItem item)
    {
        item.UpdatedAtUtc = DateTime.UtcNow;
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ContentItems
            SET Title = $title, BodyText = $bodyText,
                ContentType = $contentType, Platform = $platform,
                SourceName = $sourceName, AuthorName = $authorName,
                OriginalUrl = $originalUrl, PublishedAtUtc = $publishedAtUtc,
                CapturedAtUtc = $capturedAtUtc, MediaPath = $mediaPath,
                ThumbnailPath = $thumbnailPath,
                SourceMetadataJson = $sourceMetadataJson,
                Category = $category, Topic = $topic, Tags = $tags,
                ResearcherNotes = $researcherNotes,
                AcquisitionProvider = $acquisitionProvider,
                AcquisitionStatus = $acquisitionStatus,
                IsDemo = $isDemo,
                IsActive = $isActive, UpdatedAtUtc = $updatedAtUtc,
                PublishedMediaUrl = $publishedMediaUrl
            WHERE Id = $id AND ResearcherId = $researcherId;
            """;
        AddParameters(command, item);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<ContentItem>> GetByResearcherAsync(
        string researcherId,
        bool includeInactive = false)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ContentItems
            WHERE ResearcherId = $researcherId
              AND ($includeInactive = 1 OR IsActive = 1)
            ORDER BY CapturedAtUtc DESC, CreatedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$researcherId", researcherId);
        command.Parameters.AddWithValue("$includeInactive", includeInactive ? 1 : 0);
        return await ReadListAsync(command);
    }

    public static async Task<ContentItem?> GetByIdAsync(string id)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ContentItems
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        var items = await ReadListAsync(command);
        return items.Count == 0 ? null : items[0];
    }

    public static async Task<int> CountByResearcherAsync(string researcherId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM ContentItems
            WHERE ResearcherId = $researcherId AND IsActive = 1;
            """;
        command.Parameters.AddWithValue("$researcherId", researcherId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<List<ContentItem>> ReadListAsync(SqliteCommand command)
    {
        var items = new List<ContentItem>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new ContentItem
            {
                Id = reader.GetString(0),
                ResearcherId = reader.GetString(1),
                LegacyStimulusId = StringOrNull(reader, 2),
                Title = reader.GetString(3),
                BodyText = reader.GetString(4),
                ContentType = reader.GetString(5),
                Platform = reader.GetString(6),
                SourceName = StringOrNull(reader, 7),
                AuthorName = StringOrNull(reader, 8),
                OriginalUrl = StringOrNull(reader, 9),
                PublishedAtUtc = DateOrNull(reader, 10),
                CapturedAtUtc = DateTime.Parse(reader.GetString(11)),
                MediaPath = StringOrNull(reader, 12),
                ThumbnailPath = StringOrNull(reader, 13),
                SourceMetadataJson = StringOrNull(reader, 14),
                Category = StringOrNull(reader, 15),
                Topic = StringOrNull(reader, 16),
                Tags = StringOrNull(reader, 17),
                ResearcherNotes = StringOrNull(reader, 18),
                AcquisitionProvider = reader.GetString(19),
                AcquisitionStatus = reader.GetString(20),
                IsDemo = reader.GetInt32(21) == 1,
                IsActive = reader.GetInt32(22) == 1,
                CreatedAtUtc = DateTime.Parse(reader.GetString(23)),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(24)),
                PublishedMediaUrl = StringOrNull(reader, 25)
            });
        }
        return items;
    }

    private static void AddParameters(SqliteCommand command, ContentItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$researcherId", item.ResearcherId);
        command.Parameters.AddWithValue("$legacyStimulusId", Db(item.LegacyStimulusId));
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$bodyText", item.BodyText);
        command.Parameters.AddWithValue("$contentType", item.ContentType);
        command.Parameters.AddWithValue("$platform", item.Platform);
        command.Parameters.AddWithValue("$sourceName", Db(item.SourceName));
        command.Parameters.AddWithValue("$authorName", Db(item.AuthorName));
        command.Parameters.AddWithValue("$originalUrl", Db(item.OriginalUrl));
        command.Parameters.AddWithValue("$publishedAtUtc", DateDb(item.PublishedAtUtc));
        command.Parameters.AddWithValue("$capturedAtUtc", item.CapturedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$mediaPath", Db(item.MediaPath));
        command.Parameters.AddWithValue("$thumbnailPath", Db(item.ThumbnailPath));
        command.Parameters.AddWithValue("$publishedMediaUrl", Db(item.PublishedMediaUrl));
        command.Parameters.AddWithValue("$sourceMetadataJson", Db(item.SourceMetadataJson));
        command.Parameters.AddWithValue("$category", Db(item.Category));
        command.Parameters.AddWithValue("$topic", Db(item.Topic));
        command.Parameters.AddWithValue("$tags", Db(item.Tags));
        command.Parameters.AddWithValue("$researcherNotes", Db(item.ResearcherNotes));
        command.Parameters.AddWithValue("$acquisitionProvider", item.AcquisitionProvider);
        command.Parameters.AddWithValue("$acquisitionStatus", item.AcquisitionStatus);
        command.Parameters.AddWithValue("$isDemo", item.IsDemo ? 1 : 0);
        command.Parameters.AddWithValue("$isActive", item.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", item.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", item.UpdatedAtUtc.ToString("O"));
    }

    private static object Db(object? value) => value ?? DBNull.Value;
    private static object DateDb(DateTime? value) =>
        value.HasValue ? value.Value.ToString("O") : DBNull.Value;
    private static string? StringOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);
    private static DateTime? DateOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : DateTime.Parse(reader.GetString(index));
}
