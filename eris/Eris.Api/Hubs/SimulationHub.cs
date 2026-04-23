using Microsoft.AspNetCore.SignalR;

namespace Eris.Api.Hubs;

public class SimulationHub : Hub
{
    public Task JoinSimulation(string runId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, runId);
    }
}
