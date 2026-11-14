using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace RegistrationFunctions;

public class ProcessRegistration(IOrganizationServiceAsync2 dataverse, ILogger<ProcessRegistration> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Takes up to 20 messages at a time (maxMessageBatchSize in host.json)
    // and writes them to Dataverse in a single round trip.
    [Function("processRegistration")]
    public async Task Run(
        [ServiceBusTrigger(Queues.Registrations, Connection = Queues.Connection, IsBatched = true)]
        ServiceBusReceivedMessage[] messages,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing a batch of {Count} registrations", messages.Length);

        var batch = new ExecuteMultipleRequest
        {
            // One bad registration fails alone; the rest of the batch still goes through.
            // ReturnResponses = false means the response lists only the failures.
            Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = false },
            Requests = new OrganizationRequestCollection()
        };

        foreach (var message in messages)
        {
            var registration = message.Body.ToObjectFromJson<Registration>(Json)
                ?? throw new InvalidOperationException($"Message {message.MessageId} has an empty body.");

            batch.Requests.Add(new CreateRequest { Target = ToContact(registration) });
        }

        var response = (ExecuteMultipleResponse)await dataverse.ExecuteAsync(batch, cancellationToken);

        foreach (var failure in response.Responses.Where(r => r.Fault is not null))
        {
            logger.LogError("Registration at position {Index} failed: {Fault}",
                failure.RequestIndex, failure.Fault.Message);
        }

        logger.LogInformation("Batch of {Count} registrations completed with {Failures} failures",
            messages.Length, response.Responses.Count);
    }

    // Deep insert: the contact and its registration are created together in one request.
    private static Entity ToContact(Registration registration)
    {
        var contact = new Entity("contact")
        {
            ["firstname"] = registration.Customer.FirstName,
            ["lastname"] = registration.Customer.LastName,
            ["emailaddress1"] = registration.Customer.Email
        };

        var row = new Entity("tot_registration")
        {
            ["tot_externalid"] = registration.RegistrationId,
            ["tot_campaigncode"] = registration.CampaignCode,
            ["tot_eventid"] = registration.EventId,
            ["tot_quantity"] = registration.Quantity,
            ["tot_sourcesystem"] = registration.SourceSystem,
            ["tot_submittedon"] = registration.EventTime.UtcDateTime,
            ["tot_tickettype"] = registration.TicketType,
            ["tot_totalamount"] = new Money(registration.TotalAmount)
        };

        contact.RelatedEntities[new Relationship("tot_registration_contact")] =
            new EntityCollection(new List<Entity> { row });

        return contact;
    }
}
