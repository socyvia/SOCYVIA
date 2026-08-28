using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SOCYVIA.Data;
using SOCYVIA.Models;

namespace SOCYVIA.Repositories;

public static class InteractionEventRepository
{
    private const string InsertSql = """
        INSERT INTO Events
        (
            Id, StudyId, SessionId, ParticipantId, GroupId,
            ExperimentBlockId, StimulusId, EventType, TimestampUtc,
            ElapsedMilliseconds, StimulusElapsedMilliseconds,
            DurationMilliseconds, TargetElement, Value, ValueNumber,
            ValueBoolean, PreviousValue, PointerX, PointerY,
            ScrollPosition, ScrollDepthPercent, StimulusOrderIndex,
            ScreenWidth, ScreenHeight, SyncMarker, SequenceNumber,
            MetadataJson, SnapshotStimulusId
        )
        VALUES
        (
            $id, $studyId, $sessionId, $participantId, $groupId,
            $experimentBlockId, $stimulusId, $eventType, $timestampUtc,
            $elapsedMilliseconds, $stimulusElapsedMilliseconds,
            $durationMilliseconds, $targetElement, $value, $valueNumber,
            $valueBoolean, $previousValue, $pointerX, $pointerY,
            $scrollPosition, $scrollDepthPercent, $stimulusOrderIndex,
            $screenWidth, $screenHeight, $syncMarker, $sequenceNumber,
            $metadataJson, $snapshotStimulusId
        );
        """;

    // =========================================================
    // CREATE
    // =========================================================

    public static async Task CreateAsync(
        InteractionEvent interactionEvent)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = InsertSql;

        AddParameters(
            command,
            interactionEvent);

        await command.ExecuteNonQueryAsync();
    }


    public static async Task CreateBatchAsync(
        IReadOnlyList<InteractionEvent> interactionEvents)
    {
        if (interactionEvents.Count == 0)
        {
            return;
        }

        await using var connection =
            await DatabaseService.OpenConnectionAsync();
        await using var transaction =
            connection.BeginTransaction();

        foreach (var interactionEvent in interactionEvents)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = InsertSql;
            AddParameters(command, interactionEvent);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }


    // =========================================================
    // GET SESSION EVENTS
    // =========================================================

    public static async Task<List<InteractionEvent>>
        GetBySessionAsync(
            string sessionId)
    {
        var events =
            new List<InteractionEvent>();

        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                Id,
                StudyId,
                SessionId,
                ParticipantId,

                GroupId,
                ExperimentBlockId,
                StimulusId,

                EventType,
                TimestampUtc,

                ElapsedMilliseconds,
                StimulusElapsedMilliseconds,
                DurationMilliseconds,

                TargetElement,

                Value,
                ValueNumber,
                ValueBoolean,
                PreviousValue,

                PointerX,
                PointerY,

                ScrollPosition,
                ScrollDepthPercent,

                StimulusOrderIndex,

                ScreenWidth,
                ScreenHeight,

                SyncMarker,

                SequenceNumber,

                MetadataJson,
                SnapshotStimulusId

            FROM Events

            WHERE SessionId = $sessionId

            ORDER BY SequenceNumber ASC,
                     TimestampUtc ASC;
            """;

        command.Parameters.AddWithValue(
            "$sessionId",
            sessionId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            events.Add(
                ReadEvent(reader));
        }

        return events;
    }


    // =========================================================
    // COUNT SESSION EVENTS
    // =========================================================

    public static async Task<int>
        CountBySessionAsync(
            string sessionId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM Events
            WHERE SessionId = $sessionId;
            """;

        command.Parameters.AddWithValue(
            "$sessionId",
            sessionId);

        var result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }


    // =========================================================
    // NEXT SEQUENCE
    // =========================================================

    public static async Task<int>
        GetNextSequenceNumberAsync(
            string sessionId)
    {
        await using var connection =
            await DatabaseService.OpenConnectionAsync();

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT COALESCE(
                MAX(SequenceNumber),
                -1
            ) + 1
            FROM Events
            WHERE SessionId = $sessionId;
            """;

        command.Parameters.AddWithValue(
            "$sessionId",
            sessionId);

        var result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }


    // =========================================================
    // PARAMETERS
    // =========================================================

    private static void AddParameters(
        SqliteCommand command,
        InteractionEvent interactionEvent)
    {
        command.Parameters.AddWithValue(
            "$id",
            interactionEvent.Id);

        command.Parameters.AddWithValue(
            "$studyId",
            interactionEvent.StudyId);

        command.Parameters.AddWithValue(
            "$sessionId",
            interactionEvent.SessionId);

        command.Parameters.AddWithValue(
            "$participantId",
            interactionEvent.ParticipantId);

        command.Parameters.AddWithValue(
            "$groupId",
            Db(interactionEvent.GroupId));

        command.Parameters.AddWithValue(
            "$experimentBlockId",
            Db(interactionEvent.ExperimentBlockId));

        command.Parameters.AddWithValue(
            "$stimulusId",
            Db(interactionEvent.StimulusPostId));

        command.Parameters.AddWithValue(
            "$eventType",
            interactionEvent.EventType);

        command.Parameters.AddWithValue(
            "$timestampUtc",
            interactionEvent.TimestampUtc.ToString("O"));

        command.Parameters.AddWithValue(
            "$elapsedMilliseconds",
            interactionEvent.SessionElapsedMilliseconds);

        command.Parameters.AddWithValue(
            "$stimulusElapsedMilliseconds",
            Db(interactionEvent.StimulusElapsedMilliseconds));

        command.Parameters.AddWithValue(
            "$durationMilliseconds",
            Db(interactionEvent.DurationMilliseconds));

        command.Parameters.AddWithValue(
            "$targetElement",
            Db(interactionEvent.Target));

        command.Parameters.AddWithValue(
            "$value",
            Db(interactionEvent.ValueText));

        command.Parameters.AddWithValue(
            "$valueNumber",
            Db(interactionEvent.ValueNumber));

        command.Parameters.AddWithValue(
            "$valueBoolean",
            interactionEvent.ValueBoolean.HasValue
                ? interactionEvent.ValueBoolean.Value
                    ? 1
                    : 0
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$previousValue",
            Db(interactionEvent.PreviousValueText));

        command.Parameters.AddWithValue(
            "$pointerX",
            Db(interactionEvent.PointerX));

        command.Parameters.AddWithValue(
            "$pointerY",
            Db(interactionEvent.PointerY));

        command.Parameters.AddWithValue(
            "$scrollPosition",
            Db(interactionEvent.ScrollPosition));

        command.Parameters.AddWithValue(
            "$scrollDepthPercent",
            Db(interactionEvent.ScrollDepthPercent));

        command.Parameters.AddWithValue(
            "$stimulusOrderIndex",
            Db(interactionEvent.StimulusOrderIndex));

        command.Parameters.AddWithValue(
            "$screenWidth",
            Db(interactionEvent.ScreenWidth));

        command.Parameters.AddWithValue(
            "$screenHeight",
            Db(interactionEvent.ScreenHeight));

        command.Parameters.AddWithValue(
            "$syncMarker",
            Db(interactionEvent.SyncMarker));

        command.Parameters.AddWithValue(
            "$sequenceNumber",
            interactionEvent.SequenceNumber);

        command.Parameters.AddWithValue(
            "$metadataJson",
            Db(interactionEvent.MetadataJson));

        command.Parameters.AddWithValue(
            "$snapshotStimulusId",
            Db(interactionEvent.SnapshotStimulusId));
    }


    // =========================================================
    // READER
    // =========================================================

    private static InteractionEvent ReadEvent(
        SqliteDataReader reader)
    {
        return new InteractionEvent
        {
            Id =
                reader.GetString(0),

            StudyId =
                reader.GetString(1),

            SessionId =
                reader.GetString(2),

            ParticipantId =
                reader.GetString(3),

            GroupId =
                StringOrNull(reader, 4),

            ExperimentBlockId =
                StringOrNull(reader, 5),

            StimulusPostId =
                StringOrNull(reader, 6),

            EventType =
                reader.GetString(7),

            TimestampUtc =
                DateTime.Parse(
                    reader.GetString(8)),

            SessionElapsedMilliseconds =
                reader.IsDBNull(9)
                    ? 0
                    : reader.GetInt64(9),

            StimulusElapsedMilliseconds =
                LongOrNull(reader, 10),

            DurationMilliseconds =
                LongOrNull(reader, 11),

            Target =
                StringOrNull(reader, 12),

            ValueText =
                StringOrNull(reader, 13),

            ValueNumber =
                DoubleOrNull(reader, 14),

            ValueBoolean =
                BoolOrNull(reader, 15),

            PreviousValueText =
                StringOrNull(reader, 16),

            PointerX =
                DoubleOrNull(reader, 17),

            PointerY =
                DoubleOrNull(reader, 18),

            ScrollPosition =
                DoubleOrNull(reader, 19),

            ScrollDepthPercent =
                DoubleOrNull(reader, 20),

            StimulusOrderIndex =
                IntOrNull(reader, 21),

            ScreenWidth =
                IntOrNull(reader, 22),

            ScreenHeight =
                IntOrNull(reader, 23),

            SyncMarker =
                StringOrNull(reader, 24),

            SequenceNumber =
                reader.GetInt32(25),

            MetadataJson =
                StringOrNull(reader, 26),

            SnapshotStimulusId =
                StringOrNull(reader, 27)
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


    private static double? DoubleOrNull(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetDouble(index);
    }


    private static bool? BoolOrNull(
        SqliteDataReader reader,
        int index)
    {
        if (reader.IsDBNull(index))
        {
            return null;
        }

        return reader.GetInt32(index) == 1;
    }
}
