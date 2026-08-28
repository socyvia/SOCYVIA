using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ParticipantAssignmentRepository
{
    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        ParticipantAssignment assignment)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO ParticipantAssignments
            (
                Id,
                StudyId,
                ParticipantId,
                GroupId,
                AssignmentMethod,
                RandomizationSeed,
                AssignmentOrder,
                IsActive,
                AssignedAtUtc,
                Notes
            )
            VALUES
            (
                $id,
                $studyId,
                $participantId,
                $groupId,
                $assignmentMethod,
                $randomizationSeed,
                $assignmentOrder,
                $isActive,
                $assignedAtUtc,
                $notes
            );
            """;

        AddParameters(
            command,
            assignment);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // GET BY STUDY
    // =========================================================

    public static async Task<List<ParticipantAssignment>>
        GetByStudyAsync(
            string studyId)
    {
        var assignments =
            new List<ParticipantAssignment>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                ParticipantId,
                GroupId,
                AssignmentMethod,
                RandomizationSeed,
                AssignmentOrder,
                IsActive,
                AssignedAtUtc,
                Notes

            FROM ParticipantAssignments

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
            assignments.Add(
                ReadAssignment(reader));
        }

        return assignments;
    }


    // =========================================================
    // GET ACTIVE FOR PARTICIPANT
    // =========================================================

    public static async Task<ParticipantAssignment?>
        GetActiveForParticipantAsync(
            string participantId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                ParticipantId,
                GroupId,
                AssignmentMethod,
                RandomizationSeed,
                AssignmentOrder,
                IsActive,
                AssignedAtUtc,
                Notes

            FROM ParticipantAssignments

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

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadAssignment(reader);
    }


    // =========================================================
    // DEACTIVATE OLD ASSIGNMENTS
    // =========================================================

    public static async Task DeactivateForParticipantAsync(
        string participantId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE ParticipantAssignments
            SET IsActive = 0
            WHERE ParticipantId = $participantId
              AND IsActive = 1;
            """;

        command.Parameters.AddWithValue(
            "$participantId",
            participantId);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // PARAMETERS
    // =========================================================

    private static void AddParameters(
        SqliteCommand command,
        ParticipantAssignment assignment)
    {
        command.Parameters.AddWithValue(
            "$id",
            assignment.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            assignment.StudyId);

        command.Parameters.AddWithValue(
            "$participantId",
            assignment.ParticipantId);

        command.Parameters.AddWithValue(
            "$groupId",
            assignment.GroupId);

        command.Parameters.AddWithValue(
            "$assignmentMethod",
            assignment.AssignmentMethod);

        command.Parameters.AddWithValue(
            "$randomizationSeed",
            (object?)assignment.RandomizationSeed
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$assignmentOrder",
            (object?)assignment.AssignmentOrder
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$isActive",
            assignment.IsActive ? 1 : 0);

        command.Parameters.AddWithValue(
            "$assignedAtUtc",
            assignment.AssignedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$notes",
            (object?)assignment.Notes
            ?? DBNull.Value);
    }


    // =========================================================
    // READER
    // =========================================================

    private static ParticipantAssignment ReadAssignment(
        SqliteDataReader reader)
    {
        return new ParticipantAssignment
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            ParticipantId =
                reader.GetString(2),

            GroupId =
                reader.GetString(3),

            AssignmentMethod =
                reader.GetString(4),

            RandomizationSeed =
                reader.IsDBNull(5)
                    ? null
                    : reader.GetInt32(5),

            AssignmentOrder =
                reader.IsDBNull(6)
                    ? null
                    : reader.GetInt32(6),

            IsActive =
                reader.GetInt32(7) == 1,

            AssignedAtUtc =
                DateTime.Parse(
                    reader.GetString(8)),

            Notes =
                reader.IsDBNull(9)
                    ? null
                    : reader.GetString(9)
        };
    }
}