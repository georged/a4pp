namespace Eris.Api.Models;

public class RegistrationEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = "registration.created";
    public DateTimeOffset EventTime { get; set; } = DateTimeOffset.UtcNow;
    public string SourceSystem { get; set; } = "contoso-web";
    public string RegistrationId { get; set; } = string.Empty;
    public CustomerInfo Customer { get; set; } = new();
    public string CampaignCode { get; set; } = string.Empty;
    public string TicketType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
}

public class CustomerInfo
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
