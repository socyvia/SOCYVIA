using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Text.Json;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services.Telemetry;

public sealed class TelemetryWriteFailureEventArgs : EventArgs
{
    public required Exception Exception { get; init; }
}

public sealed class BufferedTelemetryService : IAsyncDisposable
{
    private const int Capacity = 512;
    private const int BatchSize = 48;

    private readonly ExperimentSession _session;
    private readonly ITelemetrySink _sink;
    private readonly Channel<QueueItem> _channel;
    private readonly Task _worker;
    private int _nextSequence;
    private Exception? _failure;
    private bool _stopped;

    private BufferedTelemetryService(
        ExperimentSession session,
        ITelemetrySink sink,
        int nextSequence)
    {
        _session = session;
        _sink = sink;
        _nextSequence = nextSequence;
        _channel = Channel.CreateBounded<QueueItem>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        _worker = ProcessAsync();
    }

    public event EventHandler<TelemetryWriteFailureEventArgs>?
        WriteFailed;

    public Exception? LastFailure => _failure;

    public static async Task<BufferedTelemetryService> CreateAsync(
        ExperimentSession session,
        ITelemetrySink? sink = null)
    {
        var nextSequence = await InteractionEventRepository
            .GetNextSequenceNumberAsync(session.Id);
        return new BufferedTelemetryService(
            session,
            sink ?? new SqliteTelemetrySink(),
            nextSequence);
    }

    public async ValueTask TrackAsync(
        string eventType,
        long elapsedMilliseconds,
        string? stimulusId = null,
        int? stimulusOrderIndex = null,
        long? stimulusElapsedMilliseconds = null,
        long? durationMilliseconds = null,
        string? target = null,
        string? valueText = null,
        string? previousValueText = null,
        double? valueNumber = null,
        bool? valueBoolean = null,
        double? scrollPosition = null,
        double? scrollDepthPercent = null,
        string? metadataJson = null,
        double? pointerX = null,
        double? pointerY = null,
        int? screenWidth = null,
        int? screenHeight = null)
    {
        ThrowIfUnavailable();
        var sequence = Interlocked.Increment(ref _nextSequence) - 1;
        var interactionEvent = new InteractionEvent
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = _session.StudyId,
            SessionId = _session.Id,
            ParticipantId = _session.ParticipantId,
            GroupId = _session.GroupId,
            SnapshotStimulusId = stimulusId,
            EventType = eventType,
            TimestampUtc = DateTime.UtcNow,
            SessionElapsedMilliseconds = elapsedMilliseconds,
            StimulusElapsedMilliseconds = stimulusElapsedMilliseconds,
            DurationMilliseconds = durationMilliseconds,
            SequenceNumber = sequence,
            Target = target,
            ValueText = valueText,
            PreviousValueText = previousValueText,
            ValueNumber = valueNumber,
            ValueBoolean = valueBoolean,
            ScrollPosition = scrollPosition,
            ScrollDepthPercent = scrollDepthPercent,
            StimulusOrderIndex = stimulusOrderIndex,
            PointerX = pointerX,
            PointerY = pointerY,
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            MetadataJson = JsonSerializer.Serialize(new
            {
                conditionId = _session.ConditionId,
                configurationSnapshotId = _session.ConfigurationSnapshotId,
                details = metadataJson
            })
        };
        await _channel.Writer.WriteAsync(new EventItem(interactionEvent));
    }

    public async Task FlushAsync()
    {
        ThrowIfUnavailable();
        var completion = NewCompletion();
        await _channel.Writer.WriteAsync(new FlushItem(completion));
        await completion.Task;
        ThrowIfUnavailable();
    }

    public async Task SynchronizeSequenceAsync()
    {
        await FlushAsync();
        var nextSequence = await InteractionEventRepository
            .GetNextSequenceNumberAsync(_session.Id);
        Interlocked.Exchange(ref _nextSequence, nextSequence);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (_failure is not null)
        {
            _channel.Writer.TryComplete(_failure);
            await _worker;
            return;
        }
        var completion = NewCompletion();
        await _channel.Writer.WriteAsync(new StopItem(completion));
        _channel.Writer.TryComplete();
        await completion.Task;
        await _worker;
    }

    private async Task ProcessAsync()
    {
        var batch = new List<InteractionEvent>(BatchSize);
        try
        {
            while (true)
            {
                if (batch.Count == 0)
                {
                    if (!await _channel.Reader.WaitToReadAsync())
                    {
                        return;
                    }
                }
                else
                {
                    var dataAvailable =
                        _channel.Reader.WaitToReadAsync().AsTask();
                    var flushInterval = Task.Delay(250);
                    var completed = await Task.WhenAny(
                        dataAvailable,
                        flushInterval);
                    if (completed == flushInterval)
                    {
                        await WriteWithRetryAsync(batch);
                        batch.Clear();
                        continue;
                    }
                    if (!await dataAvailable)
                    {
                        await WriteWithRetryAsync(batch);
                        return;
                    }
                }

                while (_channel.Reader.TryRead(out var item))
                {
                    if (item is EventItem eventItem)
                    {
                        batch.Add(eventItem.Event);
                        if (batch.Count >= BatchSize)
                        {
                            await WriteWithRetryAsync(batch);
                            batch.Clear();
                        }
                        continue;
                    }

                    await WriteWithRetryAsync(batch);
                    batch.Clear();

                    if (item is FlushItem flush)
                    {
                        flush.Completion.TrySetResult();
                        continue;
                    }

                    if (item is StopItem stop)
                    {
                        stop.Completion.TrySetResult();
                        return;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _failure = exception;
            _channel.Writer.TryComplete(exception);
            WriteFailed?.Invoke(
                this,
                new TelemetryWriteFailureEventArgs
                {
                    Exception = exception
                });
            while (_channel.Reader.TryRead(out var pending))
            {
                pending.Completion?.TrySetException(exception);
            }
        }
    }

    private async Task WriteWithRetryAsync(
        IReadOnlyList<InteractionEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _sink.WriteBatchAsync(events);
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt < 2)
                {
                    await Task.Delay(75 * (attempt + 1));
                }
            }
        }

        throw new InvalidOperationException(
            "The local telemetry batch could not be saved after three attempts.",
            lastException);
    }

    private void ThrowIfUnavailable()
    {
        if (_failure is not null)
        {
            throw new InvalidOperationException(
                "Participant telemetry is unavailable.",
                _failure);
        }
        if (_stopped)
        {
            throw new ObjectDisposedException(
                nameof(BufferedTelemetryService));
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private abstract record QueueItem
    {
        public virtual TaskCompletionSource? Completion => null;
    }

    private sealed record EventItem(InteractionEvent Event) : QueueItem;

    private sealed record FlushItem(
        TaskCompletionSource Signal) : QueueItem
    {
        public override TaskCompletionSource Completion => Signal;
    }

    private sealed record StopItem(
        TaskCompletionSource Signal) : QueueItem
    {
        public override TaskCompletionSource Completion => Signal;
    }
}
