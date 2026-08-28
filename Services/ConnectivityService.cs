using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

public enum ConnectivityState { Checking, Online, Offline }

public sealed record ConnectivitySnapshot(ConnectivityState State, DateTime CheckedAtUtc);

/// <summary>
/// Process-wide HTTPS reachability monitor. It represents internet access only,
/// never Cloudflare authorization or cached cloud configuration.
/// </summary>
public static class ConnectivityService
{
    private static readonly object Gate = new();
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly IReadOnlyList<Uri> DefaultProbes =
    [
        new("https://socyvia.com/experimentfeed/api/health")
    ];
    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private static CancellationTokenSource? _monitorCancellation;
    private static Task? _monitorTask;
    private static int _monitorConsumers;
    private static ConnectivitySnapshot _current = new(ConnectivityState.Checking, DateTime.UtcNow);

    public static ConnectivitySnapshot Current
    {
        get { lock (Gate) return _current; }
    }

    public static event EventHandler<ConnectivitySnapshot>? StateChanged;

    static ConnectivityService()
    {
        NetworkChange.NetworkAvailabilityChanged += (_, _) => OnNetworkChanged();
        NetworkChange.NetworkAddressChanged += (_, _) => OnNetworkChanged();
    }

    public static void StartMonitoring()
    {
        lock (Gate)
        {
            _monitorConsumers++;
            if (_monitorTask is not null) return;
            _monitorCancellation = new CancellationTokenSource();
            _monitorTask = MonitorAsync(_monitorCancellation.Token);
        }
    }

    public static void StopMonitoring()
    {
        lock (Gate)
        {
            if (_monitorConsumers > 0) _monitorConsumers--;
            if (_monitorConsumers != 0 || _monitorCancellation is null) return;
            _monitorCancellation.Cancel();
            _monitorCancellation.Dispose();
            _monitorCancellation = null;
            _monitorTask = null;
        }
    }

    public static async Task<ConnectivitySnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        SetCurrent(new(ConnectivityState.Checking, DateTime.UtcNow));
        await CheckGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await CheckAsync(cancellationToken: cancellationToken);
            SetCurrent(snapshot);
            return snapshot;
        }
        finally { CheckGate.Release(); }
    }

    public static async Task<ConnectivitySnapshot> CheckAsync(
        HttpClient? httpClient = null,
        IReadOnlyList<Uri>? probes = null,
        CancellationToken cancellationToken = default)
    {
        if (httpClient is null && !NetworkInterface.GetIsNetworkAvailable())
            return new(ConnectivityState.Offline, DateTime.UtcNow);

        var client = httpClient ?? SharedClient;
        foreach (var endpoint in probes ?? DefaultProbes)
        {
            if (!endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                // Receiving an HTTPS response proves internet reachability even if
                // the SOCYVIA runtime itself is unhealthy. Cloud readiness is a
                // separate state and must never redefine the global network badge.
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return new(ConnectivityState.Online, DateTime.UtcNow);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException) { }
        }
        return new(ConnectivityState.Offline, DateTime.UtcNow);
    }

    private static async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static void OnNetworkChanged()
    {
        CancellationToken token;
        lock (Gate)
        {
            if (_monitorCancellation is null) return;
            token = _monitorCancellation.Token;
        }
        SetCurrent(new(ConnectivityState.Checking, DateTime.UtcNow));
        _ = RefreshAfterNetworkChangeAsync(token);
    }

    private static async Task RefreshAfterNetworkChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static void SetCurrent(ConnectivitySnapshot snapshot)
    {
        EventHandler<ConnectivitySnapshot>? changed;
        lock (Gate)
        {
            if (_current.State == snapshot.State && snapshot.State == ConnectivityState.Checking) return;
            _current = snapshot;
            changed = StateChanged;
        }
        changed?.Invoke(null, snapshot);
    }
}
