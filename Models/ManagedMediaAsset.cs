using System;

namespace SOCYVIA.Models;

public sealed class ManagedMediaAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ResearcherId { get; set; } = string.Empty;
    public string? ContentItemId { get; set; }
    public string MediaKind { get; set; } = "File";
    public string OriginalFileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public bool IsDemo { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DemoExperienceStatus
{
    public bool IsInstalled { get; init; }
    public string? StudyId { get; init; }
    public int DemoVersion { get; init; }
}
