using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services.ContentAcquisition;

public sealed partial class GenericWebMetadataProvider : IContentAcquisitionProvider
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    public string ProviderId => "GenericWebMetadata";

    public bool CanHandle(Uri uri) =>
        uri.Scheme is "http" or "https";

    public async Task<ContentAcquisitionResult> AcquireAsync(
        ContentAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var acquiredAt = DateTime.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await Client.GetAsync(
                request.Url,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaximumResponseBytes)
            {
                return Error("The page metadata response is too large to inspect safely.", acquiredAt);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return new ContentAcquisitionResult
                {
                    Status = ContentAcquisitionStatus.Partial,
                    ProviderId = ProviderId,
                    CanonicalMessage = "The URL is reachable, but it is not an HTML metadata page.",
                    Metadata = new AcquiredContentMetadata
                    {
                        OriginalUrl = request.Url,
                        SourceName = new Uri(request.Url).Host,
                        ContentType = InferContentType(mediaType),
                        UnavailableFields = new[] { "title", "author", "publicationTimestamp", "engagementMetrics" }
                    },
                    AcquiredAtUtc = acquiredAt
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[MaximumResponseBytes + 1];
            var readTotal = 0;
            while (readTotal < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(readTotal), timeout.Token);
                if (read == 0) break;
                readTotal += read;
            }
            if (readTotal > MaximumResponseBytes)
            {
                return Error("The page is too large to inspect safely.", acquiredAt);
            }

            var html = System.Text.Encoding.UTF8.GetString(buffer, 0, readTotal);
            var title = Meta(html, "property", "og:title") ??
                        Meta(html, "name", "twitter:title") ??
                        MatchValue(TitleRegex(), html);
            var description = Meta(html, "property", "og:description") ??
                              Meta(html, "name", "description") ??
                              Meta(html, "name", "twitter:description");
            var author = Meta(html, "name", "author");
            var image = Meta(html, "property", "og:image") ??
                        Meta(html, "name", "twitter:image");
            var published = Meta(html, "property", "article:published_time") ??
                            Meta(html, "name", "date") ??
                            Meta(html, "itemprop", "datePublished");
            var hasPublishedAt = DateTime.TryParse(
                published,
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var publishedAt);
            var responseUri = response.RequestMessage?.RequestUri ?? new Uri(request.Url);
            var canonicalValue = Meta(html, "property", "og:url") ??
                                 Link(html, "canonical") ??
                                 responseUri.ToString();
            var canonicalUri = Uri.TryCreate(responseUri, canonicalValue, out var resolvedCanonical)
                ? resolvedCanonical
                : responseUri;
            var thumbnailUri = Uri.TryCreate(responseUri, image, out var resolvedImage)
                ? resolvedImage.ToString()
                : image;
            var sourceName = Clean(Meta(html, "property", "og:site_name")) ?? responseUri.Host;
            var unavailable = new List<string> { "engagementMetrics" };
            if (author is null) unavailable.Add("author");
            if (published is null) unavailable.Add("publicationTimestamp");
            if (image is null) unavailable.Add("thumbnail");

            return new ContentAcquisitionResult
            {
                Status = unavailable.Count == 1
                    ? ContentAcquisitionStatus.Success
                    : ContentAcquisitionStatus.Partial,
                ProviderId = ProviderId,
                CanonicalMessage = "Available public page metadata was acquired. Engagement values require a permitted provider or manual observation.",
                Metadata = new AcquiredContentMetadata
                {
                    OriginalUrl = canonicalUri.ToString(),
                    Title = Clean(title),
                    BodyText = Clean(description),
                    AuthorName = Clean(author),
                    SourceName = sourceName,
                    Platform = sourceName,
                    ContentType = image is null ? "Link" : "Mixed",
                    PublishedAtUtc = hasPublishedAt ? publishedAt : null,
                    ThumbnailUrl = thumbnailUri,
                    SourceMetadataJson = JsonSerializer.Serialize(new
                    {
                        provider = ProviderId,
                        requestedUri = request.Url,
                        responseUri = responseUri.ToString(),
                        canonicalUri = canonicalUri.ToString(),
                        contentType = mediaType,
                        thumbnailUrl = image
                    }),
                    UnavailableFields = unavailable
                },
                AcquiredAtUtc = acquiredAt,
                CanUseManualFallback = true
            };
        }
        catch (OperationCanceledException)
        {
            return Error("Content acquisition timed out or was cancelled.", acquiredAt);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or UriFormatException)
        {
            return Error("The content source could not be reached safely.", acquiredAt);
        }
    }

    private ContentAcquisitionResult Error(string message, DateTime acquiredAt) => new()
    {
        Status = ContentAcquisitionStatus.Error,
        ProviderId = ProviderId,
        CanonicalMessage = message,
        AcquiredAtUtc = acquiredAt,
        CanUseManualFallback = true
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaximumResponseBytes + 1
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SOCYVIA-ResearchContentAcquisition/1.0");
        return client;
    }

    private static string? Meta(string html, string attribute, string value)
    {
        var escaped = Regex.Escape(value);
        var first = Regex.Match(html,
            $"<meta[^>]+{attribute}=[\"']{escaped}[\"'][^>]+content=[\"'](?<v>.*?)[\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (first.Success) return first.Groups["v"].Value;
        var reverse = Regex.Match(html,
            $"<meta[^>]+content=[\"'](?<v>.*?)[\"'][^>]+{attribute}=[\"']{escaped}[\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return reverse.Success ? reverse.Groups["v"].Value : null;
    }

    private static string? MatchValue(Regex regex, string value)
    {
        var match = regex.Match(value);
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static string? Link(string html, string relation)
    {
        var escaped = Regex.Escape(relation);
        var first = Regex.Match(html,
            $"<link[^>]+rel=[\"']{escaped}[\"'][^>]+href=[\"'](?<v>.*?)[\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (first.Success) return first.Groups["v"].Value;
        var reverse = Regex.Match(html,
            $"<link[^>]+href=[\"'](?<v>.*?)[\"'][^>]+rel=[\"']{escaped}[\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return reverse.Success ? reverse.Groups["v"].Value : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : WebUtility.HtmlDecode(Regex.Replace(value, "\\s+", " ")).Trim();

    private static string InferContentType(string? mediaType) =>
        mediaType?.Split('/')[0] switch
        {
            "image" => "Image",
            "video" => "Video",
            "audio" => "Audio",
            _ => "Link"
        };

    [GeneratedRegex("<title[^>]*>(?<v>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();
}
