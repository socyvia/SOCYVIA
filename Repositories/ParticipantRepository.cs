using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ParticipantRepository
{
    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        Participant participant)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Participants
            (
                Id,
                StudyId,
                GroupId,
                ParticipantCode,
                Status,

                Age,
                Gender,
                EducationLevel,
                Occupation,

                IsEligible,
                EligibilityNotes,

                ConsentAccepted,
                ConsentAcceptedAtUtc,

                HasStartedStudy,
                HasCompletedStudy,

                StudyStartedAtUtc,
                StudyCompletedAtUtc,

                IsExcluded,
                ExclusionReason,

                HasWithdrawn,
                WithdrawalReason,

                ResearcherNotes,
                MetadataJson,

                CreatedAtUtc,
                UpdatedAtUtc
            )
            VALUES
            (
                $id,
                $studyId,
                $groupId,
                $participantCode,
                $status,

                $age,
                $gender,
                $educationLevel,
                $occupation,

                $isEligible,
                $eligibilityNotes,

                $consentAccepted,
                $consentAcceptedAtUtc,

                $hasStartedStudy,
                $hasCompletedStudy,

                $studyStartedAtUtc,
                $studyCompletedAtUtc,

                $isExcluded,
                $exclusionReason,

                $hasWithdrawn,
                $withdrawalReason,

                $researcherNotes,
                $metadataJson,

                $createdAtUtc,
                $updatedAtUtc
            );
            """;

        AddParameters(
            command,
            participant);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // GET BY STUDY
    // =========================================================

    public static async Task<List<Participant>>
        GetByStudyAsync(
            string studyId)
    {
        var participants =
            new List<Participant>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                GroupId,
                ParticipantCode,
                Status,

                Age,
                Gender,
                EducationLevel,
                Occupation,

                IsEligible,
                EligibilityNotes,

                ConsentAccepted,
                ConsentAcceptedAtUtc,

                HasStartedStudy,
                HasCompletedStudy,

                StudyStartedAtUtc,
                StudyCompletedAtUtc,

                IsExcluded,
                ExclusionReason,

                HasWithdrawn,
                WithdrawalReason,

                ResearcherNotes,
                MetadataJson,

                CreatedAtUtc,
                UpdatedAtUtc

            FROM Participants

            WHERE StudyId = $studyId

            ORDER BY CreatedAtUtc ASC;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            participants.Add(
                ReadParticipant(reader));
        }

        return participants;
    }


    // =========================================================
    // GET BY ID
    // =========================================================

    public static async Task<Participant?>
        GetByIdAsync(
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
                GroupId,
                ParticipantCode,
                Status,

                Age,
                Gender,
                EducationLevel,
                Occupation,

                IsEligible,
                EligibilityNotes,

                ConsentAccepted,
                ConsentAcceptedAtUtc,

                HasStartedStudy,
                HasCompletedStudy,

                StudyStartedAtUtc,
                StudyCompletedAtUtc,

                IsExcluded,
                ExclusionReason,

                HasWithdrawn,
                WithdrawalReason,

                ResearcherNotes,
                MetadataJson,

                CreatedAtUtc,
                UpdatedAtUtc

            FROM Participants

            WHERE Id = $id

            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$id",
            participantId);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadParticipant(reader);
    }


    // =========================================================
    // UPDATE
    // =========================================================

    public static async Task UpdateAsync(
        Participant participant)
    {
        participant.UpdatedAtUtc =
            DateTime.UtcNow;

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Participants
            SET
                GroupId = $groupId,
                ParticipantCode = $participantCode,
                Status = $status,

                Age = $age,
                Gender = $gender,
                EducationLevel = $educationLevel,
                Occupation = $occupation,

                IsEligible = $isEligible,
                EligibilityNotes = $eligibilityNotes,

                ConsentAccepted = $consentAccepted,
                ConsentAcceptedAtUtc = $consentAcceptedAtUtc,

                HasStartedStudy = $hasStartedStudy,
                HasCompletedStudy = $hasCompletedStudy,

                StudyStartedAtUtc = $studyStartedAtUtc,
                StudyCompletedAtUtc = $studyCompletedAtUtc,

                IsExcluded = $isExcluded,
                ExclusionReason = $exclusionReason,

                HasWithdrawn = $hasWithdrawn,
                WithdrawalReason = $withdrawalReason,

                ResearcherNotes = $researcherNotes,
                MetadataJson = $metadataJson,

                UpdatedAtUtc = $updatedAtUtc

            WHERE Id = $id;
            """;

        AddParameters(
            command,
            participant);

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // COUNT
    // =========================================================

    public static async Task<int>
        CountByStudyAsync(
            string studyId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM Participants
            WHERE StudyId = $studyId;
            """;

        command.Parameters.AddWithValue(
            "$studyId",
            studyId);

        var result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }


    // =========================================================
    // UPDATE GROUP
    // =========================================================

    public static async Task UpdateGroupAsync(
        string participantId,
        string? groupId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Participants
            SET
                GroupId = $groupId,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            participantId);

        command.Parameters.AddWithValue(
            "$groupId",
            (object?)groupId
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // UPDATE STATUS
    // =========================================================

    public static async Task UpdateStatusAsync(
        string participantId,
        string status)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Participants
            SET
                Status = $status,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            participantId);

        command.Parameters.AddWithValue(
            "$status",
            status);

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
        Participant participant)
    {
        command.Parameters.AddWithValue(
            "$id",
            participant.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            participant.StudyId);

        command.Parameters.AddWithValue(
            "$groupId",
            DbValue(participant.GroupId));

        command.Parameters.AddWithValue(
            "$participantCode",
            participant.ParticipantCode);

        command.Parameters.AddWithValue(
            "$status",
            participant.Status);

        command.Parameters.AddWithValue(
            "$age",
            DbValue(participant.Age));

        command.Parameters.AddWithValue(
            "$gender",
            DbValue(participant.Gender));

        command.Parameters.AddWithValue(
            "$educationLevel",
            DbValue(participant.EducationLevel));

        command.Parameters.AddWithValue(
            "$occupation",
            DbValue(participant.Occupation));

        command.Parameters.AddWithValue(
            "$isEligible",
            participant.IsEligible ? 1 : 0);

        command.Parameters.AddWithValue(
            "$eligibilityNotes",
            DbValue(participant.EligibilityNotes));

        command.Parameters.AddWithValue(
            "$consentAccepted",
            participant.ConsentAccepted ? 1 : 0);

        command.Parameters.AddWithValue(
            "$consentAcceptedAtUtc",
            DbDate(participant.ConsentAcceptedAtUtc));

        command.Parameters.AddWithValue(
            "$hasStartedStudy",
            participant.HasStartedStudy ? 1 : 0);

        command.Parameters.AddWithValue(
            "$hasCompletedStudy",
            participant.HasCompletedStudy ? 1 : 0);

        command.Parameters.AddWithValue(
            "$studyStartedAtUtc",
            DbDate(participant.StudyStartedAtUtc));

        command.Parameters.AddWithValue(
            "$studyCompletedAtUtc",
            DbDate(participant.StudyCompletedAtUtc));

        command.Parameters.AddWithValue(
            "$isExcluded",
            participant.IsExcluded ? 1 : 0);

        command.Parameters.AddWithValue(
            "$exclusionReason",
            DbValue(participant.ExclusionReason));

        command.Parameters.AddWithValue(
            "$hasWithdrawn",
            participant.HasWithdrawn ? 1 : 0);

        command.Parameters.AddWithValue(
            "$withdrawalReason",
            DbValue(participant.WithdrawalReason));

        command.Parameters.AddWithValue(
            "$researcherNotes",
            DbValue(participant.ResearcherNotes));

        command.Parameters.AddWithValue(
            "$metadataJson",
            DbValue(participant.MetadataJson));

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            participant.CreatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            participant.UpdatedAtUtc.ToString("O"));
    }


    // =========================================================
    // READER
    // =========================================================

    private static Participant ReadParticipant(
        SqliteDataReader reader)
    {
        return new Participant
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            GroupId =
                GetNullableString(reader, 2),

            ParticipantCode =
                reader.GetString(3),

            Status =
                reader.GetString(4),

            Age =
                GetNullableInt(reader, 5),

            Gender =
                GetNullableString(reader, 6),

            EducationLevel =
                GetNullableString(reader, 7),

            Occupation =
                GetNullableString(reader, 8),

            IsEligible =
                reader.GetInt32(9) == 1,

            EligibilityNotes =
                GetNullableString(reader, 10),

            ConsentAccepted =
                reader.GetInt32(11) == 1,

            ConsentAcceptedAtUtc =
                GetNullableDate(reader, 12),

            HasStartedStudy =
                reader.GetInt32(13) == 1,

            HasCompletedStudy =
                reader.GetInt32(14) == 1,

            StudyStartedAtUtc =
                GetNullableDate(reader, 15),

            StudyCompletedAtUtc =
                GetNullableDate(reader, 16),

            IsExcluded =
                reader.GetInt32(17) == 1,

            ExclusionReason =
                GetNullableString(reader, 18),

            HasWithdrawn =
                reader.GetInt32(19) == 1,

            WithdrawalReason =
                GetNullableString(reader, 20),

            ResearcherNotes =
                GetNullableString(reader, 21),

            MetadataJson =
                GetNullableString(reader, 22),

            CreatedAtUtc =
                DateTime.Parse(
                    reader.GetString(23)),

            UpdatedAtUtc =
                reader.IsDBNull(24)
                    ? DateTime.Parse(
                        reader.GetString(23))
                    : DateTime.Parse(
                        reader.GetString(24))
        };
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static object DbValue(
        object? value)
    {
        return value ?? DBNull.Value;
    }


    private static object DbDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("O")
            : DBNull.Value;
    }


    private static string? GetNullableString(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetString(index);
    }


    private static int? GetNullableInt(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetInt32(index);
    }


    private static DateTime? GetNullableDate(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : DateTime.Parse(
                reader.GetString(index));
    }
}