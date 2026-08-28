using System;

namespace SOCYVIA.Models;

public sealed class EngagementObservation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ContentItemId { get; set; } = string.Empty;
    public long? Likes { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Saves { get; set; }
    public long? Views { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string ObservationSource { get; set; } = "Manual";
    public string? SourceMetadataJson { get; set; }
}
