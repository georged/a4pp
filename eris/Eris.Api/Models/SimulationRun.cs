namespace Eris.Api.Models;

public class SimulationRun
{
    public Guid Id { get; set; }
    public Guid RegistrationEndpointId { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public bool IsRunning { get; set; }
}
