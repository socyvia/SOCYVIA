using System;
using System.Threading.Tasks;
using SOCYVIA.Data;

namespace SOCYVIA.Services;

public static class ResearchDataClassificationService
{
    public const string DemoSource = "SOCYVIA.DEMO";

    public static async Task<bool> IsDemoAsync(string entityType, string entityId)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT IsDemo
            FROM ResearchDataClassifications
            WHERE EntityType = $type AND EntityId = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$type", entityType);
        command.Parameters.AddWithValue("$id", entityId);
        var value = await command.ExecuteScalarAsync();
        return value is not null && value is not DBNull && Convert.ToInt32(value) != 0;
    }

    public static async Task ClassifyAsync(
        string entityType,
        string entityId,
        string? studyId,
        bool isDemo,
        bool isSynthetic,
        string source)
    {
        await using var connection = await DatabaseService.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ResearchDataClassifications
            (EntityType, EntityId, StudyId, IsDemo, IsSynthetic,
             ClassificationSource, CreatedAtUtc)
            VALUES ($type, $id, $study, $demo, $synthetic, $source, $created)
            ON CONFLICT(EntityType, EntityId) DO UPDATE SET
                StudyId = excluded.StudyId,
                IsDemo = excluded.IsDemo,
                IsSynthetic = excluded.IsSynthetic,
                ClassificationSource = excluded.ClassificationSource;
            """;
        command.Parameters.AddWithValue("$type", entityType);
        command.Parameters.AddWithValue("$id", entityId);
        command.Parameters.AddWithValue("$study", studyId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$demo", isDemo ? 1 : 0);
        command.Parameters.AddWithValue("$synthetic", isSynthetic ? 1 : 0);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
