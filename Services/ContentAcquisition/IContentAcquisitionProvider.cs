using System;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services.ContentAcquisition;

public interface IContentAcquisitionProvider
{
    string ProviderId { get; }
    bool CanHandle(Uri uri);
    Task<ContentAcquisitionResult> AcquireAsync(
        ContentAcquisitionRequest request,
        CancellationToken cancellationToken = default);
}
