using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Repositories;

public sealed class DuplicateActiveSessionException : InvalidOperationException
{
    public DuplicateActiveSessionException(string sessionId)
        : base("An active prepared or running session already exists for this participant and study.")
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }
}

public static class ExperimentPreparationRepository
{
    public static async Task<ExperimentSession> CreatePreparedAsync(
        ExperimentSession session,
        ExperimentConfigurationSnapshot snapshot)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();
        await using var transaction =
            connection.BeginTransaction(deferred: false);

        var activeSessionId = await FindActiveSessionIdAsync(
            connection,
            transaction,
            session.StudyId,
            session.ParticipantId);

        if (activeSessionId is not null)
        {
            throw new DuplicateActiveSessionException(activeSessionId);
        }

        await RecoverIncompleteCreatedSessionsAsync(
            connection,
            transaction,
            session.StudyId,
            session.ParticipantId,
            session.CreatedAtUtc);

        session.Status = SessionLifecycleStates.Ready;
        session.ConfigurationSnapshotId = snapshot.Id;
        session.UpdatedAtUtc = session.CreatedAtUtc;

        await InsertSessionAsync(connection, transaction, session);

        var snapshotJson = ExperimentSnapshotSerializer.Serialize(snapshot);
        snapshot.PersistedSnapshotJson = snapshotJson;
        snapshot.IntegrityHash =
            SnapshotIntegrityService.ComputeHash(snapshotJson);
        snapshot.IntegrityHashAlgorithm =
            SnapshotIntegrityService.Algorithm;

        await InsertSnapshotAsync(
            connection,
            transaction,
            snapshot,
            snapshotJson);

        await InsertPreparedEventAsync(
            connection,
            transaction,
            session);

        await transaction.CommitAsync();
        return session;
    }

    public static async Task<int> RecoverIncompleteCreatedAsync(
        string studyId,
        string participantId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();
        await using var transaction =
            connection.BeginTransaction(deferred: false);
        var recovered = await RecoverIncompleteCreatedSessionsAsync(
            connection,
            transaction,
            studyId,
            participantId,
            DateTime.UtcNow);
        await transaction.CommitAsync();
        return recovered;
    }

    private static async Task<string?> FindActiveSessionIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string studyId,
        string participantId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id
            FROM Sessions
            WHERE StudyId = $studyId
              AND ParticipantId = $participantId
              AND Status IN ('Ready', 'Running', 'Paused')
            ORDER BY COALESCE(CreatedAtUtc, StartedAtUtc) DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$studyId", studyId);
        command.Parameters.AddWithValue("$participantId", participantId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<int> RecoverIncompleteCreatedSessionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string studyId,
        string participantId,
        DateTime recoveredAtUtc)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Sessions
            SET Status = 'Cancelled',
                LifecycleUpdatedAtUtc = $updatedAtUtc,
                InterruptionReason = COALESCE(
                    InterruptionReason,
                    'Recovered incomplete Created session before preparation')
            WHERE StudyId = $studyId
              AND ParticipantId = $participantId
              AND Status = 'Created'
              AND LifecycleVersion >= 2;
            """;
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            recoveredAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$studyId", studyId);
        command.Parameters.AddWithValue("$participantId", participantId);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExperimentSession session)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Sessions
            (
                Id, StudyId, ParticipantId, GroupId, ConditionId,
                ConfigurationSnapshotId, CreatedAtUtc,
                LifecycleUpdatedAtUtc, StartedAtUtc, ActualStartedAtUtc,
                CompletedAtUtc, Status, LifecycleVersion,
                DurationMilliseconds, CurrentStimulusIndex,
                CompletedStimulusCount, EegEnabled, GsrEnabled,
                WasInterrupted
            )
            VALUES
            (
                $id, $studyId, $participantId, $groupId, $conditionId,
                $snapshotId, $createdAtUtc, $updatedAtUtc,
                $legacyStartedAtUtc, NULL, NULL, $status,
                $lifecycleVersion, NULL, 0, 0, $eegEnabled,
                $gsrEnabled, 0
            );
            """;
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$studyId", session.StudyId);
        command.Parameters.AddWithValue("$participantId", session.ParticipantId);
        command.Parameters.AddWithValue("$groupId", Db(session.GroupId));
        command.Parameters.AddWithValue("$conditionId", Db(session.ConditionId));
        command.Parameters.AddWithValue(
            "$snapshotId",
            session.ConfigurationSnapshotId!);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            session.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            session.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$legacyStartedAtUtc",
            session.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", session.Status);
        command.Parameters.AddWithValue(
            "$lifecycleVersion",
            session.LifecycleVersion);
        command.Parameters.AddWithValue(
            "$eegEnabled",
            session.EegEnabled ? 1 : 0);
        command.Parameters.AddWithValue(
            "$gsrEnabled",
            session.GsrEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExperimentConfigurationSnapshot snapshot,
        string snapshotJson)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ExperimentConfigurationSnapshots
            (
                Id, SessionId, StudyId, ParticipantId, GroupId,
                ConditionId, SnapshotVersion, SnapshotJson,
                IntegrityHash, IntegrityHashAlgorithm, CreatedAtUtc
            )
            VALUES
            (
                $id, $sessionId, $studyId, $participantId, $groupId,
                $conditionId, $snapshotVersion, $snapshotJson,
                $integrityHash, $integrityHashAlgorithm, $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", snapshot.Id);
        command.Parameters.AddWithValue("$sessionId", snapshot.SessionId);
        command.Parameters.AddWithValue("$studyId", snapshot.StudyId);
        command.Parameters.AddWithValue("$participantId", snapshot.ParticipantId);
        command.Parameters.AddWithValue("$groupId", Db(snapshot.GroupId));
        command.Parameters.AddWithValue("$conditionId", snapshot.ConditionId);
        command.Parameters.AddWithValue("$snapshotVersion", snapshot.SnapshotVersion);
        command.Parameters.AddWithValue("$snapshotJson", snapshotJson);
        command.Parameters.AddWithValue("$integrityHash", snapshot.IntegrityHash!);
        command.Parameters.AddWithValue(
            "$integrityHashAlgorithm",
            snapshot.IntegrityHashAlgorithm!);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            snapshot.CreatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPreparedEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExperimentSession session)
    {
        var eventId = Guid.NewGuid().ToString();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Events
            (
                Id, StudyId, SessionId, ParticipantId, GroupId,
                EventType, TimestampUtc, ElapsedMilliseconds,
                SyncMarker, SequenceNumber, MetadataJson
            )
            VALUES
            (
                $id, $studyId, $sessionId, $participantId, $groupId,
                'SessionPrepared', $timestampUtc, 0,
                $syncMarker, 0, $metadataJson
            );
            """;
        command.Parameters.AddWithValue("$id", eventId);
        command.Parameters.AddWithValue("$studyId", session.StudyId);
        command.Parameters.AddWithValue("$sessionId", session.Id);
        command.Parameters.AddWithValue("$participantId", session.ParticipantId);
        command.Parameters.AddWithValue("$groupId", Db(session.GroupId));
        command.Parameters.AddWithValue(
            "$timestampUtc",
            session.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$syncMarker",
            $"SESSIONPREPARED_{session.Id}");
        command.Parameters.AddWithValue(
            "$metadataJson",
            JsonSerializer.Serialize(new
            {
                conditionId = session.ConditionId,
                configurationSnapshotId = session.ConfigurationSnapshotId,
                lifecycleVersion = session.LifecycleVersion
            }));
        await command.ExecuteNonQueryAsync();
    }

    private static object Db(object? value) => value ?? DBNull.Value;
}
