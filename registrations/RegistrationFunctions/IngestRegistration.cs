using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace RegistrationFunctions;

public class IngestRegistration(ILogger<IngestRegistration> logger)
{
    // POST /api/ingestRegistration: accept the registration, queue it, answer straight away.
    [Function("ingestRegistration")]
    public async Task<IngestResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            return new IngestResult
            {
                Response = new BadRequestObjectResult("Request body is required.")
            };
        }

        logger.LogInformation("HTTP request received. Queuing body.");

        return new IngestResult
        {
            Message = body,
            Response = new ObjectResult("Message queued.") { StatusCode = StatusCodes.Status202Accepted }
        };
    }
}

// One function, two outputs: the HTTP response and the queue message.
// A null Message sends nothing to the queue.
public class IngestResult
{
    [HttpResult]
    public required IActionResult Response { get; init; }

    [ServiceBusOutput(Queues.Registrations, Connection = Queues.Connection)]
    public string? Message { get; init; }
}
