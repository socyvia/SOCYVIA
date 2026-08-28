using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SOCYVIA.Models;

namespace SOCYVIA.Services.Telemetry;

public interface ITelemetrySink
{
    Task WriteBatchAsync(
        IReadOnlyList<InteractionEvent> events,
        CancellationToken cancellationToken = default);
}
