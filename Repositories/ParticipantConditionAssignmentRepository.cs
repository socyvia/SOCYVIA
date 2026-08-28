using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ParticipantConditionAssignmentRepository
{
    public static async Task CreateReplacingActiveAsync(
        ParticipantConditionAssignment assignment)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            await using (var deactivate = connection.CreateCommand())
            {
                deactivate.Transaction =
                    (SqliteTransaction)transaction;

                deactivate.CommandText = """
                    UPDATE ParticipantConditionAssignments
                    SET IsActive = 0
                    WHERE ParticipantId = $participantId
                      AND IsActive = 1;
                    """;

                deactivate.Parameters.AddWithValue(
                    "$participantId",
                    assignment.ParticipantId);

                await deactivate.ExecuteNonQueryAsync();
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction =
                    (SqliteTransaction)transaction;

                insert.CommandText = """
                    INSERT INTO ParticipantConditionAssignments
                    (
                        Id,
                        StudyId,
                        ParticipantId,
                        ConditionId,
                        AssignmentMethod,
                        RandomizationSeed,
                        AssignmentMetadataJson,
                        AssignedAtUtc,
                        IsActive
                    )
                    VALUES
                    (
                        $id,
                        $studyId,
                        $participantId,
                        $conditionId,
                        $assignmentMethod,
                        $randomizationSeed,
                        $assignmentMetadataJson,
                        $assignedAtUtc,
                        $isActive
                    );
                    """;

                AddParameters(insert, assignment);
                await insert.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }


    public static async Task<ParticipantConditionAssignment?>
        GetActiveForParticipantAsync(
            string participantId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = SelectColumns + """

            WHERE ParticipantId = $participantId
              AND IsActive = 1
            ORDER BY AssignedAtUtc DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$participantId",
            participantId);

        await using var reader =
            await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? Read(reader)
            : null;
    }


    public static async Task<List<ParticipantConditionAssignment>>
        GetByStudyAsync(
            string studyId,
            bool activeOnly = false)
    {
        var assignments =
            new List<ParticipantConditionAssignment>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = activeOnly
            ? SelectColumns + """

                WHERE StudyId = $studyId
                  AND IsActive = 1
                ORDER BY AssignedAtUtc ASC;
                """
            : SelectColumns + """

                WHERE StudyId = $studyId
                ORDER BY AssignedAtUtc ASC;
                """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            assignments.Add(Read(reader));
        }

        return assignments;
    }


    public static async Task<int> CountByConditionAsync(
        string conditionId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM ParticipantConditionAssignments
            WHERE ConditionId = $conditionId;
            """;

        command.Parameters.AddWithValue(
            "$conditionId",
            conditionId);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }


    private const string SelectColumns = """
        SELECT
            Id,
            StudyId,
            ParticipantId,
            ConditionId,
            AssignmentMethod,
            RandomizationSeed,
            AssignmentMetadataJson,
            AssignedAtUtc,
            IsActive
        FROM ParticipantConditionAssignments
        """;


    private static void AddParameters(
        SqliteCommand command,
        ParticipantConditionAssignment assignment)
    {
        command.Parameters.AddWithValue("$id", assignment.Id);
        command.Parameters.AddWithValue("$studyId", assignment.StudyId);
        command.Parameters.AddWithValue(
            "$participantId",
            assignment.ParticipantId);
        command.Parameters.AddWithValue(
            "$conditionId",
            assignment.ConditionId);
        command.Parameters.AddWithValue(
            "$assignmentMethod",
            assignment.AssignmentMethod);
        command.Parameters.AddWithValue(
            "$randomizationSeed",
            Db(assignment.RandomizationSeed));
        command.Parameters.AddWithValue(
            "$assignmentMetadataJson",
            Db(assignment.AssignmentMetadataJson));
        command.Parameters.AddWithValue(
            "$assignedAtUtc",
            assignment.AssignedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$isActive",
            assignment.IsActive ? 1 : 0);
    }


    private static ParticipantConditionAssignment Read(
        SqliteDataReader reader)
    {
        return new ParticipantConditionAssignment
        {
            Id = reader.GetString(0),
            StudyId = reader.GetString(1),
            ParticipantId = reader.GetString(2),
            ConditionId = reader.GetString(3),
            AssignmentMethod = reader.GetString(4),
            RandomizationSeed = reader.IsDBNull(5)
                ? null
                : reader.GetInt32(5),
            AssignmentMetadataJson = reader.IsDBNull(6)
                ? null
                : reader.GetString(6),
            AssignedAtUtc = DateTime.Parse(reader.GetString(7)),
            IsActive = reader.GetInt32(8) == 1
        };
    }


    private static object Db(object? value) =>
        value ?? DBNull.Value;
}
