using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ManagedMediaAssetRepository
{
    public static async Task CreateAsync(ManagedMediaAsset asset)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ManagedMediaAssets
            (Id, ResearcherId, ContentItemId, MediaKind, OriginalFileName,
             RelativePath, MimeType, ByteLength, Sha256, MetadataJson,
             IsDemo, CreatedAtUtc)
            VALUES
            ($id, $researcherId, $contentItemId, $mediaKind, $originalFileName,
             $relativePath, $mimeType, $byteLength, $sha256, $metadataJson,
             $isDemo, $createdAtUtc);
            """;
        AddParameters(command, asset);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task LinkToContentAsync(string id, string contentItemId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ManagedMediaAssets
            SET ContentItemId = $contentItemId
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$contentItemId", contentItemId);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<ManagedMediaAsset>> GetByContentAsync(
        string contentItemId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ResearcherId, ContentItemId, MediaKind, OriginalFileName,
                   RelativePath, MimeType, ByteLength, Sha256, MetadataJson,
                   IsDemo, CreatedAtUtc
            FROM ManagedMediaAssets
            WHERE ContentItemId = $contentItemId
            ORDER BY CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("$contentItemId", contentItemId);
        var assets = new List<ManagedMediaAsset>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) assets.Add(Read(reader));
        return assets;
    }

    private static ManagedMediaAsset Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ResearcherId = reader.GetString(1),
        ContentItemId = reader.IsDBNull(2) ? null : reader.GetString(2),
        MediaKind = reader.GetString(3),
        OriginalFileName = reader.GetString(4),
        RelativePath = reader.GetString(5),
        MimeType = reader.IsDBNull(6) ? null : reader.GetString(6),
        ByteLength = reader.GetInt64(7),
        Sha256 = reader.GetString(8),
        MetadataJson = reader.IsDBNull(9) ? null : reader.GetString(9),
        IsDemo = reader.GetInt32(10) == 1,
        CreatedAtUtc = DateTime.Parse(reader.GetString(11))
    };

    private static void AddParameters(SqliteCommand command, ManagedMediaAsset asset)
    {
        command.Parameters.AddWithValue("$id", asset.Id);
        command.Parameters.AddWithValue("$researcherId", asset.ResearcherId);
        command.Parameters.AddWithValue("$contentItemId", Db(asset.ContentItemId));
        command.Parameters.AddWithValue("$mediaKind", asset.MediaKind);
        command.Parameters.AddWithValue("$originalFileName", asset.OriginalFileName);
        command.Parameters.AddWithValue("$relativePath", asset.RelativePath);
        command.Parameters.AddWithValue("$mimeType", Db(asset.MimeType));
        command.Parameters.AddWithValue("$byteLength", asset.ByteLength);
        command.Parameters.AddWithValue("$sha256", asset.Sha256);
        command.Parameters.AddWithValue("$metadataJson", Db(asset.MetadataJson));
        command.Parameters.AddWithValue("$isDemo", asset.IsDemo ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", asset.CreatedAtUtc.ToString("O"));
    }

    private static object Db(object? value) => value ?? DBNull.Value;
}
