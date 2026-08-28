using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;
using SOCYVIA.Repositories;

namespace SOCYVIA.Services.Telemetry;

public sealed class SqliteTelemetrySink : ITelemetrySink
{
    public async Task WriteBatchAsync(
        IReadOnlyList<InteractionEvent> events,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await InteractionEventRepository.CreateBatchAsync(events);
    }
}
