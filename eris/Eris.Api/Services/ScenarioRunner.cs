using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, RunControl> activeRuns = new();

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

        var control = new RunControl();
        activeRuns[run.Id] = control;

        _ = Task.Run(() => ExecuteRunAsync(run.Id, registrationId, registration.EndpointUrl, request, control));
        return run.Id;
    }

    public bool TryPause(Guid runId)
    {
        if (!activeRuns.TryGetValue(runId, out var control))
        {
            return false;
        }

        control.Pause();
        return true;
    }

    public bool TryResume(Guid runId)
    {
        if (!activeRuns.TryGetValue(runId, out var control))
        {
            return false;
        }

        control.Resume();
        return true;
    }

    public bool TryCancel(Guid runId)
    {
        if (!activeRuns.TryGetValue(runId, out var control))
        {
            return false;
        }

        control.Cancel();
        return true;
    }

    private async Task ExecuteRunAsync(Guid runId, Guid registrationId, string endpointUrl, StartSimulationRequest request, RunControl control)
    {
        var sent = 0;
        var succeeded = 0;
        var failed = 0;
        var cancelled = false;

        try
        {
            var items = await BuildItemsAsync(registrationId, request, CancellationToken.None);
            var semaphore = new SemaphoreSlim(Math.Max(1, request.MaxConcurrency));
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync(control.Token);
                try
                {
                    // Checked after acquiring the slot (not before) so it's re-evaluated on every
                    // send attempt — items.Select's tasks all start almost immediately, so a
                    // pause-check made only once per item, before the semaphore, would be raced
                    // past by nearly everything before a human ever gets to click Pause.
                    await control.WaitIfPausedAsync(control.Token);
                    var ok = await deliveryEngine.SendAsync(
                        registrationId,
                        endpointUrl,
                        item.PayloadJson,
                        request.Scenario,
                        item.IsDuplicate,
                        control.Token);

                    var totalNow = Interlocked.Increment(ref sent);
                    if (ok) Interlocked.Increment(ref succeeded);
                    else Interlocked.Increment(ref failed);

                    await progressPublisher.PublishProgressAsync(runId, BuildProgress(runId, request.Scenario, totalNow, succeeded, failed, true, control.IsPaused));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            logger.LogInformation("Simulation run {RunId} was cancelled after {Sent} sends.", runId, sent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Simulation run {RunId} failed.", runId);
        }
        finally
        {
            activeRuns.TryRemove(runId, out _);
            control.Dispose();
        }

        await MarkRunCompletedAsync(runId, sent, succeeded, failed, CancellationToken.None);
        await progressPublisher.PublishProgressAsync(runId, BuildProgress(runId, request.Scenario, sent, succeeded, failed, false, false));
        await progressPublisher.PublishCompletedAsync(runId, new
        {
            runId,
            scenario = request.Scenario,
            sent,
            succeeded,
            failed,
            failureRate = sent == 0 ? 0 : (double)failed / sent * 100,
            cancelled,
            completedAtUtc = DateTime.UtcNow
        });
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

    private static SimulationProgress BuildProgress(Guid runId, string scenario, int sent, int succeeded, int failed, bool isRunning, bool isPaused)
    {
        return new SimulationProgress
        {
            RunId = runId,
            Scenario = scenario,
            Sent = sent,
            Succeeded = succeeded,
            Failed = failed,
            FailureRate = sent == 0 ? 0 : (double)failed / sent * 100,
            IsRunning = isRunning,
            IsPaused = isPaused
        };
    }

    private sealed record SimulationItem(string PayloadJson, bool IsDuplicate);

    /// <summary>
    /// Per-run pause/resume/cancel state. Pausing swaps in a fresh, uncompleted signal that
    /// every worker awaits before sending its next item; resuming completes it, releasing them
    /// all at once. Cancellation goes through the normal CancellationToken so in-flight HTTP
    /// sends and retry delays (in DeliveryEngine) stop promptly rather than just blocking new work.
    /// </summary>
    private sealed class RunControl : IDisposable
    {
        private readonly CancellationTokenSource cts = new();
        private volatile TaskCompletionSource<bool> resumeSignal = CreateSignal(completed: true);

        public CancellationToken Token => cts.Token;
        public bool IsPaused => !resumeSignal.Task.IsCompleted;

        public void Pause()
        {
            if (resumeSignal.Task.IsCompleted)
            {
                resumeSignal = CreateSignal(completed: false);
            }
        }

        public void Resume() => resumeSignal.TrySetResult(true);

        public void Cancel() => cts.Cancel();

        public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
        {
            var signal = resumeSignal;
            if (signal.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            using var registration = cancellationToken.Register(() => signal.TrySetCanceled());
            await signal.Task;
        }

        public void Dispose() => cts.Dispose();

        private static TaskCompletionSource<bool> CreateSignal(bool completed)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed)
            {
                tcs.SetResult(true);
            }

            return tcs;
        }
    }
}
