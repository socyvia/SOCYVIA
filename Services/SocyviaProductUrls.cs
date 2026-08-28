using System;
using System.Diagnostics;

namespace SOCYVIA.Services;

/// <summary>
/// Official product URLs. Researcher-facing navigation goes through this one
/// boundary so preview, live-study links, and release infrastructure cannot be
/// confused with local development endpoints.
/// </summary>
public static class SocyviaProductUrls
{
    public const string ParticipantDemoUrl = "https://socyvia.com/experimentfeed/demo";
    public const string ReleaseManifestUrl = "https://socyvia.com/releases/manifest.json";
    public const string CloudflareMediaStorageSetupUrl = "https://dash.cloudflare.com/?to=/:account/r2/overview";

    public static Uri ParticipantDemoUri { get; } = new(ParticipantDemoUrl, UriKind.Absolute);
    public static Uri ReleaseManifestUri { get; } = new(ReleaseManifestUrl, UriKind.Absolute);
    public static Uri CloudflareMediaStorageSetupUri { get; } = new(CloudflareMediaStorageSetupUrl, UriKind.Absolute);

    public static void OpenInDefaultBrowser(Uri uri, Action<Uri>? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("SOCYVIA product links must use HTTPS.");
        }

        if (launcher is not null)
        {
            launcher(uri);
            return;
        }

        var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        if (process is null)
        {
            throw new InvalidOperationException("The operating system did not open the default browser.");
        }
    }

    public static void OpenParticipantDemo(Action<Uri>? launcher = null) =>
        OpenInDefaultBrowser(ParticipantDemoUri, launcher);

    public static void OpenCloudflareMediaStorageSetup(Action<Uri>? launcher = null) =>
        OpenInDefaultBrowser(CloudflareMediaStorageSetupUri, launcher);
}
