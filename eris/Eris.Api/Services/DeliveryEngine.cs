using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Eris.Api.Data;
using Eris.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eris.Api.Services;

public class DeliveryEngine(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<ErisDbContext> dbContextFactory,
    ILogger<DeliveryEngine> logger)
{
    public async Task<bool> SendAsync(
        Guid registrationId,
        string endpointUrl,
        string payloadJson,
        string scenario,
        bool isDuplicate,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        bool isSuccess = false;
        string? error = null;

        try
        {
            var client = httpClientFactory.CreateClient("delivery");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            statusCode = (int)response.StatusCode;
            isSuccess = response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogWarning(ex, "Delivery failed for registration {RegistrationId}", registrationId);
        }

        sw.Stop();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.EventRecords.Add(new EventDeliveryRecord
        {
            Id = Guid.NewGuid(),
            RegistrationEndpointId = registrationId,
            PayloadJson = payloadJson,
            SentAtUtc = DateTime.UtcNow,
            StatusCode = statusCode,
            IsSuccess = isSuccess,
            Scenario = scenario,
            IsDuplicate = isDuplicate,
            LatencyMs = sw.ElapsedMilliseconds,
            Error = error
        });
        await db.SaveChangesAsync(cancellationToken);

        return isSuccess;
    }
}
