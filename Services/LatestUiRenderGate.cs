using System;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

/// <summary>
/// Serializes asynchronous, programmatic view construction and lets only the
/// newest queued refresh run. This prevents re-entrant selection/language
/// events from appending two copies of the same workspace.
/// </summary>
public sealed class LatestUiRenderGate
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private long _requestedRevision;

    public async Task RunAsync(Func<Task> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        var revision = Interlocked.Increment(ref _requestedRevision);
        await _mutex.WaitAsync();
        try
        {
            if (revision != Volatile.Read(ref _requestedRevision)) return;
            await render();
        }
        finally
        {
            _mutex.Release();
        }
    }
}
