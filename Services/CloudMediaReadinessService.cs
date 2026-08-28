using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services;

public enum CloudMediaReadinessState { TextOnlyReady, RemoteMediaUrlMissing, Ready }

/// <summary>Product-language readiness boundary; no worker, R2, or upload call is performed here.</summary>
public sealed record CloudMediaReadiness(CloudMediaReadinessState State, string Title, string Message, bool CanPublish, bool RequiresCloudConnection, int RequiredAssetCount = 0, string? MediaAssetContentId = null);

public sealed record MediaPublishProgress(string Stage, int CompletedAssets, int TotalAssets, string? CurrentFileName = null)
{
    public static MediaPublishProgress Validating() => new("Validating experiment", 0, 0);
    public static MediaPublishProgress Preparing(int total) => new("Preparing media", 0, total);
    public static MediaPublishProgress Uploading(int completed, int total, string fileName) => new("Uploading media", completed, total, fileName);
}

public static class CloudMediaReadinessService
{
    public static async Task<CloudMediaReadiness> EvaluateAsync(
        IReadOnlyList<MediaManifestAsset>? media,
        CloudflareProviderConfiguration? cloud,
        CancellationToken cancellationToken = default)
    {
        var result = Evaluate(media, cloud);
        if (!result.CanPublish || media is null) return result;

        var published = media.Where(asset => !asset.RequiredForDeployment &&
                                              !string.IsNullOrWhiteSpace(asset.DeploymentUrl))
            .ToArray();
        if (published.Length == 0) return result;

        var checks = await Task.WhenAll(published.Select(async asset =>
        {
            if (!PublishedMediaUrlValidator.TryValidateDirectMedia(asset.DeploymentUrl, out var uri, out _))
                return asset;
            return await PublishedMediaUrlValidator.ResolvesToPublicInternetAsync(uri!, cancellationToken)
                ? null
                : asset;
        }));
        var inaccessible = checks.Where(asset => asset is not null).Cast<MediaManifestAsset>().ToArray();
        return inaccessible.Length == 0
            ? result
            : new CloudMediaReadiness(
                CloudMediaReadinessState.RemoteMediaUrlMissing,
                "Published media source unavailable",
                $"SOCYVIA could not resolve {inaccessible.Length} published media source{(inaccessible.Length == 1 ? string.Empty : "s")} to a public participant-accessible address.",
                false,
                false,
                inaccessible.Length,
                inaccessible.Select(item => item.ContentId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
    }

    public static CloudMediaReadiness Evaluate(IReadOnlyList<MediaManifestAsset>? media, CloudflareProviderConfiguration? cloud)
    {
        var allMedia = media?.ToArray() ?? Array.Empty<MediaManifestAsset>();
        var unresolvedLocalMedia = allMedia.Where(asset => asset.RequiredForDeployment).ToArray();
        if (allMedia.Length == 0)
            return new(CloudMediaReadinessState.TextOnlyReady, "No remote media location required", "This experiment is text-only and does not require media storage.", true, false, 0);
        if (unresolvedLocalMedia.Length == 0)
            return new(CloudMediaReadinessState.Ready, "Published media URLs ready", "Every media item has a participant-accessible HTTPS publication URL.", true, false, 0);
        if (cloud?.HasRequiredResourceIdentity == true)
            return new(CloudMediaReadinessState.Ready, "Optional cloud media storage ready", $"{unresolvedLocalMedia.Length} local media file{(unresolvedLocalMedia.Length == 1 ? string.Empty : "s")} can use the explicitly configured optional cloud storage.", true, false, unresolvedLocalMedia.Length);
        return new(CloudMediaReadinessState.RemoteMediaUrlMissing, "Remote media location required", $"This experiment contains {unresolvedLocalMedia.Length} local media file{(unresolvedLocalMedia.Length == 1 ? string.Empty : "s")} that need participant-accessible HTTPS URLs before publishing.", false, false, unresolvedLocalMedia.Length, unresolvedLocalMedia.Select(item => item.ContentId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
    }
}
