using System;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class SessionLifecycleEventService
{
    public static Task RecordSessionPreparedAsync(
        ExperimentSession session) =>
        RecordAsync(session, "SessionPrepared");

    public static Task RecordSessionStartedAsync(
        ExperimentSession session,
        long? elapsedMilliseconds = null) =>
        RecordAsync(session, "SessionStarted", elapsedMilliseconds);

    public static Task RecordSessionPausedAsync(
        ExperimentSession session,
        long? elapsedMilliseconds = null) =>
        RecordAsync(session, "SessionPaused", elapsedMilliseconds);

    public static Task RecordSessionResumedAsync(
        ExperimentSession session,
        long? elapsedMilliseconds = null) =>
        RecordAsync(session, "SessionResumed", elapsedMilliseconds);

    public static Task RecordSessionCompletedAsync(
        ExperimentSession session,
        long? elapsedMilliseconds = null) =>
        RecordAsync(session, "SessionCompleted", elapsedMilliseconds);

    public static Task RecordSessionCancelledAsync(
        ExperimentSession session) =>
        RecordAsync(session, "SessionCancelled");

    public static Task RecordSessionInterruptedAsync(
        ExperimentSession session,
        long? elapsedMilliseconds = null) =>
        RecordAsync(session, "SessionInterrupted", elapsedMilliseconds);


    private static async Task RecordAsync(
        ExperimentSession session,
        string eventType,
        long? elapsedMilliseconds = null)
    {
        var sequenceNumber =
            await InteractionEventRepository
                .GetNextSequenceNumberAsync(session.Id);

        var now = DateTime.UtcNow;
        var elapsed = elapsedMilliseconds ?? (session.StartedAtUtc.HasValue
            ? Math.Max(
                0,
                (long)(now - session.StartedAtUtc.Value)
                    .TotalMilliseconds)
            : 0);

        var interactionEvent =
            new InteractionEvent
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = session.StudyId,
                SessionId = session.Id,
                ParticipantId = session.ParticipantId,
                GroupId = session.GroupId,
                EventType = eventType,
                TimestampUtc = now,
                SessionElapsedMilliseconds = elapsed,
                SequenceNumber = sequenceNumber,
                SyncMarker =
                    $"{eventType.ToUpperInvariant()}_{session.Id}",
                MetadataJson = JsonSerializer.Serialize(
                    new
                    {
                        conditionId = session.ConditionId,
                        configurationSnapshotId =
                            session.ConfigurationSnapshotId,
                        lifecycleVersion = session.LifecycleVersion
                    })
            };

        await InteractionEventRepository
            .CreateAsync(interactionEvent);
    }
}
