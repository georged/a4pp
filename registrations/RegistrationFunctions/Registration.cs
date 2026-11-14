namespace RegistrationFunctions;

// The payload the website (Eris) posts. See sample-input.json at the repo root.
public record Registration(
    string EventId,
    string EventType,
    DateTimeOffset EventTime,
    string SourceSystem,
    string RegistrationId,
    Customer Customer,
    string CampaignCode,
    string TicketType,
    int Quantity,
    decimal TotalAmount);

public record Customer(string FirstName, string LastName, string Email);
