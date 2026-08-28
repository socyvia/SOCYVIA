using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ExperimentalConditionRepository
{
    public static async Task CreateAsync(
        ExperimentalCondition condition)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO ExperimentalConditions
            (
                Id,
                StudyId,
                GroupId,
                Name,
                Description,
                ConditionType,
                SortOrder,
                IsControlCondition,
                IsActive,
                ManipulationJson,
                CreatedAtUtc,
                UpdatedAtUtc
            )
            VALUES
            (
                $id,
                $studyId,
                $groupId,
                $name,
                $description,
                $conditionType,
                $sortOrder,
                $isControlCondition,
                $isActive,
                $manipulationJson,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;

        AddParameters(
            command,
            condition);

        await command.ExecuteNonQueryAsync();
    }


    public static async Task<List<ExperimentalCondition>>
        GetByStudyAsync(
            string studyId)
    {
        return await GetByStudyCoreAsync(
            studyId,
            activeOnly: false);
    }


    public static async Task<List<ExperimentalCondition>>
        GetActiveByStudyAsync(
            string studyId)
    {
        return await GetByStudyCoreAsync(
            studyId,
            activeOnly: true);
    }


    public static async Task<ExperimentalCondition?>
        GetByIdAsync(
            string conditionId)
    {
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
                Description,
                ConditionType,
                SortOrder,
                IsControlCondition,
                IsActive,
                ManipulationJson,
                CreatedAtUtc,
                UpdatedAtUtc
            FROM ExperimentalConditions
            WHERE Id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$id",
            conditionId);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadCondition(
            reader);
    }


    public static async Task UpdateAsync(
        ExperimentalCondition condition)
    {
        condition.UpdatedAtUtc =
            DateTime.UtcNow;

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE ExperimentalConditions
            SET
                StudyId = $studyId,
                GroupId = $groupId,
                Name = $name,
                Description = $description,
                ConditionType = $conditionType,
                SortOrder = $sortOrder,
                IsControlCondition = $isControlCondition,
                IsActive = $isActive,
                ManipulationJson = $manipulationJson,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;

        AddParameters(
            command,
            condition);

        await command.ExecuteNonQueryAsync();
    }


    public static async Task DeleteAsync(
        string conditionId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            DELETE FROM ExperimentalConditions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            conditionId);

        await command.ExecuteNonQueryAsync();
    }


    internal static async Task UnsetOtherControlConditionsAsync(
        string studyId,
        string conditionId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE ExperimentalConditions
            SET
                IsControlCondition = 0,
                UpdatedAtUtc = $updatedAtUtc
            WHERE StudyId = $studyId
              AND Id <> $conditionId
              AND IsControlCondition = 1;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        command.Parameters.AddWithValue(
            "$conditionId",
            conditionId);

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }


    private static async Task<List<ExperimentalCondition>>
        GetByStudyCoreAsync(
            string studyId,
            bool activeOnly)
    {
        var conditions =
            new List<ExperimentalCondition>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = activeOnly
            ? """
                SELECT
                    Id,
                    StudyId,
                    GroupId,
                    Name,
                    Description,
                    ConditionType,
                    SortOrder,
                    IsControlCondition,
                    IsActive,
                    ManipulationJson,
                    CreatedAtUtc,
                    UpdatedAtUtc
                FROM ExperimentalConditions
                WHERE StudyId = $studyId
                  AND IsActive = 1
                ORDER BY SortOrder ASC,
                         CreatedAtUtc ASC;
                """
            : """
                SELECT
                    Id,
                    StudyId,
                    GroupId,
                    Name,
                    Description,
                    ConditionType,
                    SortOrder,
                    IsControlCondition,
                    IsActive,
                    ManipulationJson,
                    CreatedAtUtc,
                    UpdatedAtUtc
                FROM ExperimentalConditions
                WHERE StudyId = $studyId
                ORDER BY SortOrder ASC,
                         CreatedAtUtc ASC;
                """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            conditions.Add(
                ReadCondition(
                    reader));
        }

        return conditions;
    }


    private static void AddParameters(
        SqliteCommand command,
        ExperimentalCondition condition)
    {
        command.Parameters.AddWithValue(
            "$id",
            condition.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            condition.StudyId);

        command.Parameters.AddWithValue(
            "$groupId",
            Db(condition.GroupId));

        command.Parameters.AddWithValue(
            "$name",
            condition.Name);

        command.Parameters.AddWithValue(
            "$description",
            Db(condition.Description));

        command.Parameters.AddWithValue(
            "$conditionType",
            condition.ConditionType);

        command.Parameters.AddWithValue(
            "$sortOrder",
            condition.SortOrder);

        command.Parameters.AddWithValue(
            "$isControlCondition",
            condition.IsControlCondition ? 1 : 0);

        command.Parameters.AddWithValue(
            "$isActive",
            condition.IsActive ? 1 : 0);

        command.Parameters.AddWithValue(
            "$manipulationJson",
            Db(condition.ManipulationJson));

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            condition.CreatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            condition.UpdatedAtUtc.ToString("O"));
    }


    private static ExperimentalCondition ReadCondition(
        SqliteDataReader reader)
    {
        return new ExperimentalCondition
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            GroupId =
                StringOrNull(reader, 2),

            Name =
                reader.GetString(3),

            Description =
                StringOrNull(reader, 4),

            ConditionType =
                reader.GetString(5),

            SortOrder =
                reader.GetInt32(6),

            IsControlCondition =
                reader.GetInt32(7) == 1,

            IsActive =
                reader.GetInt32(8) == 1,

            ManipulationJson =
                StringOrNull(reader, 9),

            CreatedAtUtc =
                DateTime.Parse(
                    reader.GetString(10)),

            UpdatedAtUtc =
                DateTime.Parse(
                    reader.GetString(11))
        };
    }


    private static object Db(
        object? value)
    {
        return value ?? DBNull.Value;
    }


    private static string? StringOrNull(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetString(index);
    }
}
