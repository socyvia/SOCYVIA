using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class EngagementObservationRepository
{
    public static async Task CreateAsync(EngagementObservation observation)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EngagementObservations
            (Id, ContentItemId, Likes, Comments, Shares, Saves, Views,
             CapturedAtUtc, ObservationSource, SourceMetadataJson)
            VALUES
            ($id, $contentItemId, $likes, $comments, $shares, $saves, $views,
             $capturedAtUtc, $observationSource, $sourceMetadataJson);
            """;
        AddParameters(command, observation);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<EngagementObservation?> GetLatestAsync(
        string contentItemId)
    {
        var observations = await GetAsync(contentItemId, 1);
        return observations.Count == 0 ? null : observations[0];
    }

    public static Task<List<EngagementObservation>> GetHistoryAsync(
        string contentItemId) => GetAsync(contentItemId, null);

    public static async Task<EngagementObservation?> GetByIdAsync(string id)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ContentItemId, Likes, Comments, Shares, Saves, Views,
                   CapturedAtUtc, ObservationSource, SourceMetadataJson
            FROM EngagementObservations
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    private static async Task<List<EngagementObservation>> GetAsync(
        string contentItemId,
        int? limit)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ContentItemId, Likes, Comments, Shares, Saves, Views,
                   CapturedAtUtc, ObservationSource, SourceMetadataJson
            FROM EngagementObservations
            WHERE ContentItemId = $contentItemId
            ORDER BY CapturedAtUtc DESC, Id DESC
            """ + (limit.HasValue ? " LIMIT 1;" : ";");
        command.Parameters.AddWithValue("$contentItemId", contentItemId);
        var results = new List<EngagementObservation>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(Read(reader));
        }
        return results;
    }

    private static void AddParameters(SqliteCommand command, EngagementObservation item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$contentItemId", item.ContentItemId);
        command.Parameters.AddWithValue("$likes", Db(item.Likes));
        command.Parameters.AddWithValue("$comments", Db(item.Comments));
        command.Parameters.AddWithValue("$shares", Db(item.Shares));
        command.Parameters.AddWithValue("$saves", Db(item.Saves));
        command.Parameters.AddWithValue("$views", Db(item.Views));
        command.Parameters.AddWithValue("$capturedAtUtc", item.CapturedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$observationSource", item.ObservationSource);
        command.Parameters.AddWithValue("$sourceMetadataJson", Db(item.SourceMetadataJson));
    }

    private static object Db(object? value) => value ?? DBNull.Value;
    private static long? LongOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetInt64(index);

    private static EngagementObservation Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ContentItemId = reader.GetString(1),
        Likes = LongOrNull(reader, 2),
        Comments = LongOrNull(reader, 3),
        Shares = LongOrNull(reader, 4),
        Saves = LongOrNull(reader, 5),
        Views = LongOrNull(reader, 6),
        CapturedAtUtc = DateTime.Parse(reader.GetString(7)),
        ObservationSource = reader.GetString(8),
        SourceMetadataJson = reader.IsDBNull(9) ? null : reader.GetString(9)
    };
}
