namespace Eris.Api.Models;

public class CreateRegistrationRequest
{
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
}

public class ResolveRegistrationRequest
{
    public string EndpointUrl { get; set; } = string.Empty;
}

public class StartSimulationRequest
{
    public string Scenario { get; set; } = "mixed";
    public int Count { get; set; } = 1;
    public int MaxConcurrency { get; set; } = 10;
    public int ValidCount { get; set; } = 10;
    public int DuplicateCount { get; set; } = 0;
}

public class SimulationProgress
{
    public Guid RunId { get; set; }
    public int Sent { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public double FailureRate { get; set; }
    public bool IsRunning { get; set; }
    public string Scenario { get; set; } = string.Empty;
}

public class AdminRegistrationView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public double FailureRate { get; set; }
}

public class AdminRunSummary
{
    public Guid RunId { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public double FailureRate { get; set; }
    public bool IsRunning { get; set; }
}

public class AdminRegistrationDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public double FailureRate { get; set; }
    public List<AdminRunSummary> Runs { get; set; } = [];
}

public class BulkDeleteWebhooksRequest
{
    public List<Guid> RegistrationIds { get; set; } = [];
}

public class QueueMetricsRequest
{
    public string ConnectionString { get; set; } = string.Empty;
    public string? QueueName { get; set; }
    public int PlannedAdditionalMessages { get; set; }
}

public class QueueMetricsResponse
{
    public string QueueName { get; set; } = string.Empty;
    public long ActiveMessageCount { get; set; }
    public long PlannedAdditionalMessages { get; set; }
    public long ScaleMax { get; set; }
}
