using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services.ContentAcquisition;

public sealed class RestrictedSocialContentProvider : IContentAcquisitionProvider
{
    private static readonly string[] RestrictedHosts =
    {
        "instagram.com", "facebook.com", "tiktok.com",
        "twitter.com", "x.com"
    };

    public string ProviderId => "RestrictedSocialPlatform";

    public bool CanHandle(Uri uri) => RestrictedHosts.Any(host =>
        uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith('.' + host, StringComparison.OrdinalIgnoreCase));

    public Task<ContentAcquisitionResult> AcquireAsync(
        ContentAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ContentAcquisitionResult
        {
            Status = ContentAcquisitionStatus.AuthenticationRequired,
            ProviderId = ProviderId,
            CanonicalMessage =
                "This platform does not expose dependable public metadata without an approved provider integration. Preserve the URL and complete available fields manually.",
            Metadata = new AcquiredContentMetadata
            {
                OriginalUrl = request.Url,
                Platform = PlatformName(request.Url),
                ContentType = "Mixed",
                UnavailableFields = new[]
                {
                    "title", "author", "publicationTimestamp",
                    "media", "engagementMetrics"
                }
            },
            AcquiredAtUtc = DateTime.UtcNow,
            CanUseManualFallback = true
        });
    }

    private static string PlatformName(string url)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        if (host.Contains("instagram")) return "Instagram";
        if (host.Contains("facebook")) return "Facebook";
        if (host.Contains("tiktok")) return "TikTok";
        if (host.Contains("twitter") || host.EndsWith("x.com")) return "X";
        return "Social";
    }
}
