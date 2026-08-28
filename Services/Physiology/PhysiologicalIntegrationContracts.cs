using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services.Physiology;

// Extension contracts only. No device or LSL support is claimed by this build.
public enum PhysiologicalCapability
{
    Eeg,
    GsrEda,
    EyeTracking
}

public interface IDeviceConnector : IAsyncDisposable
{
    string ConnectorId { get; }
    IReadOnlySet<PhysiologicalCapability> Capabilities { get; }
    Task<IReadOnlyList<PhysiologicalDeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default);
    Task<DeviceSession> ConnectAsync(
        PhysiologicalDeviceDescriptor device,
        CancellationToken cancellationToken = default);
}

public sealed record PhysiologicalDeviceDescriptor(
    string Id,
    string DisplayName,
    string DeviceFamily,
    string ConnectorId);

public sealed record DeviceSession(
    string Id,
    string ExperimentSessionId,
    PhysiologicalDeviceDescriptor Device,
    DateTime StartedAtUtc);

public sealed record PhysiologicalStreamSample(
    string DeviceSessionId,
    string StreamName,
    long MonotonicTimestamp,
    DateTime TimestampUtc,
    IReadOnlyList<double> Values);

public sealed record EyeTrackingSample(
    string DeviceSessionId,
    long MonotonicTimestamp,
    DateTime TimestampUtc,
    double GazeX,
    double GazeY,
    double? Confidence,
    double? LeftPupilDiameter,
    double? RightPupilDiameter,
    string? AreaOfInterestId);

public sealed record EyeTrackingEvent(
    string DeviceSessionId,
    string EventType,
    long StartedAtMonotonicTimestamp,
    long? EndedAtMonotonicTimestamp,
    string? AreaOfInterestId,
    string? MetadataJson);

public sealed record SynchronizationMarker(
    string ExperimentSessionId,
    string EventType,
    long SessionElapsedMilliseconds,
    DateTime TimestampUtc,
    int SequenceNumber);
