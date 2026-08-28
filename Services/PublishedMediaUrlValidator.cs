using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

/// <summary>
/// Validates the participant-facing location of published media. The pure
/// validation methods do not perform I/O; the async resolution check provides
/// the final public-DNS guard used by publication readiness.
/// </summary>
public static class PublishedMediaUrlValidator
{
    private static readonly string[] ExternalContentHosts =
    [
        "youtube.com", "youtu.be", "facebook.com", "fb.watch", "instagram.com",
        "tiktok.com", "x.com", "twitter.com", "linkedin.com", "vimeo.com",
        "dailymotion.com", "twitch.tv"
    ];

    public static bool TryValidate(string? value, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A participant-accessible HTTPS media URL is required.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(candidate.Host) ||
            !string.IsNullOrEmpty(candidate.UserInfo))
        {
            error = "Published media must use a valid absolute HTTPS URL.";
            return false;
        }

        var host = candidate.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase) ||
            IsDocumentationOnlyHost(host) ||
            (!host.Contains('.') && !IPAddress.TryParse(host, out _)) ||
            IPAddress.TryParse(host, out var address) && IsPrivateOrLocal(address))
        {
            error = "Published media must be reachable by participants on the public internet.";
            return false;
        }

        uri = candidate;
        return true;
    }

    /// <summary>
    /// Validates a source intended for an image, video, or audio element.
    /// Social-platform and ordinary platform pages belong to the existing
    /// External Link workflow. File-name extensions are deliberately optional.
    /// </summary>
    public static bool TryValidateDirectMedia(string? value, out Uri? uri, out string? error)
    {
        if (!TryValidate(value, out uri, out error)) return false;
        if (!IsExternalContentPage(uri!)) return true;
        uri = null;
        error = "This URL identifies external/social content. Use Add External Link instead of a direct media source.";
        return false;
    }

    public static bool IsExternalContentPage(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        return ExternalContentHosts.Any(candidate =>
            host.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith('.' + candidate, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<bool> ResolvesToPublicInternetAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        if (IPAddress.TryParse(uri.IdnHost, out var literal))
            return !IsPrivateOrLocal(literal);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, timeout.Token);
            return addresses.Length > 0 && addresses.All(address => !IsPrivateOrLocal(address));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsDocumentationOnlyHost(string host) =>
        new[] { "example.com", "example.org", "example.net" }.Any(candidate =>
            host.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith('.' + candidate, StringComparison.OrdinalIgnoreCase));

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && (bytes[1] == 0 || bytes[1] == 168) ||
                   bytes[0] == 198 && (bytes[1] is 18 or 19 or 51) ||
                   bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                   bytes[0] == 0 || bytes[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                   (bytes[0] & 0xFE) == 0xFC ||
                   bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
        }

        return true;
    }
}
