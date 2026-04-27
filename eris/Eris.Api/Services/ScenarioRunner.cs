using Eris.Api.Data;
using Eris.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eris.Api.Services;

public class ScenarioRunner(
    IDbContextFactory<ErisDbContext> dbContextFactory,
    EventGenerator eventGenerator,
    DeliveryEngine deliveryEngine,
    SimulationProgressPublisher progressPublisher,
    ILogger<ScenarioRunner> logger)
{
    public async Task<Guid> StartRunAsync(Guid registrationId, StartSimulationRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var registration = await db.Registrations.FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            throw new InvalidOperationException("Registration not found.");
        }

        var run = new SimulationRun
        {
            Id = Guid.NewGuid(),
            RegistrationEndpointId = registrationId,
            Scenario = request.Scenario,
            StartedAtUtc = DateTime.UtcNow,
            IsRunning = true
        };

        db.SimulationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => ExecuteRunAsync(run.Id, registrationId, registration.EndpointUrl, request));
        return run.Id;
    }

    private async Task ExecuteRunAsync(Guid runId, Guid registrationId, string endpointUrl, StartSimulationRequest request)
    {
        var sent = 0;
        var succeeded = 0;
        var failed = 0;

        try
        {
            var items = await BuildItemsAsync(registrationId, request, CancellationToken.None);
            var semaphore = new SemaphoreSlim(Math.Max(1, request.MaxConcurrency));
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var ok = await deliveryEngine.SendAsync(
                        registrationId,
                        endpointUrl,
                        item.PayloadJson,
                        request.Scenario,
                        item.IsDuplicate,
                        CancellationToken.None);

                    var totalNow = Interlocked.Increment(ref sent);
                    if (ok) Interlocked.Increment(ref succeeded);
                    else Interlocked.Increment(ref failed);

                    await progressPublisher.PublishProgressAsync(runId, BuildProgress(runId, request.Scenario, totalNow, succeeded, failed, true));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            await MarkRunCompletedAsync(runId, sent, succeeded, failed, CancellationToken.None);
            await progressPublisher.PublishProgressAsync(runId, BuildProgress(runId, request.Scenario, sent, succeeded, failed, false));
            await progressPublisher.PublishCompletedAsync(runId, new
            {
                runId,
                scenario = request.Scenario,
                sent,
                succeeded,
                failed,
                failureRate = sent == 0 ? 0 : (double)failed / sent * 100,
                completedAtUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Simulation run {RunId} failed.", runId);
            await MarkRunCompletedAsync(runId, sent, succeeded, failed, CancellationToken.None);
            await progressPublisher.PublishProgressAsync(runId, BuildProgress(runId, request.Scenario, sent, succeeded, failed, false));
        }
    }

    private async Task<List<SimulationItem>> BuildItemsAsync(Guid registrationId, StartSimulationRequest request, CancellationToken cancellationToken)
    {
        var items = new List<SimulationItem>();
        var scenario = request.Scenario.ToLowerInvariant();

        if (scenario == "single-valid")
        {
            items.Add(new SimulationItem(eventGenerator.GenerateValidJson(), false));
            return items;
        }

        if (scenario == "burst")
        {
            for (var i = 0; i < Math.Max(1, request.Count); i++)
            {
                items.Add(new SimulationItem(eventGenerator.GenerateValidJson(), false));
            }

            return items;
        }

        if (scenario == "duplicates")
        {
            var duplicatePayload = eventGenerator.GenerateValidJson();
            for (var i = 0; i < Math.Max(1, request.Count); i++)
            {
                items.Add(new SimulationItem(duplicatePayload, true));
            }

            return items;
        }

        if (scenario == "mixed")
        {
            for (var i = 0; i < Math.Max(0, request.ValidCount); i++)
            {
                items.Add(new SimulationItem(eventGenerator.GenerateValidJson(), false));
            }

            var duplicatePayload = eventGenerator.GenerateValidJson();
            for (var i = 0; i < Math.Max(0, request.DuplicateCount); i++)
            {
                items.Add(new SimulationItem(duplicatePayload, true));
            }

            return items.OrderBy(_ => Guid.NewGuid()).ToList();
        }

        if (scenario == "replay-last-200")
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var previous = await db.EventRecords
                .Where(x => x.RegistrationEndpointId == registrationId)
                .OrderByDescending(x => x.SentAtUtc)
                .Take(200)
                .Select(x => x.PayloadJson)
                .ToListAsync(cancellationToken);

            foreach (var payload in previous)
            {
                items.Add(new SimulationItem(payload, false));
            }

            return items;
        }

        throw new InvalidOperationException($"Unknown scenario '{request.Scenario}'.");
    }

    private async Task MarkRunCompletedAsync(Guid runId, int sent, int succeeded, int failed, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.SimulationRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Total = sent;
        run.Succeeded = succeeded;
        run.Failed = failed;
        run.IsRunning = false;
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SimulationProgress BuildProgress(Guid runId, string scenario, int sent, int succeeded, int failed, bool isRunning)
    {
        return new SimulationProgress
        {
            RunId = runId,
            Scenario = scenario,
            Sent = sent,
            Succeeded = succeeded,
            Failed = failed,
            FailureRate = sent == 0 ? 0 : (double)failed / sent * 100,
            IsRunning = isRunning
        };
    }

    private sealed record SimulationItem(string PayloadJson, bool IsDuplicate);
}
