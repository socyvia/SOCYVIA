using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class GroupRepository
{
    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        StudyGroup group)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Groups
            (
                Id,
                StudyId,
                Name,
                Description,
                ColorHex,
                IsControlGroup,
                SortOrder,
                TargetSampleSize,
                IsActive,
                CreatedAtUtc,
                UpdatedAtUtc
            )
            VALUES
            (
                $id,
                $studyId,
                $name,
                $description,
                $colorHex,
                $isControlGroup,
                $sortOrder,
                $targetSampleSize,
                $isActive,
                $createdAtUtc,
                $updatedAtUtc
            );
            """;

        AddParameters(
            command,
            group);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // GET BY STUDY
    // =========================================================

    public static async Task<List<StudyGroup>>
        GetByStudyAsync(
            string studyId)
    {
        var groups =
            new List<StudyGroup>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                Name,
                Description,
                ColorHex,
                IsControlGroup,
                SortOrder,
                TargetSampleSize,
                IsActive,
                CreatedAtUtc,
                UpdatedAtUtc

            FROM Groups

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
            groups.Add(
                ReadGroup(reader));
        }

        return groups;
    }


    // =========================================================
    // GET ACTIVE
    // =========================================================

    public static async Task<List<StudyGroup>>
        GetActiveByStudyAsync(
            string studyId)
    {
        var groups =
            new List<StudyGroup>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                Name,
                Description,
                ColorHex,
                IsControlGroup,
                SortOrder,
                TargetSampleSize,
                IsActive,
                CreatedAtUtc,
                UpdatedAtUtc

            FROM Groups

            WHERE StudyId = $studyId
              AND IsActive = 1

            ORDER BY SortOrder ASC;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            groups.Add(
                ReadGroup(reader));
        }

        return groups;
    }


    public static async Task<StudyGroup?> GetByIdAsync(
        string groupId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                Name,
                Description,
                ColorHex,
                IsControlGroup,
                SortOrder,
                TargetSampleSize,
                IsActive,
                CreatedAtUtc,
                UpdatedAtUtc
            FROM Groups
            WHERE Id = $groupId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$groupId",
            groupId);

        await using var reader =
            await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? ReadGroup(reader)
            : null;
    }


    // =========================================================
    // UPDATE
    // =========================================================

    public static async Task UpdateAsync(
        StudyGroup group)
    {
        group.UpdatedAtUtc =
            DateTime.UtcNow;

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Groups
            SET
                Name = $name,
                Description = $description,
                ColorHex = $colorHex,
                IsControlGroup = $isControlGroup,
                SortOrder = $sortOrder,
                TargetSampleSize = $targetSampleSize,
                IsActive = $isActive,
                UpdatedAtUtc = $updatedAtUtc

            WHERE Id = $id;
            """;

        AddParameters(
            command,
            group);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // DELETE
    // =========================================================

    public static async Task DeleteAsync(
        string groupId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            DELETE FROM Groups
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            groupId);

        await command.ExecuteNonQueryAsync();
    }


    public static async Task<GroupUsageSummary> GetUsageAsync(
        string groupId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM Participants
                    WHERE GroupId = $groupId),
                (SELECT COUNT(*) FROM ParticipantAssignments
                    WHERE GroupId = $groupId),
                (SELECT COUNT(*) FROM Sessions
                    WHERE GroupId = $groupId),
                (SELECT COUNT(*) FROM Events
                    WHERE GroupId = $groupId),
                (SELECT COUNT(*) FROM Stimuli
                    WHERE GroupId = $groupId),
                (SELECT COUNT(*) FROM ExperimentalConditions
                    WHERE GroupId = $groupId);
            """;

        command.Parameters.AddWithValue(
            "$groupId",
            groupId);

        await using var reader =
            await command.ExecuteReaderAsync();

        await reader.ReadAsync();

        return new GroupUsageSummary
        {
            GroupId = groupId,
            ParticipantCount = checked((int)reader.GetInt64(0)),
            AssignmentCount = checked((int)reader.GetInt64(1)),
            SessionCount = checked((int)reader.GetInt64(2)),
            EventCount = checked((int)reader.GetInt64(3)),
            StimulusCount = checked((int)reader.GetInt64(4)),
            ConditionCount = checked((int)reader.GetInt64(5))
        };
    }


    public static async Task<bool> TryDeleteUnusedAsync(
        string groupId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            DELETE FROM Groups
            WHERE Id = $groupId
              AND NOT EXISTS
                    (SELECT 1 FROM Participants
                     WHERE GroupId = $groupId)
              AND NOT EXISTS
                    (SELECT 1 FROM ParticipantAssignments
                     WHERE GroupId = $groupId)
              AND NOT EXISTS
                    (SELECT 1 FROM Sessions
                     WHERE GroupId = $groupId)
              AND NOT EXISTS
                    (SELECT 1 FROM Events
                     WHERE GroupId = $groupId)
              AND NOT EXISTS
                    (SELECT 1 FROM Stimuli
                     WHERE GroupId = $groupId)
              AND NOT EXISTS
                    (SELECT 1 FROM ExperimentalConditions
                     WHERE GroupId = $groupId);
            """;

        command.Parameters.AddWithValue(
            "$groupId",
            groupId);

        return await command.ExecuteNonQueryAsync() == 1;
    }


    internal static async Task UnsetOtherControlGroupsAsync(
        string studyId,
        string groupId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Groups
            SET
                IsControlGroup = 0,
                UpdatedAtUtc = $updatedAtUtc
            WHERE StudyId = $studyId
              AND Id <> $groupId
              AND IsControlGroup = 1;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        command.Parameters.AddWithValue(
            "$groupId",
            groupId);

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // PARAMETERS
    // =========================================================

    private static void AddParameters(
        SqliteCommand command,
        StudyGroup group)
    {
        command.Parameters.AddWithValue(
            "$id",
            group.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            group.StudyId);

        command.Parameters.AddWithValue(
            "$name",
            group.Name);

        command.Parameters.AddWithValue(
            "$description",
            (object?)group.Description
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$colorHex",
            (object?)group.ColorHex
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$isControlGroup",
            group.IsControlGroup ? 1 : 0);

        command.Parameters.AddWithValue(
            "$sortOrder",
            group.SortOrder);

        command.Parameters.AddWithValue(
            "$targetSampleSize",
            (object?)group.TargetSampleSize
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$isActive",
            group.IsActive ? 1 : 0);

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            group.CreatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            group.UpdatedAtUtc.ToString("O"));
    }


    // =========================================================
    // READER
    // =========================================================

    private static StudyGroup ReadGroup(
        SqliteDataReader reader)
    {
        return new StudyGroup
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            Name =
                reader.GetString(2),

            Description =
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3),

            ColorHex =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),

            IsControlGroup =
                reader.GetInt32(5) == 1,

            SortOrder =
                reader.GetInt32(6),

            TargetSampleSize =
                reader.IsDBNull(7)
                    ? null
                    : reader.GetInt32(7),

            IsActive =
                reader.GetInt32(8) == 1,

            CreatedAtUtc =
                DateTime.Parse(
                    reader.GetString(9)),

            UpdatedAtUtc =
                reader.IsDBNull(10)
                    ? DateTime.Parse(
                        reader.GetString(9))
                    : DateTime.Parse(
                        reader.GetString(10))
        };
    }
}
