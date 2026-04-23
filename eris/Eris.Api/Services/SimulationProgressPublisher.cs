using Eris.Api.Hubs;
using Eris.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Eris.Api.Services;

public class SimulationProgressPublisher(IHubContext<SimulationHub> hubContext)
{
    public Task PublishProgressAsync(Guid runId, SimulationProgress progress, CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Group(runId.ToString()).SendAsync("progress", progress, cancellationToken);
    }

    public Task PublishCompletedAsync(Guid runId, object summary, CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Group(runId.ToString()).SendAsync("completed", summary, cancellationToken);
    }
}
