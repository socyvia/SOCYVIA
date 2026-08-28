using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

public enum StudySaveState { Saved, Saving, UnsavedChanges, SaveFailed }

/// <summary>One debounced, serialized design-save boundary per study.</summary>
public sealed class StudySaveCoordinator : IDisposable
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _debounceCancellation;
    private Func<CancellationToken, Task>? _pendingSave;
    private long _generation;
    private bool _disposed;

    public StudySaveCoordinator(TimeSpan? debounce = null) =>
        _debounce = debounce ?? TimeSpan.FromMilliseconds(700);

    public StudySaveState State { get; private set; } = StudySaveState.Saved;
    public event EventHandler<StudySaveState>? StateChanged;

    public void MarkDirty(Func<CancellationToken, Task> save)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pendingSave = save ?? throw new ArgumentNullException(nameof(save));
        Interlocked.Increment(ref _generation);
        SetState(StudySaveState.UnsavedChanges);
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _debounceCancellation = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_debounceCancellation.Token);
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _debounceCancellation?.Cancel();
        return await SavePendingAsync(cancellationToken);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken);
            await SavePendingAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> SavePendingAsync(CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            var save = _pendingSave;
            if (save is null) return State != StudySaveState.SaveFailed;
            var savingGeneration = Interlocked.Read(ref _generation);
            SetState(StudySaveState.Saving);
            try
            {
                await save(cancellationToken);
                if (savingGeneration == Interlocked.Read(ref _generation))
                {
                    _pendingSave = null;
                    SetState(StudySaveState.Saved);
                }
                else SetState(StudySaveState.UnsavedChanges);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                SetState(StudySaveState.SaveFailed);
                return false;
            }
        }
        finally { _saveGate.Release(); }
    }

    private void SetState(StudySaveState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        _saveGate.Dispose();
    }
}

public static class StudySaveCoordinatorRegistry
{
    private static readonly ConcurrentDictionary<string, StudySaveCoordinator> Coordinators = new(StringComparer.Ordinal);

    public static StudySaveCoordinator ForStudy(string studyId)
    {
        if (string.IsNullOrWhiteSpace(studyId)) throw new ArgumentException("Study ID is required.", nameof(studyId));
        return Coordinators.GetOrAdd(studyId, _ => new StudySaveCoordinator());
    }

    public static async Task<bool> FlushAsync(string studyId, CancellationToken cancellationToken = default) =>
        await ForStudy(studyId).FlushAsync(cancellationToken);

    public static bool HasUnsafeChanges => Coordinators.Values.Any(coordinator =>
        coordinator.State is StudySaveState.Saving or StudySaveState.UnsavedChanges or StudySaveState.SaveFailed);

    public static async Task<bool> FlushAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var coordinator in Coordinators.Values)
            if (!await coordinator.FlushAsync(cancellationToken)) return false;
        return true;
    }
}
