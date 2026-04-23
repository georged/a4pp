using System.Text.Json;
using System.Threading;
using Bogus;
using Eris.Api.Models;

namespace Eris.Api.Services;

public class EventGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Faker _faker = new();
    private readonly object _sync = new();
    private long _sequence;

    private string NextEventId() => $"evt-{Interlocked.Increment(ref _sequence):D8}";
    private string NextRegistrationId() => $"reg-{Interlocked.Increment(ref _sequence):D8}";

    public string GenerateValidJson()
    {
        lock (_sync)
        {
            var quantity = _faker.Random.Int(1, 5);
            var unitAmount = _faker.Random.Decimal(49, 399);

            var payload = new RegistrationEvent
            {
                EventId = NextEventId(),
                EventType = "registration.created",
                EventTime = DateTimeOffset.UtcNow,
                SourceSystem = "contoso-web",
                RegistrationId = NextRegistrationId(),
                Customer = new CustomerInfo
                {
                    FirstName = _faker.Name.FirstName(),
                    LastName = _faker.Name.LastName(),
                    Email = _faker.Internet.Email()
                },
                CampaignCode = _faker.PickRandom("BLACKFRIDAY", "SPRING", "SUMMER", "VIP2026"),
                TicketType = _faker.PickRandom("General", "VIP", "EarlyBird"),
                Quantity = quantity,
                TotalAmount = Math.Round(unitAmount * quantity, 2)
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }
    }

    public string GenerateInvalidJson()
    {
        lock (_sync)
        {
            var invalidType = _faker.Random.Int(1, 3);

            return invalidType switch
            {
                1 => JsonSerializer.Serialize(new
                {
                    eventId = NextEventId(),
                    eventType = "registration.created",
                    eventTime = DateTimeOffset.UtcNow,
                    sourceSystem = "contoso-web",
                    registrationId = (string?)null,
                    customer = new
                    {
                        firstName = _faker.Name.FirstName(),
                        lastName = _faker.Name.LastName(),
                        email = _faker.Internet.Email()
                    },
                    campaignCode = "BLACKFRIDAY",
                    ticketType = "VIP",
                    quantity = 2,
                    totalAmount = 249.90
                }, JsonOptions),
                2 => JsonSerializer.Serialize(new
                {
                    eventId = NextEventId(),
                    eventType = "registration.created",
                    eventTime = DateTimeOffset.UtcNow,
                    sourceSystem = "contoso-web",
                    registrationId = NextRegistrationId(),
                    customer = new
                    {
                        firstName = _faker.Name.FirstName(),
                        lastName = _faker.Name.LastName(),
                        email = (string?)null
                    },
                    campaignCode = "BLACKFRIDAY",
                    ticketType = "VIP",
                    quantity = 2,
                    totalAmount = 249.90
                }, JsonOptions),
                _ => JsonSerializer.Serialize(new
                {
                    eventId = NextEventId(),
                    eventType = "registration.created",
                    eventTime = DateTimeOffset.UtcNow,
                    sourceSystem = "contoso-web",
                    registrationId = NextRegistrationId(),
                    customer = new
                    {
                        firstName = _faker.Name.FirstName(),
                        lastName = _faker.Name.LastName(),
                        email = _faker.Internet.Email()
                    },
                    campaignCode = "BLACKFRIDAY",
                    ticketType = "VIP",
                    quantity = 2,
                    totalAmount = "invalid-number"
                }, JsonOptions)
            };
        }
    }
}
