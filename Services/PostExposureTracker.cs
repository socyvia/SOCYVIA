using System;
using System.Collections.Generic;
using System.Linq;

namespace SOCYVIA.Services;

public enum PostExposureTransitionType
{
    EnteredViewport,
    MeaningfullyExposed,
    MeaningfulExposureEnded,
    ExitedViewport
}

public sealed class PostExposureTransition
{
    public string StimulusId { get; init; } = string.Empty;
    public PostExposureTransitionType Type { get; init; }
    public bool IsFirstExposure { get; init; }
    public long SessionElapsedMilliseconds { get; init; }
    public long VisibleDurationMilliseconds { get; init; }
    public long CumulativeVisibleMilliseconds { get; init; }
    public double VisibleRatio { get; init; }
}

/// <summary>
/// Viewport exposure is scientifically qualified in two steps:
/// any ratio above 1% enters the viewport; meaningful exposure requires
/// at least 50% visibility continuously for at least 500 milliseconds.
/// Durations include the qualifying interval once that threshold is met.
/// </summary>
public sealed class PostExposureTracker
{
    public const double ViewportEntryThreshold = 0.01;
    public const double MeaningfulVisibilityThreshold = 0.50;
    public const long MinimumMeaningfulExposureMilliseconds = 500;

    private readonly Dictionary<string, ExposureState> _states =
        new(StringComparer.Ordinal);

    public IReadOnlyList<PostExposureTransition> Update(
        string stimulusId,
        double visibleRatio,
        long elapsedMilliseconds)
    {
        visibleRatio = Math.Clamp(visibleRatio, 0, 1);
        if (!_states.TryGetValue(stimulusId, out var state))
        {
            state = new ExposureState();
            _states.Add(stimulusId, state);
        }

        state.LastVisibleRatio = visibleRatio;
        var transitions = new List<PostExposureTransition>(2);
        var inViewport = visibleRatio >= ViewportEntryThreshold;
        if (inViewport && !state.IsInViewport)
        {
            state.IsInViewport = true;
            transitions.Add(Create(
                stimulusId,
                PostExposureTransitionType.EnteredViewport,
                elapsedMilliseconds,
                visibleRatio,
                state));
        }

        var aboveMeaningfulThreshold =
            visibleRatio >= MeaningfulVisibilityThreshold;
        if (aboveMeaningfulThreshold && !state.IsAboveMeaningfulThreshold)
        {
            state.IsAboveMeaningfulThreshold = true;
            state.MeaningfulCandidateSinceMilliseconds = elapsedMilliseconds;
        }
        else if (!aboveMeaningfulThreshold && state.IsAboveMeaningfulThreshold)
        {
            EndMeaningfulInterval(stimulusId, elapsedMilliseconds, visibleRatio, state, transitions);
        }

        if (state.IsAboveMeaningfulThreshold && !state.IsMeaningfullyExposed &&
            elapsedMilliseconds - state.MeaningfulCandidateSinceMilliseconds >=
            MinimumMeaningfulExposureMilliseconds)
        {
            var first = !state.WasEverMeaningfullyExposed;
            state.IsMeaningfullyExposed = true;
            state.WasEverMeaningfullyExposed = true;
            transitions.Add(new PostExposureTransition
            {
                StimulusId = stimulusId,
                Type = PostExposureTransitionType.MeaningfullyExposed,
                IsFirstExposure = first,
                SessionElapsedMilliseconds = elapsedMilliseconds,
                VisibleDurationMilliseconds = elapsedMilliseconds -
                    state.MeaningfulCandidateSinceMilliseconds,
                CumulativeVisibleMilliseconds = state.CumulativeMeaningfulMilliseconds,
                VisibleRatio = visibleRatio
            });
        }

        if (!inViewport && state.IsInViewport)
        {
            state.IsInViewport = false;
            transitions.Add(Create(
                stimulusId,
                PostExposureTransitionType.ExitedViewport,
                elapsedMilliseconds,
                visibleRatio,
                state));
        }

        return transitions;
    }

    public IReadOnlyList<PostExposureTransition> HideAll(long elapsedMilliseconds)
    {
        var transitions = new List<PostExposureTransition>();
        foreach (var stimulusId in _states.Keys.ToList())
            transitions.AddRange(Update(stimulusId, 0, elapsedMilliseconds));
        return transitions;
    }

    public long TotalVisibleMilliseconds =>
        _states.Values.Sum(state => state.CumulativeMeaningfulMilliseconds);

    public int ExposedCount =>
        _states.Values.Count(state => state.WasEverMeaningfullyExposed);

    private static void EndMeaningfulInterval(
        string stimulusId,
        long elapsedMilliseconds,
        double visibleRatio,
        ExposureState state,
        ICollection<PostExposureTransition> transitions)
    {
        state.IsAboveMeaningfulThreshold = false;
        if (!state.IsMeaningfullyExposed)
            return;

        var duration = Math.Max(
            0,
            elapsedMilliseconds - state.MeaningfulCandidateSinceMilliseconds);
        state.CumulativeMeaningfulMilliseconds += duration;
        state.IsMeaningfullyExposed = false;
        transitions.Add(new PostExposureTransition
        {
            StimulusId = stimulusId,
            Type = PostExposureTransitionType.MeaningfulExposureEnded,
            SessionElapsedMilliseconds = elapsedMilliseconds,
            VisibleDurationMilliseconds = duration,
            CumulativeVisibleMilliseconds = state.CumulativeMeaningfulMilliseconds,
            VisibleRatio = visibleRatio
        });
    }

    private static PostExposureTransition Create(
        string stimulusId,
        PostExposureTransitionType type,
        long elapsedMilliseconds,
        double visibleRatio,
        ExposureState state) => new()
    {
        StimulusId = stimulusId,
        Type = type,
        SessionElapsedMilliseconds = elapsedMilliseconds,
        CumulativeVisibleMilliseconds = state.CumulativeMeaningfulMilliseconds,
        VisibleRatio = visibleRatio
    };

    private sealed class ExposureState
    {
        public bool IsInViewport { get; set; }
        public bool IsAboveMeaningfulThreshold { get; set; }
        public bool IsMeaningfullyExposed { get; set; }
        public bool WasEverMeaningfullyExposed { get; set; }
        public long MeaningfulCandidateSinceMilliseconds { get; set; }
        public long CumulativeMeaningfulMilliseconds { get; set; }
        public double LastVisibleRatio { get; set; }
    }
}
