using System;
using System.Threading.Tasks;
using SOCYVIA.Data;
using SOCYVIA.Models;
using SOCYVIA.Services;

namespace SOCYVIA.Repositories;

public static class ExperimentConfigurationSnapshotRepository
{
    public static async Task CreateAsync(
        ExperimentConfigurationSnapshot snapshot)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            INSERT INTO ExperimentConfigurationSnapshots
            (
                Id,
                SessionId,
                StudyId,
                ParticipantId,
                GroupId,
                ConditionId,
                SnapshotVersion,
                SnapshotJson,
                IntegrityHash,
                IntegrityHashAlgorithm,
                CreatedAtUtc
            )
            VALUES
            (
                $id,
                $sessionId,
                $studyId,
                $participantId,
                $groupId,
                $conditionId,
                $snapshotVersion,
                $snapshotJson,
                $integrityHash,
                $integrityHashAlgorithm,
                $createdAtUtc
            );
            """;

        AddParameters(command, snapshot);
        await command.ExecuteNonQueryAsync();
    }


    public static async Task<ExperimentConfigurationSnapshot?>
        GetByIdAsync(
            string snapshotId)
    {
        return await GetAsync(
            "Id",
            snapshotId);
    }


    public static async Task<ExperimentConfigurationSnapshot?>
        GetBySessionAsync(
            string sessionId)
    {
        return await GetAsync(
            "SessionId",
            sessionId);
    }


    private static async Task<ExperimentConfigurationSnapshot?> GetAsync(
        string column,
        string value)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = $"""
            SELECT SnapshotJson, IntegrityHash, IntegrityHashAlgorithm
            FROM ExperimentConfigurationSnapshots
            WHERE {column} = $value
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$value", value);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var json = reader.GetString(0);
        var snapshot = ExperimentSnapshotSerializer.Deserialize(json);
        snapshot.PersistedSnapshotJson = json;
        snapshot.IntegrityHash = reader.IsDBNull(1)
            ? null
            : reader.GetString(1);
        snapshot.IntegrityHashAlgorithm = reader.IsDBNull(2)
            ? null
            : reader.GetString(2);
        return snapshot;
    }


    private static void AddParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
        ExperimentConfigurationSnapshot snapshot)
    {
        command.Parameters.AddWithValue("$id", snapshot.Id);
        command.Parameters.AddWithValue("$sessionId", snapshot.SessionId);
        command.Parameters.AddWithValue("$studyId", snapshot.StudyId);
        command.Parameters.AddWithValue(
            "$participantId",
            snapshot.ParticipantId);
        command.Parameters.AddWithValue(
            "$groupId",
            (object?)snapshot.GroupId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$conditionId",
            snapshot.ConditionId);
        command.Parameters.AddWithValue(
            "$snapshotVersion",
            snapshot.SnapshotVersion);
        var snapshotJson =
            ExperimentSnapshotSerializer.Serialize(snapshot);
        snapshot.PersistedSnapshotJson = snapshotJson;
        snapshot.IntegrityHash ??=
            SnapshotIntegrityService.ComputeHash(snapshotJson);
        snapshot.IntegrityHashAlgorithm ??=
            SnapshotIntegrityService.Algorithm;
        command.Parameters.AddWithValue("$snapshotJson", snapshotJson);
        command.Parameters.AddWithValue(
            "$integrityHash",
            snapshot.IntegrityHash);
        command.Parameters.AddWithValue(
            "$integrityHashAlgorithm",
            snapshot.IntegrityHashAlgorithm);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            snapshot.CreatedAtUtc.ToString("O"));
    }
}
