using System;

namespace SOCYVIA.Models;

public sealed class ContentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResearcherId { get; set; } = string.Empty;
    public string? LegacyStimulusId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string ContentType { get; set; } = "Text";
    public string Platform { get; set; } = "Generic";
    public string? SourceName { get; set; }
    public string? AuthorName { get; set; }
    public string? OriginalUrl { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string? MediaPath { get; set; }
    public string? ThumbnailPath { get; set; }
    /// <summary>Participant-accessible HTTPS media used for remote publication; local paths remain preview-only.</summary>
    public string? PublishedMediaUrl { get; set; }
    public string? SourceMetadataJson { get; set; }
    public string? Category { get; set; }
    public string? Topic { get; set; }
    public string? Tags { get; set; }
    public string? ResearcherNotes { get; set; }
    public string AcquisitionProvider { get; set; } = "Manual";
    public string AcquisitionStatus { get; set; } = "Manual";
    public bool IsDemo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
