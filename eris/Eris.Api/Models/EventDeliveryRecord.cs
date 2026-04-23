namespace Eris.Api.Models;

public class EventDeliveryRecord
{
    public Guid Id { get; set; }
    public Guid RegistrationEndpointId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public int? StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public bool IsDuplicate { get; set; }
    public long LatencyMs { get; set; }
    public string? Error { get; set; }
}
