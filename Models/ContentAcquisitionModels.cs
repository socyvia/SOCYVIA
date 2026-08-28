using System;
using System.Collections.Generic;

namespace SOCYVIA.Models;

public enum ContentAcquisitionStatus
{
    Success,
    Partial,
    Unsupported,
    AuthenticationRequired,
    Error
}

public sealed class ContentAcquisitionRequest
{
    public string Url { get; init; } = string.Empty;
}

public sealed class AcquiredContentMetadata
{
    public string OriginalUrl { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? BodyText { get; init; }
    public string? AuthorName { get; init; }
    public string? SourceName { get; init; }
    public string? Platform { get; init; }
    public string? ContentType { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? SourceMetadataJson { get; init; }
    public IReadOnlyList<string> UnavailableFields { get; init; } =
        Array.Empty<string>();
}

public sealed class ContentAcquisitionResult
{
    public ContentAcquisitionStatus Status { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public string CanonicalMessage { get; init; } = string.Empty;
    public AcquiredContentMetadata? Metadata { get; init; }
    public DateTime AcquiredAtUtc { get; init; } = DateTime.UtcNow;
    public bool CanUseManualFallback { get; init; } = true;
}
