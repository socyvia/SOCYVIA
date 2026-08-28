using System;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class SessionLifecycleService
{
    public static async Task<ExperimentSession> CreateSessionAsync(
        Participant participant,
        string? conditionId = null)
    {
        var now = DateTime.UtcNow;

        var session =
            new ExperimentSession
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = participant.StudyId,
                ParticipantId = participant.Id,
                GroupId = participant.GroupId,
                ConditionId = conditionId,
                Status = SessionLifecycleStates.Created,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                StartedAtUtc = null,
                CompletedAtUtc = null,
                LifecycleVersion = 2,
                CurrentStimulusIndex = 0,
                CompletedStimulusCount = 0,
                WasInterrupted = false
            };

        await ExperimentSessionRepository.CreateAsync(session);
        return session;
    }


    public static async Task<ExperimentSession> PrepareSessionAsync(
        string sessionId)
    {
        var session =
            await LoadAsync(sessionId);

        RequireState(
            session,
            SessionLifecycleStates.Created);

        if (string.IsNullOrWhiteSpace(
                session.ConfigurationSnapshotId))
        {
            throw new InvalidOperationException(
                "A configuration snapshot is required before a session can be prepared.");
        }

        session.Status = SessionLifecycleStates.Ready;
        await ExperimentSessionRepository.UpdateAsync(session);
        await SessionLifecycleEventService
            .RecordSessionPreparedAsync(session);
        return session;
    }


    public static async Task<ExperimentSession> StartSessionAsync(
        string sessionId,
        long? elapsedMilliseconds = null)
    {
        var session = await LoadAsync(sessionId);
        RequireState(session, SessionLifecycleStates.Ready);

        var now = DateTime.UtcNow;
        session.Status = SessionLifecycleStates.Running;
        session.StartedAtUtc = now;

        await ExperimentSessionRepository.UpdateAsync(session);

        var participant =
            await ParticipantRepository
                .GetByIdAsync(session.ParticipantId)
            ?? throw new InvalidOperationException(
                "The session participant no longer exists.");

        participant.HasStartedStudy = true;
        participant.StudyStartedAtUtc ??= now;
        participant.Status = "InProgress";
        await ParticipantRepository.UpdateAsync(participant);

        await SessionLifecycleEventService
            .RecordSessionStartedAsync(session, elapsedMilliseconds);
        return session;
    }


    public static async Task<ExperimentSession> PauseSessionAsync(
        string sessionId,
        long? elapsedMilliseconds = null)
    {
        var session = await LoadAsync(sessionId);
        RequireState(session, SessionLifecycleStates.Running);
        session.Status = SessionLifecycleStates.Paused;
        await ExperimentSessionRepository.UpdateAsync(session);
        await SessionLifecycleEventService
            .RecordSessionPausedAsync(session, elapsedMilliseconds);
        return session;
    }


    public static async Task<ExperimentSession> ResumeSessionAsync(
        string sessionId,
        long? elapsedMilliseconds = null)
    {
        var session = await LoadAsync(sessionId);
        RequireState(session, SessionLifecycleStates.Paused);
        session.Status = SessionLifecycleStates.Running;
        await ExperimentSessionRepository.UpdateAsync(session);
        await SessionLifecycleEventService
            .RecordSessionResumedAsync(session, elapsedMilliseconds);
        return session;
    }


    public static async Task<ExperimentSession> CompleteSessionAsync(
        string sessionId,
        long? elapsedMilliseconds = null)
    {
        var session = await LoadAsync(sessionId);
        RequireState(
            session,
            SessionLifecycleStates.Running,
            SessionLifecycleStates.Paused);

        var now = DateTime.UtcNow;
        session.Status = SessionLifecycleStates.Completed;
        session.CompletedAtUtc = now;
        session.DurationMilliseconds = elapsedMilliseconds ??
            (session.StartedAtUtc.HasValue
            ? Math.Max(
                0,
                (long)(now - session.StartedAtUtc.Value)
                    .TotalMilliseconds)
            : null);

        await ExperimentSessionRepository.UpdateAsync(session);

        var participant =
            await ParticipantRepository
                .GetByIdAsync(session.ParticipantId);

        if (participant is not null)
        {
            participant.HasCompletedStudy = true;
            participant.StudyCompletedAtUtc = now;
            participant.Status = "Completed";
            await ParticipantRepository.UpdateAsync(participant);
        }

        await SessionLifecycleEventService
            .RecordSessionCompletedAsync(session, elapsedMilliseconds);
        return session;
    }


    public static async Task<ExperimentSession> CancelSessionAsync(
        string sessionId)
    {
        var session = await LoadAsync(sessionId);
        RequireState(
            session,
            SessionLifecycleStates.Created,
            SessionLifecycleStates.Ready,
            SessionLifecycleStates.Running,
            SessionLifecycleStates.Paused);

        session.Status = SessionLifecycleStates.Cancelled;
        session.CompletedAtUtc = null;
        await ExperimentSessionRepository.UpdateAsync(session);
        await SessionLifecycleEventService
            .RecordSessionCancelledAsync(session);
        return session;
    }


    public static async Task<ExperimentSession> InterruptSessionAsync(
        string sessionId,
        string? reason = null,
        long? elapsedMilliseconds = null)
    {
        var session = await LoadAsync(sessionId);
        RequireState(
            session,
            SessionLifecycleStates.Running,
            SessionLifecycleStates.Paused);

        session.Status = SessionLifecycleStates.Interrupted;
        session.WasInterrupted = true;
        session.InterruptionReason = reason;
        session.CompletedAtUtc = null;
        session.DurationMilliseconds = elapsedMilliseconds;
        await ExperimentSessionRepository.UpdateAsync(session);
        await SessionLifecycleEventService
            .RecordSessionInterruptedAsync(session, elapsedMilliseconds);
        return session;
    }


    private static async Task<ExperimentSession> LoadAsync(
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException(
                "Session ID is required.",
                nameof(sessionId));
        }

        return await ExperimentSessionRepository
                   .GetByIdAsync(sessionId)
               ?? throw new InvalidOperationException(
                   "The experiment session does not exist.");
    }


    private static void RequireState(
        ExperimentSession session,
        params string[] allowedStates)
    {
        foreach (var allowedState in allowedStates)
        {
            if (string.Equals(
                    session.Status,
                    allowedState,
                    StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Session state '{session.Status}' cannot transition through this operation.");
    }
}
