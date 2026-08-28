using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class ExperimentSessionRepository
{
    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        ExperimentSession session)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        var startedAt =
            session.StartedAtUtc
            ?? session.CreatedAtUtc;

        session.UpdatedAtUtc =
            session.UpdatedAtUtc == default
                ? session.CreatedAtUtc
                : session.UpdatedAtUtc;

        command.CommandText = """
            INSERT INTO Sessions
            (
                Id,
                StudyId,
                ParticipantId,
                GroupId,

                ConditionId,
                ConfigurationSnapshotId,

                CreatedAtUtc,
                LifecycleUpdatedAtUtc,
                StartedAtUtc,
                ActualStartedAtUtc,
                CompletedAtUtc,

                Status,
                LifecycleVersion,
                DurationMilliseconds,

                CurrentStimulusIndex,
                CompletedStimulusCount,

                DeviceName,
                OperatingSystem,

                ScreenWidth,
                ScreenHeight,

                EegEnabled,
                GsrEnabled,

                EegDeviceId,
                GsrDeviceId,

                SynchronizationSessionId,

                WasInterrupted,
                InterruptionReason,

                ResearcherNotes,
                MetadataJson
            )
            VALUES
            (
                $id,
                $studyId,
                $participantId,
                $groupId,

                $conditionId,
                $configurationSnapshotId,

                $createdAtUtc,
                $lifecycleUpdatedAtUtc,
                $startedAtUtc,
                $actualStartedAtUtc,
                $completedAtUtc,

                $status,
                $lifecycleVersion,
                $durationMilliseconds,

                $currentStimulusIndex,
                $completedStimulusCount,

                $deviceName,
                $operatingSystem,

                $screenWidth,
                $screenHeight,

                $eegEnabled,
                $gsrEnabled,

                $eegDeviceId,
                $gsrDeviceId,

                $synchronizationSessionId,

                $wasInterrupted,
                $interruptionReason,

                $researcherNotes,
                $metadataJson
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            session.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            session.StudyId);

        command.Parameters.AddWithValue(
            "$participantId",
            session.ParticipantId);

        command.Parameters.AddWithValue(
            "$groupId",
            Db(session.GroupId));

        command.Parameters.AddWithValue(
            "$conditionId",
            Db(session.ConditionId));

        command.Parameters.AddWithValue(
            "$configurationSnapshotId",
            Db(session.ConfigurationSnapshotId));

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            session.CreatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$lifecycleUpdatedAtUtc",
            session.UpdatedAtUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$startedAtUtc",
            startedAt.ToString("O"));

        command.Parameters.AddWithValue(
            "$actualStartedAtUtc",
            DateDb(session.StartedAtUtc));

        command.Parameters.AddWithValue(
            "$completedAtUtc",
            DateDb(session.CompletedAtUtc));

        command.Parameters.AddWithValue(
            "$status",
            session.Status);

        command.Parameters.AddWithValue(
            "$lifecycleVersion",
            session.LifecycleVersion);

        command.Parameters.AddWithValue(
            "$durationMilliseconds",
            Db(session.DurationMilliseconds));

        command.Parameters.AddWithValue(
            "$currentStimulusIndex",
            session.CurrentStimulusIndex);

        command.Parameters.AddWithValue(
            "$completedStimulusCount",
            session.CompletedStimulusCount);

        command.Parameters.AddWithValue(
            "$deviceName",
            Db(session.DeviceName));

        command.Parameters.AddWithValue(
            "$operatingSystem",
            Db(session.OperatingSystem));

        command.Parameters.AddWithValue(
            "$screenWidth",
            Db(session.ScreenWidth));

        command.Parameters.AddWithValue(
            "$screenHeight",
            Db(session.ScreenHeight));

        command.Parameters.AddWithValue(
            "$eegEnabled",
            session.EegEnabled ? 1 : 0);

        command.Parameters.AddWithValue(
            "$gsrEnabled",
            session.GsrEnabled ? 1 : 0);

        command.Parameters.AddWithValue(
            "$eegDeviceId",
            Db(session.EegDeviceId));

        command.Parameters.AddWithValue(
            "$gsrDeviceId",
            Db(session.GsrDeviceId));

        command.Parameters.AddWithValue(
            "$synchronizationSessionId",
            Db(session.SynchronizationSessionId));

        command.Parameters.AddWithValue(
            "$wasInterrupted",
            session.WasInterrupted ? 1 : 0);

        command.Parameters.AddWithValue(
            "$interruptionReason",
            Db(session.InterruptionReason));

        command.Parameters.AddWithValue(
            "$researcherNotes",
            Db(session.ResearcherNotes));

        command.Parameters.AddWithValue(
            "$metadataJson",
            Db(session.MetadataJson));

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // GET BY STUDY
    // =========================================================

    public static async Task<List<ExperimentSession>>
        GetByStudyAsync(
            string studyId)
    {
        return await ReadListAsync(
            """
            SELECT
                Id,
                StudyId,
                ParticipantId,
                GroupId,
                ConditionId,
                ConfigurationSnapshotId,
                CreatedAtUtc,
                LifecycleUpdatedAtUtc,
                StartedAtUtc,
                ActualStartedAtUtc,
                CompletedAtUtc,
                Status,
                LifecycleVersion,
                DurationMilliseconds,
                CurrentStimulusIndex,
                CompletedStimulusCount,
                DeviceName,
                OperatingSystem,
                ScreenWidth,
                ScreenHeight,
                EegEnabled,
                GsrEnabled,
                EegDeviceId,
                GsrDeviceId,
                SynchronizationSessionId,
                WasInterrupted,
                InterruptionReason,
                ResearcherNotes,
                MetadataJson
            FROM Sessions
            WHERE StudyId = $value
            ORDER BY CreatedAtUtc DESC;
            """,
            "$value",
            studyId);
    }


    // =========================================================
    // GET BY PARTICIPANT
    // =========================================================

    public static async Task<List<ExperimentSession>>
        GetByParticipantAsync(
            string participantId)
    {
        return await ReadListAsync(
            """
            SELECT
                Id,
                StudyId,
                ParticipantId,
                GroupId,
                ConditionId,
                ConfigurationSnapshotId,
                CreatedAtUtc,
                LifecycleUpdatedAtUtc,
                StartedAtUtc,
                ActualStartedAtUtc,
                CompletedAtUtc,
                Status,
                LifecycleVersion,
                DurationMilliseconds,
                CurrentStimulusIndex,
                CompletedStimulusCount,
                DeviceName,
                OperatingSystem,
                ScreenWidth,
                ScreenHeight,
                EegEnabled,
                GsrEnabled,
                EegDeviceId,
                GsrDeviceId,
                SynchronizationSessionId,
                WasInterrupted,
                InterruptionReason,
                ResearcherNotes,
                MetadataJson
            FROM Sessions
            WHERE ParticipantId = $value
            ORDER BY CreatedAtUtc DESC;
            """,
            "$value",
            participantId);
    }


    public static async Task<ExperimentSession?> GetByIdAsync(
        string sessionId)
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
                ConditionId,
                ConfigurationSnapshotId,
                CreatedAtUtc,
                LifecycleUpdatedAtUtc,
                StartedAtUtc,
                ActualStartedAtUtc,
                CompletedAtUtc,
                Status,
                LifecycleVersion,
                DurationMilliseconds,
                CurrentStimulusIndex,
                CompletedStimulusCount,
                DeviceName,
                OperatingSystem,
                ScreenWidth,
                ScreenHeight,
                EegEnabled,
                GsrEnabled,
                EegDeviceId,
                GsrDeviceId,
                SynchronizationSessionId,
                WasInterrupted,
                InterruptionReason,
                ResearcherNotes,
                MetadataJson
            FROM Sessions
            WHERE Id = $sessionId
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$sessionId",
            sessionId);

        await using var reader =
            await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? ReadSession(reader)
            : null;
    }


    public static async Task UpdateAsync(
        ExperimentSession session)
    {
        session.UpdatedAtUtc = DateTime.UtcNow;

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            UPDATE Sessions
            SET
                GroupId = $groupId,
                ConditionId = $conditionId,
                ConfigurationSnapshotId = $configurationSnapshotId,
                LifecycleUpdatedAtUtc = $lifecycleUpdatedAtUtc,
                StartedAtUtc = $legacyStartedAtUtc,
                ActualStartedAtUtc = $actualStartedAtUtc,
                CompletedAtUtc = $completedAtUtc,
                Status = $status,
                LifecycleVersion = $lifecycleVersion,
                DurationMilliseconds = $durationMilliseconds,
                CurrentStimulusIndex = $currentStimulusIndex,
                CompletedStimulusCount = $completedStimulusCount,
                WasInterrupted = $wasInterrupted,
                InterruptionReason = $interruptionReason,
                ResearcherNotes = $researcherNotes,
                MetadataJson = $metadataJson
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$groupId", Db(session.GroupId));
        command.Parameters.AddWithValue(
            "$conditionId",
            Db(session.ConditionId));
        command.Parameters.AddWithValue(
            "$configurationSnapshotId",
            Db(session.ConfigurationSnapshotId));
        command.Parameters.AddWithValue(
            "$lifecycleUpdatedAtUtc",
            session.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$legacyStartedAtUtc",
            (session.StartedAtUtc ?? session.CreatedAtUtc).ToString("O"));
        command.Parameters.AddWithValue(
            "$actualStartedAtUtc",
            DateDb(session.StartedAtUtc));
        command.Parameters.AddWithValue(
            "$completedAtUtc",
            DateDb(session.CompletedAtUtc));
        command.Parameters.AddWithValue("$status", session.Status);
        command.Parameters.AddWithValue(
            "$lifecycleVersion",
            session.LifecycleVersion);
        command.Parameters.AddWithValue(
            "$durationMilliseconds",
            Db(session.DurationMilliseconds));
        command.Parameters.AddWithValue(
            "$currentStimulusIndex",
            session.CurrentStimulusIndex);
        command.Parameters.AddWithValue(
            "$completedStimulusCount",
            session.CompletedStimulusCount);
        command.Parameters.AddWithValue(
            "$wasInterrupted",
            session.WasInterrupted ? 1 : 0);
        command.Parameters.AddWithValue(
            "$interruptionReason",
            Db(session.InterruptionReason));
        command.Parameters.AddWithValue(
            "$researcherNotes",
            Db(session.ResearcherNotes));
        command.Parameters.AddWithValue(
            "$metadataJson",
            Db(session.MetadataJson));

        await command.ExecuteNonQueryAsync();
    }


    // =========================================================
    // COUNT BY STUDY
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
            FROM Sessions
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
    // READER
    // =========================================================

    private static async Task<List<ExperimentSession>>
        ReadListAsync(
            string sql,
            string parameterName,
            string parameterValue)
    {
        var sessions =
            new List<ExperimentSession>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            sql;

        command.Parameters.AddWithValue(
            parameterName,
            parameterValue);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            sessions.Add(
                ReadSession(reader));
        }

        return sessions;
    }


    private static ExperimentSession ReadSession(
        SqliteDataReader reader)
    {
        var legacyStartedAt =
            DateTime.Parse(
                reader.GetString(8));

        var lifecycleVersion =
            reader.GetInt32(12);

        return new ExperimentSession
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            ParticipantId =
                reader.GetString(2),

            GroupId =
                StringOrNull(reader, 3),

            ConditionId =
                StringOrNull(reader, 4),

            ConfigurationSnapshotId =
                StringOrNull(reader, 5),

            CreatedAtUtc =
                reader.IsDBNull(6)
                    ? legacyStartedAt
                    : DateTime.Parse(
                        reader.GetString(6)),

            UpdatedAtUtc =
                reader.IsDBNull(7)
                    ? legacyStartedAt
                    : DateTime.Parse(
                        reader.GetString(7)),

            StartedAtUtc =
                lifecycleVersion >= 2
                    ? DateOrNull(reader, 9)
                    : legacyStartedAt,

            CompletedAtUtc =
                DateOrNull(reader, 10),

            Status =
                reader.GetString(11),

            LifecycleVersion =
                lifecycleVersion,

            DurationMilliseconds =
                LongOrNull(reader, 13),

            CurrentStimulusIndex =
                reader.GetInt32(14),

            CompletedStimulusCount =
                reader.GetInt32(15),

            DeviceName =
                StringOrNull(reader, 16),

            OperatingSystem =
                StringOrNull(reader, 17),

            ScreenWidth =
                IntOrNull(reader, 18),

            ScreenHeight =
                IntOrNull(reader, 19),

            EegEnabled =
                reader.GetInt32(20) == 1,

            GsrEnabled =
                reader.GetInt32(21) == 1,

            EegDeviceId =
                StringOrNull(reader, 22),

            GsrDeviceId =
                StringOrNull(reader, 23),

            SynchronizationSessionId =
                StringOrNull(reader, 24),

            WasInterrupted =
                reader.GetInt32(25) == 1,

            InterruptionReason =
                StringOrNull(reader, 26),

            ResearcherNotes =
                StringOrNull(reader, 27),

            MetadataJson =
                StringOrNull(reader, 28)
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
