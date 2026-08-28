using System;
using System.Threading.Tasks;

namespace SOCYVIA.Services;

/// <summary>Single validated boundary for participant-link copy and launch actions.</summary>
public static class ParticipantLinkActionService
{
    public static async Task<Uri> CopyAsync(
        PublishedExperimentStatus publication,
        Func<string, Task> clipboardWriter)
    {
        ArgumentNullException.ThrowIfNull(clipboardWriter);
        var uri = RequireDistributable(publication);
        await clipboardWriter(uri.AbsoluteUri);
        return uri;
    }

    public static Uri Open(PublishedExperimentStatus publication, Action<Uri>? launcher = null)
    {
        var uri = RequireDistributable(publication);
        SocyviaProductUrls.OpenInDefaultBrowser(uri, launcher);
        return uri;
    }

    private static Uri RequireDistributable(PublishedExperimentStatus publication) =>
        PublicExperimentLinkService.DistributableCanonicalUri(publication)
        ?? throw new InvalidOperationException("The canonical participant route is not live.");
}
