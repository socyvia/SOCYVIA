using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;
using SOCYVIA.Services.Telemetry;

namespace SOCYVIA.Services;

public sealed class ExperimentRuntimeService : IAsyncDisposable
{
    private readonly Stopwatch _sessionClock = new();
    private BufferedTelemetryService? _telemetry;
    private ExperimentSession? _session;
    private int? _viewportWidth;
    private int? _viewportHeight;

    public ExperimentRuntimeContext? Context { get; private set; }
    public long ElapsedMilliseconds => _sessionClock.ElapsedMilliseconds;
    public Exception? TelemetryFailure => _telemetry?.LastFailure;

    public async Task<ExperimentRuntimeContext> InitializeAsync(
        string sessionId)
    {
        var session = await ExperimentSessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException("The session does not exist.");
        if (!string.Equals(
                session.Status,
                SessionLifecycleStates.Ready,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Only a Ready session can be opened in the participant runner.");
        }

        var snapshot = await ExperimentConfigurationSnapshotRepository
            .GetBySessionAsync(sessionId)
            ?? throw new InvalidOperationException(
                "The immutable session snapshot is missing.");
        var integrity = SnapshotIntegrityService.Verify(snapshot);
        if (integrity.Status == SnapshotIntegrityStatus.Invalid)
        {
            throw new InvalidOperationException(
                "The immutable session snapshot failed its SHA-256 integrity check.");
        }

        if (!string.Equals(snapshot.SessionId, session.Id, StringComparison.Ordinal) ||
            !string.Equals(snapshot.StudyId, session.StudyId, StringComparison.Ordinal) ||
            !string.Equals(snapshot.ParticipantId, session.ParticipantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The session snapshot identity does not match the prepared session.");
        }

        var participant = await ParticipantRepository.GetByIdAsync(session.ParticipantId)
            ?? throw new InvalidOperationException("The session participant does not exist.");

        _session = session;
        Context = new ExperimentRuntimeContext
        {
            Session = session,
            Participant = participant,
            Snapshot = snapshot,
            Posts = RuntimePresentationService.CreatePosts(snapshot),
            PresentationMode = ParticipantPresentationMode.Feed
        };
        return Context;
    }

    public async Task StartAsync()
    {
        var session = RequireSession();
        _session = await SessionLifecycleService
            .StartSessionAsync(session.Id, 0);
        _telemetry = await BufferedTelemetryService.CreateAsync(_session);
        _sessionClock.Restart();
        await _telemetry.TrackAsync(
            CanonicalInteractionEventTypes.FeedOpened,
            ElapsedMilliseconds,
            target: "ParticipantFeed");
    }

    public async Task PauseAsync()
    {
        var session = RequireSession();
        RequireTelemetry();
        _sessionClock.Stop();
        await _telemetry!.FlushAsync();
        _session = await SessionLifecycleService.PauseSessionAsync(
            session.Id,
            ElapsedMilliseconds);
        await _telemetry.SynchronizeSequenceAsync();
    }

    public async Task ResumeAsync()
    {
        var session = RequireSession();
        RequireTelemetry();
        _session = await SessionLifecycleService.ResumeSessionAsync(
            session.Id,
            ElapsedMilliseconds);
        await _telemetry!.SynchronizeSequenceAsync();
        _sessionClock.Start();
    }

    public async Task<ParticipantSessionSummary> CompleteAsync()
    {
        var session = RequireSession();
        RequireTelemetry();
        _sessionClock.Stop();

        try
        {
            await _telemetry!.TrackAsync(
                CanonicalInteractionEventTypes.SessionEnded,
                ElapsedMilliseconds,
                target: "ParticipantFeed");
            await _telemetry!.FlushAsync();
        }
        catch (Exception exception)
        {
            await InterruptAfterTelemetryFailureAsync(session.Id, exception);
            throw;
        }

        _session = await SessionLifecycleService.CompleteSessionAsync(
            session.Id,
            ElapsedMilliseconds);
        await _telemetry.DisposeAsync();
        _telemetry = null;
        return await SessionSummaryService.CreateAsync(session.Id);
    }

    public async Task EndExperimentPhaseAsync()
    {
        RequireSession();
        RequireTelemetry();
        if (_sessionClock.IsRunning)
            _sessionClock.Stop();
        await _telemetry!.TrackAsync(
            CanonicalInteractionEventTypes.ExperimentPhaseEnded,
            ElapsedMilliseconds,
            target: "ParticipantFeed");
        await _telemetry.FlushAsync();
    }

    public ValueTask TrackSessionEventAsync(string eventType, string target, string? valueText = null)
    {
        RequireTelemetry();
        return _telemetry!.TrackAsync(eventType, ElapsedMilliseconds, target: target, valueText: valueText);
    }

    public async Task<ParticipantSessionSummary> InterruptAsync(
        string reason)
    {
        var session = RequireSession();
        _sessionClock.Stop();
        Exception? flushFailure = null;
        if (_telemetry is not null)
        {
            try
            {
                await _telemetry.FlushAsync();
            }
            catch (Exception exception)
            {
                flushFailure = exception;
                reason = $"{reason}; telemetry flush failed: {exception.Message}";
            }
        }

        _session = await SessionLifecycleService.InterruptSessionAsync(
            session.Id,
            reason,
            ElapsedMilliseconds);
        if (_telemetry is not null)
        {
            await _telemetry.DisposeAsync();
            _telemetry = null;
        }

        var summary = await SessionSummaryService.CreateAsync(session.Id);
        if (flushFailure is not null)
        {
            throw new InvalidOperationException(
                "The session was interrupted because telemetry could not be flushed.",
                flushFailure);
        }
        return summary;
    }

    public async ValueTask TrackExposureTransitionAsync(
        PostExposureTransition transition,
        int presentationOrder)
    {
        RequireTelemetry();
        var metadata = JsonSerializer.Serialize(new
        {
            visibleRatio = transition.VisibleRatio,
            viewportEntryThreshold = PostExposureTracker.ViewportEntryThreshold,
            meaningfulVisibilityThreshold = PostExposureTracker.MeaningfulVisibilityThreshold,
            minimumMeaningfulExposureMilliseconds =
                PostExposureTracker.MinimumMeaningfulExposureMilliseconds,
            cumulativeMeaningfulMilliseconds = transition.CumulativeVisibleMilliseconds
        });

        if (transition.Type == PostExposureTransitionType.EnteredViewport)
        {
            await _telemetry!.TrackAsync(
                CanonicalInteractionEventTypes.ContentEnteredViewport,
                transition.SessionElapsedMilliseconds,
                transition.StimulusId,
                presentationOrder,
                metadataJson: metadata,
                screenWidth: _viewportWidth,
                screenHeight: _viewportHeight);
            return;
        }

        if (transition.Type == PostExposureTransitionType.MeaningfullyExposed)
        {
            if (transition.IsFirstExposure)
            {
                await TrackExposureEventAsync("PostShown", transition, presentationOrder, metadata);
                await TrackExposureEventAsync(
                    CanonicalInteractionEventTypes.ContentShown,
                    transition,
                    presentationOrder,
                    metadata);
            }
            await TrackExposureEventAsync(
                CanonicalInteractionEventTypes.ContentMeaningfullyExposed,
                transition,
                presentationOrder,
                metadata);
            await TrackExposureEventAsync("PostVisible", transition, presentationOrder, metadata);
            return;
        }

        if (transition.Type == PostExposureTransitionType.MeaningfulExposureEnded)
        {
            await TrackExposureEventAsync("PostHidden", transition, presentationOrder, metadata);
            await TrackExposureEventAsync("PostExited", transition, presentationOrder, metadata);
            await TrackExposureEventAsync(
                CanonicalInteractionEventTypes.ContentExited,
                transition,
                presentationOrder,
                metadata);
            await TrackExposureEventAsync(
                CanonicalInteractionEventTypes.ContentMeaningfulExposureEnded,
                transition,
                presentationOrder,
                metadata);
            await TrackExposureEventAsync("TimeSpentPerPost", transition, presentationOrder, metadata);
            return;
        }

        await _telemetry!.TrackAsync(
            CanonicalInteractionEventTypes.ContentExitedViewport,
            transition.SessionElapsedMilliseconds,
            transition.StimulusId,
            presentationOrder,
            metadataJson: metadata,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);
    }

    public ValueTask TrackInteractionAsync(
        string eventType,
        RuntimePostPresentation post,
        string target,
        bool? valueBoolean = null,
        string? valueText = null)
    {
        RequireTelemetry();
        return _telemetry!.TrackAsync(
            eventType,
            ElapsedMilliseconds,
            post.Source.StimulusId,
            post.PresentationOrder,
            target: target,
            valueText: valueText,
            valueBoolean: valueBoolean,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);
    }

    public ValueTask TrackTimedInteractionAsync(
        string eventType,
        RuntimePostPresentation post,
        string target,
        long durationMilliseconds,
        string? valueText = null) =>
        _telemetry!.TrackAsync(
            eventType,
            ElapsedMilliseconds,
            post.Source.StimulusId,
            post.PresentationOrder,
            stimulusElapsedMilliseconds: durationMilliseconds,
            durationMilliseconds: durationMilliseconds,
            target: target,
            valueText: valueText,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);

    public void UpdateViewport(double width, double height)
    {
        _viewportWidth = width > 0 ? (int)Math.Round(width) : null;
        _viewportHeight = height > 0 ? (int)Math.Round(height) : null;
    }

    public ValueTask TrackScrollAsync(
        double position,
        double depthPercent)
    {
        RequireTelemetry();
        return _telemetry!.TrackAsync(
            "Scroll",
            ElapsedMilliseconds,
            target: "ParticipantFeed",
            scrollPosition: position,
            scrollDepthPercent: depthPercent,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);
    }

    public ValueTask TrackScrollDepthAsync(
        double position,
        double depthPercent)
    {
        RequireTelemetry();
        return _telemetry!.TrackAsync(
            "ScrollDepth",
            ElapsedMilliseconds,
            target: "ParticipantFeed",
            valueNumber: depthPercent,
            scrollPosition: position,
            scrollDepthPercent: depthPercent,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);
    }

    public ValueTask TrackMediaAsync(
        RuntimePostPresentation post,
        bool isPlaying,
        string target = "Media")
    {
        RequireTelemetry();
        return _telemetry!.TrackAsync(
            isPlaying
                ? CanonicalInteractionEventTypes.MediaPlay
                : CanonicalInteractionEventTypes.MediaPause,
            ElapsedMilliseconds,
            post.Source.StimulusId,
            post.PresentationOrder,
            target: target,
            valueBoolean: isPlaying,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);
    }

    private ValueTask TrackExposureEventAsync(
        string eventType,
        PostExposureTransition transition,
        int presentationOrder,
        string metadata) =>
        _telemetry!.TrackAsync(
            eventType,
            transition.SessionElapsedMilliseconds,
            transition.StimulusId,
            presentationOrder,
            stimulusElapsedMilliseconds: transition.VisibleDurationMilliseconds,
            durationMilliseconds: transition.VisibleDurationMilliseconds > 0
                ? transition.VisibleDurationMilliseconds
                : null,
            metadataJson: metadata,
            screenWidth: _viewportWidth,
            screenHeight: _viewportHeight);

    public async ValueTask DisposeAsync()
    {
        if (_telemetry is not null)
        {
            await _telemetry.DisposeAsync();
            _telemetry = null;
        }
    }

    private async Task InterruptAfterTelemetryFailureAsync(
        string sessionId,
        Exception exception)
    {
        _session = await SessionLifecycleService.InterruptSessionAsync(
            sessionId,
            $"Telemetry flush failed: {exception.Message}",
            ElapsedMilliseconds);
        if (_telemetry is not null)
        {
            await _telemetry.DisposeAsync();
            _telemetry = null;
        }
    }

    private ExperimentSession RequireSession() =>
        _session ?? throw new InvalidOperationException(
            "The participant runtime has not been initialized.");

    private void RequireTelemetry()
    {
        if (_telemetry is null)
        {
            throw new InvalidOperationException(
                "Participant telemetry has not started.");
        }
    }
}
