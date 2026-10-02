using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsSeqBootstrapWorker(
    SeqBootstrapOperationQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DiagnosticsSeqBootstrapWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MarkInterruptedOperationsAsync(stoppingToken);

        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            using (workItem)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider
                        .GetRequiredService<DiagnosticsSeqBootstrapOperationProcessor>();
                    await processor.ProcessAsync(workItem, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "The queued Seq bootstrap operation {OperationId} failed outside its normal safe failure boundary.",
                        workItem.OperationId);
                }
            }
        }
    }

    private async Task MarkInterruptedOperationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            var interrupted = await db.RuntimeOperations
                .Where(entity =>
                    entity.Operation == "seq.bootstrap" &&
                    (entity.Status == "queued" || entity.Status == "running"))
                .ToListAsync(cancellationToken);
            if (interrupted.Count == 0)
            {
                return;
            }

            var completedAt = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var entity in interrupted)
            {
                entity.Status = "failed";
                entity.CurrentStep = "failed";
                entity.CompletedAtUtc = completedAt;
                entity.LockedUntilUtc = null;
                entity.LastError = "seq_bootstrap_interrupted";
                entity.ResultJson = JsonSerializer.Serialize(
                    new
                    {
                        status = "failed",
                        failedStep = "process-restart",
                        warningCode = entity.LastError
                    },
                    JsonOptions);
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Marked {OperationCount} interrupted Seq bootstrap operations as failed because one-time password input cannot be resumed after an API process restart.",
                interrupted.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "MEM could not reconcile interrupted Seq bootstrap operations during worker startup.");
        }
    }
}
