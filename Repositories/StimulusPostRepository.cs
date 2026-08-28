using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class StimulusPostRepository
{
    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        StimulusPost post)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Stimuli
            (
                Id,
                StudyId,
                GroupId,

                Name,
                StimulusType,
                Platform,

                SourceName,
                AuthorName,
                OriginalUrl,
                PublishedAtUtc,

                SourcePath,
                ThumbnailPath,
                ContentText,

                Category,
                Topic,
                ConditionLabel,
                ExperimentalTag,

                OriginalLikes,
                OriginalComments,
                OriginalShares,
                OriginalSaves,
                OriginalViews,

                SortOrder,
                IsActive,

                MinimumExposureMilliseconds,
                MaximumExposureMilliseconds,

                AllowRandomization,

                MetadataJson,
                ResearcherNotes,

                CreatedAtUtc,
                UpdatedAtUtc,
                PublishedMediaUrl
            )
            VALUES
            (
                $id,
                $studyId,
                $groupId,

                $name,
                $stimulusType,
                $platform,

                $sourceName,
                $authorName,
                $originalUrl,
                $publishedAtUtc,

                $sourcePath,
                $thumbnailPath,
                $contentText,

                $category,
                $topic,
                $conditionLabel,
                $experimentalTag,

                $originalLikes,
                $originalComments,
                $originalShares,
                $originalSaves,
                $originalViews,

                $sortOrder,
                $isActive,

                $minimumExposureMilliseconds,
                $maximumExposureMilliseconds,

                $allowRandomization,

                $metadataJson,
                $researcherNotes,

                $createdAtUtc,
                $updatedAtUtc,
                $publishedMediaUrl
            );
            """;

        AddParameters(
            command,
            post);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // GET BY STUDY
    // =========================================================

    public static async Task<List<StimulusPost>>
        GetByStudyAsync(
            string studyId)
    {
        var posts =
            new List<StimulusPost>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                GroupId,

                Name,
                StimulusType,
                Platform,

                SourceName,
                AuthorName,
                OriginalUrl,
                PublishedAtUtc,

                SourcePath,
                ThumbnailPath,
                ContentText,

                Category,
                Topic,
                ConditionLabel,
                ExperimentalTag,

                OriginalLikes,
                OriginalComments,
                OriginalShares,
                OriginalSaves,
                OriginalViews,

                SortOrder,
                IsActive,

                MinimumExposureMilliseconds,
                MaximumExposureMilliseconds,

                AllowRandomization,

                MetadataJson,
                ResearcherNotes,

                CreatedAtUtc,
                UpdatedAtUtc,
                PublishedMediaUrl

            FROM Stimuli

            WHERE StudyId = $studyId

            ORDER BY SortOrder ASC;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            posts.Add(
                ReadPost(reader));
        }

        return posts;
    }


    // =========================================================
    // GET FOR GROUP
    //
    // Includes:
    // - global posts (GroupId null)
    // - posts assigned to this group
    // =========================================================

    public static async Task<List<StimulusPost>>
        GetForGroupAsync(
            string studyId,
            string groupId)
    {
        var posts =
            new List<StimulusPost>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                GroupId,

                Name,
                StimulusType,
                Platform,

                SourceName,
                AuthorName,
                OriginalUrl,
                PublishedAtUtc,

                SourcePath,
                ThumbnailPath,
                ContentText,

                Category,
                Topic,
                ConditionLabel,
                ExperimentalTag,

                OriginalLikes,
                OriginalComments,
                OriginalShares,
                OriginalSaves,
                OriginalViews,

                SortOrder,
                IsActive,

                MinimumExposureMilliseconds,
                MaximumExposureMilliseconds,

                AllowRandomization,

                MetadataJson,
                ResearcherNotes,

                CreatedAtUtc,
                UpdatedAtUtc,
                PublishedMediaUrl

            FROM Stimuli

            WHERE StudyId = $studyId
              AND IsActive = 1
              AND
              (
                  GroupId IS NULL
                  OR GroupId = $groupId
              )

            ORDER BY SortOrder ASC;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        command.Parameters.AddWithValue(
            "$groupId",
            groupId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            posts.Add(
                ReadPost(reader));
        }

        return posts;
    }


    // =========================================================
    // UPDATE
    // =========================================================

    public static async Task UpdateAsync(
        StimulusPost post)
    {
        post.UpdatedAtUtc =
            DateTime.UtcNow;

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Stimuli
            SET
                GroupId = $groupId,

                Name = $name,
                StimulusType = $stimulusType,
                Platform = $platform,

                SourceName = $sourceName,
                AuthorName = $authorName,
                OriginalUrl = $originalUrl,
                PublishedAtUtc = $publishedAtUtc,

                SourcePath = $sourcePath,
                ThumbnailPath = $thumbnailPath,
                ContentText = $contentText,

                Category = $category,
                Topic = $topic,
                ConditionLabel = $conditionLabel,
                ExperimentalTag = $experimentalTag,

                OriginalLikes = $originalLikes,
                OriginalComments = $originalComments,
                OriginalShares = $originalShares,
                OriginalSaves = $originalSaves,
                OriginalViews = $originalViews,

                SortOrder = $sortOrder,
                IsActive = $isActive,

                MinimumExposureMilliseconds =
                    $minimumExposureMilliseconds,

                MaximumExposureMilliseconds =
                    $maximumExposureMilliseconds,

                AllowRandomization =
                    $allowRandomization,

                MetadataJson = $metadataJson,
                ResearcherNotes = $researcherNotes,

                UpdatedAtUtc = $updatedAtUtc,
                PublishedMediaUrl = $publishedMediaUrl

            WHERE Id = $id;
            """;

        AddParameters(
            command,
            post);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // DELETE
    // =========================================================

    public static async Task DeleteAsync(
        string stimulusId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            DELETE FROM Stimuli
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            stimulusId);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // PARAMETERS
    // =========================================================

    private static void AddParameters(
        SqliteCommand command,
        StimulusPost post)
    {
        command.Parameters.AddWithValue(
            "$id",
            post.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            post.StudyId);

        command.Parameters.AddWithValue(
            "$groupId",
            Db(post.GroupId));

        command.Parameters.AddWithValue(
            "$name",
            post.Title);

        command.Parameters.AddWithValue(
            "$stimulusType",
            post.ContentType);

        command.Parameters.AddWithValue(
            "$platform",
            post.Platform);

        command.Parameters.AddWithValue(
            "$sourceName",
            Db(post.SourceName));

        command.Parameters.AddWithValue(
            "$authorName",
            Db(post.AuthorName));

        command.Parameters.AddWithValue(
            "$originalUrl",
            Db(post.OriginalUrl));

        command.Parameters.AddWithValue(
            "$publishedAtUtc",
            DateDb(post.PublishedAtUtc));

        command.Parameters.AddWithValue(
            "$sourcePath",
            Db(post.MediaPath));

        command.Parameters.AddWithValue(
            "$thumbnailPath",
            Db(post.ThumbnailPath));

        command.Parameters.AddWithValue(
            "$publishedMediaUrl",
            Db(post.PublishedMediaUrl));

        command.Parameters.AddWithValue(
            "$contentText",
            Db(post.BodyText));

        command.Parameters.AddWithValue(
            "$category",
            Db(post.Category));

        command.Parameters.AddWithValue(
            "$topic",
            Db(post.Topic));

        command.Parameters.AddWithValue(
            "$conditionLabel",
            Db(post.ConditionLabel));

        command.Parameters.AddWithValue(
            "$experimentalTag",
            Db(post.ExperimentalTag));

        command.Parameters.AddWithValue(
            "$originalLikes",
            Db(post.OriginalLikes));

        command.Parameters.AddWithValue(
            "$originalComments",
            Db(post.OriginalComments));

        command.Parameters.AddWithValue(
            "$originalShares",
            Db(post.OriginalShares));

        command.Parameters.AddWithValue(
            "$originalSaves",
            Db(post.OriginalSaves));

        command.Parameters.AddWithValue(
            "$originalViews",
            Db(post.OriginalViews));

        command.Parameters.AddWithValue(
            "$sortOrder",
            post.OrderIndex);

        command.Parameters.AddWithValue(
            "$isActive",
            post.IsActive ? 1 : 0);

        command.Parameters.AddWithValue(
            "$minimumExposureMilliseconds",
            post.MinimumExposureMilliseconds);

        command.Parameters.AddWithValue(
            "$maximumExposureMilliseconds",
            Db(post.MaximumExposureMilliseconds));

        command.Parameters.AddWithValue(
            "$allowRandomization",
            post.AllowRandomization ? 1 : 0);

        command.Parameters.AddWithValue(
            "$metadataJson",
            Db(post.CustomMetadataJson));

        command.Parameters.AddWithValue(
            "$researcherNotes",
            Db(post.ResearcherNotes));

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            post.CreatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            post.UpdatedAtUtc.ToString("O"));
    }


    // =========================================================
    // READER
    // =========================================================

    private static StimulusPost ReadPost(
        SqliteDataReader reader)
    {
        return new StimulusPost
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            GroupId =
                StringOrNull(reader, 2),

            Title =
                reader.GetString(3),

            ContentType =
                reader.GetString(4),

            Platform =
                reader.GetString(5),

            SourceName =
                StringOrNull(reader, 6),

            AuthorName =
                StringOrNull(reader, 7),

            OriginalUrl =
                StringOrNull(reader, 8),

            PublishedAtUtc =
                DateOrNull(reader, 9),

            MediaPath =
                StringOrNull(reader, 10),

            ThumbnailPath =
                StringOrNull(reader, 11),

            BodyText =
                StringOrNull(reader, 12)
                ?? string.Empty,

            Category =
                StringOrNull(reader, 13),

            Topic =
                StringOrNull(reader, 14),

            ConditionLabel =
                StringOrNull(reader, 15),

            ExperimentalTag =
                StringOrNull(reader, 16),

            OriginalLikes =
                IntOrNull(reader, 17),

            OriginalComments =
                IntOrNull(reader, 18),

            OriginalShares =
                IntOrNull(reader, 19),

            OriginalSaves =
                IntOrNull(reader, 20),

            OriginalViews =
                LongOrNull(reader, 21),

            OrderIndex =
                reader.GetInt32(22),

            IsActive =
                reader.GetInt32(23) == 1,

            MinimumExposureMilliseconds =
                reader.GetInt32(24),

            MaximumExposureMilliseconds =
                IntOrNull(reader, 25),

            AllowRandomization =
                reader.GetInt32(26) == 1,

            CustomMetadataJson =
                StringOrNull(reader, 27),

            ResearcherNotes =
                StringOrNull(reader, 28),

            CreatedAtUtc =
                DateTime.Parse(
                    reader.GetString(29)),

            UpdatedAtUtc =
                reader.IsDBNull(30)
                    ? DateTime.Parse(
                        reader.GetString(29))
                    : DateTime.Parse(
                        reader.GetString(30)),

            PublishedMediaUrl =
                StringOrNull(reader, 31)
        };
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static object Db(
        object? value)
    {
        return value ?? DBNull.Value;
    }


    private static object DateDb(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("O")
            : DBNull.Value;
    }


    private static string? StringOrNull(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetString(index);
    }


    private static int? IntOrNull(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetInt32(index);
    }


    private static long? LongOrNull(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetInt64(index);
    }


    private static DateTime? DateOrNull(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : DateTime.Parse(
                reader.GetString(index));
    }
}
