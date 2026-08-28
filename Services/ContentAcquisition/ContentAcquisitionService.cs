using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services.ContentAcquisition;

public sealed class ContentAcquisitionService
{
    private readonly IReadOnlyList<IContentAcquisitionProvider> _providers =
        new IContentAcquisitionProvider[]
        {
            new RestrictedSocialContentProvider(),
            new GenericWebMetadataProvider()
        };

    public async Task<ContentAcquisitionResult> AcquireAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return new ContentAcquisitionResult
            {
                Status = ContentAcquisitionStatus.Unsupported,
                ProviderId = "None",
                CanonicalMessage = "Enter a valid HTTP or HTTPS URL.",
                AcquiredAtUtc = DateTime.UtcNow,
                CanUseManualFallback = true
            };
        }

        foreach (var provider in _providers)
        {
            if (provider.CanHandle(uri))
            {
                return await provider.AcquireAsync(
                    new ContentAcquisitionRequest { Url = uri.ToString() },
                    cancellationToken);
            }
        }

        return new ContentAcquisitionResult
        {
            Status = ContentAcquisitionStatus.Unsupported,
            ProviderId = "None",
            CanonicalMessage = "No safe acquisition provider is available for this URL.",
            AcquiredAtUtc = DateTime.UtcNow,
            CanUseManualFallback = true
        };
    }
}
