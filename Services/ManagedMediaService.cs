using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services;

public static class ManagedMediaService
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".m4v"];
    private static readonly string[] AudioExtensions = [".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    public static async Task<ManagedMediaAsset> StageFileAsync(
        string researcherId,
        string sourcePath,
        string? mediaKind = null,
        bool isDemo = false,
        CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await StageStreamAsync(
            researcherId,
            source,
            Path.GetFileName(sourcePath),
            mediaKind,
            isDemo,
            cancellationToken);
    }

    public static async Task<ManagedMediaAsset> StageStreamAsync(
        string researcherId,
        Stream source,
        string originalFileName,
        string? mediaKind = null,
        bool isDemo = false,
        CancellationToken cancellationToken = default)
    {
        StorageService.InitializeResearcherWorkspace(researcherId);
        var extension = NormalizeExtension(Path.GetExtension(originalFileName));
        var assetId = Guid.NewGuid().ToString();
        var relativePath = Path.Combine("media", assetId + extension);
        var absolutePath = ResolveAbsolutePath(researcherId, relativePath);

        await using (var target = new FileStream(
                         absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var bytes = new FileInfo(absolutePath).Length;
        var hash = await ComputeSha256Async(absolutePath, cancellationToken);
        // File type is derived from the staged bytes' extension, never trusted from a UI selection.
        var kind = DetectKind(extension);
        if (kind == "File")
        {
            File.Delete(absolutePath);
            throw new NotSupportedException("SOCYVIA currently supports image, audio, and video media files only.");
        }
        return new ManagedMediaAsset
        {
            Id = assetId,
            ResearcherId = researcherId,
            MediaKind = kind,
            OriginalFileName = Path.GetFileName(originalFileName),
            RelativePath = relativePath,
            MimeType = DetectMimeType(extension),
            ByteLength = bytes,
            Sha256 = hash,
            MetadataJson = JsonSerializer.Serialize(new
            {
                ManagedBy = "SOCYVIA",
                OriginalExtension = extension,
                ImportedAtUtc = DateTime.UtcNow
            }),
            IsDemo = isDemo,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static async Task PersistAsync(ManagedMediaAsset asset, string contentItemId)
    {
        asset.ContentItemId = contentItemId;
        await ManagedMediaAssetRepository.CreateAsync(asset);
    }

    public static string ResolveAbsolutePath(ManagedMediaAsset asset) =>
        ResolveAbsolutePath(asset.ResearcherId, asset.RelativePath);

    public static string ResolveAbsolutePath(string researcherId, string relativePath)
    {
        var root = Path.GetFullPath(StorageService.GetResearcherFolder(researcherId));
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Managed media path escaped the researcher workspace.");
        return candidate;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
            return ".bin";
        foreach (var character in extension)
            if (character != '.' && !char.IsLetterOrDigit(character)) return ".bin";
        return extension.ToLowerInvariant();
    }

    private static string DetectKind(string extension)
    {
        if (Array.IndexOf(ImageExtensions, extension) >= 0) return "Image";
        if (Array.IndexOf(VideoExtensions, extension) >= 0) return "Video";
        if (Array.IndexOf(AudioExtensions, extension) >= 0) return "Audio";
        return "File";
    }

    private static string? DetectMimeType(string extension) => extension switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp", ".bmp" => "image/bmp",
        ".mp4" or ".m4v" => "video/mp4", ".webm" => "video/webm",
        ".mov" => "video/quicktime", ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg", ".m4a" => "audio/mp4",
        ".ogg" => "audio/ogg", ".flac" => "audio/flac", _ => null
    };
}
