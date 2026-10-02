using System.Security.Claims;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Coordinates the existing hardened Seq runtime service with Diagnostics-owned
/// authority, durable operation evidence, health verification, and the staged
/// delivery preference. It never accepts a container ID, image, URL, path, port,
/// API key, or password hash from the browser.
/// </summary>
public sealed class DiagnosticsSeqLifecycleService(
    SeqRuntimeService runtimeService,
    DiagnosticsSeqService diagnosticsSeqService,
    ISeqRuntimeHealthVerifier healthVerifier,
    ISeqDeliveryStateStore deliveryStateStore,
    SeqDiagnosticsOptions options,
    SeqSecretResolver secrets,
    SeqHealthState healthState,
    MemDbContext db,
    IMemDiagnosticEventWriter diagnosticWriter,
    TimeProvider timeProvider,
    SeqEffectiveConfigurationProvider? effectiveConfigurationProvider = null,
    ISeqBootstrapStateStore? bootstrapStateStore = null)
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private static readonly TimeSpan OperationLock = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<DiagnosticsSeqOperationResponse> DeployAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: "seq.deploy",
            hostMutationLevel: "container-create",
            requiresConfirmation: true,
            dataRetained: true,
            action: async ct =>
            {
                var review = await diagnosticsSeqService.ReviewSetupAsync(ct);
                if (!review.ReadyForDeployment)
                {
                    throw new SeqOperationException(
                        "seq_setup_not_ready",
                        StatusCodes.Status409Conflict,
                        "The server-side Seq setup review is not ready for deployment.");
                }

                await runtimeService.DeployAsync(new SeqDeployRequest(), ct);
                await RequireHealthyAsync(ct);
            },
            cancellationToken);

    public Task<DiagnosticsSeqOperationResponse> StartAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: "seq.start",
            hostMutationLevel: "container-start",
            requiresConfirmation: false,
            dataRetained: true,
            action: async ct =>
            {
                await runtimeService.StartAsync(ct);
                await RequireHealthyAsync(ct);
            },
            cancellationToken);

    public Task<DiagnosticsSeqOperationResponse> StopAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: "seq.stop",
            hostMutationLevel: "container-stop",
            requiresConfirmation: true,
            dataRetained: true,
            action: async ct =>
            {
                await runtimeService.StopAsync(ct);
                healthState.RecordStoppedIntentionally(timeProvider.GetUtcNow());
            },
            cancellationToken);

    public Task<DiagnosticsSeqOperationResponse> RestartAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: "seq.restart",
            hostMutationLevel: "container-restart",
            requiresConfirmation: true,
            dataRetained: true,
            action: async ct =>
            {
                await runtimeService.RestartAsync(ct);
                await RequireHealthyAsync(ct);
            },
            cancellationToken);

    public Task<DiagnosticsSeqOperationResponse> RemoveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: "seq.remove",
            hostMutationLevel: "container-remove",
            requiresConfirmation: true,
            dataRetained: true,
            action: async ct =>
            {
                var delivery = deliveryStateStore.GetState();
                if (delivery.EffectiveEnabled || delivery.DesiredEnabled)
                {
                    throw new SeqOperationException(
                        "seq_delivery_must_be_disabled",
                        StatusCodes.Status409Conflict,
                        "Disable Seq event delivery and restart the API before removing the managed runtime.");
                }

                await runtimeService.RemoveAsync(ct);
                healthState.RecordRuntimeAbsent(timeProvider.GetUtcNow());
            },
            cancellationToken);

    public Task<DiagnosticsSeqOperationResponse> ChangeDeliveryAsync(
        ClaimsPrincipal principal,
        bool enabled,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: enabled ? "seq.delivery.enable" : "seq.delivery.disable",
            hostMutationLevel: "configuration",
            requiresConfirmation: true,
            dataRetained: true,
            action: async ct =>
            {
                if (enabled)
                {
                    await EnsureDeliveryCanBeEnabledAsync(ct);
                }

                await deliveryStateStore.SetDesiredAsync(enabled, ct);
            },
            cancellationToken);

    public Task<DiagnosticsSeqOperationResponse> CheckHealthAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            principal,
            operation: "seq.health-check",
            hostMutationLevel: "none",
            requiresConfirmation: false,
            dataRetained: true,
            action: async ct =>
            {
                var status = await runtimeService.GetStatusAsync(ct);
                if (!status.Managed || !status.Running)
                {
                    throw new SeqOperationException(
                        status.WarningCode ?? "seq_runtime_not_running",
                        StatusCodes.Status409Conflict,
                        "A running MEM-managed Seq runtime is required for a health check.");
                }

                await RequireHealthyAsync(ct);
            },
            cancellationToken);

    private async Task EnsureDeliveryCanBeEnabledAsync(CancellationToken cancellationToken)
    {
        var effectiveOptions = effectiveConfigurationProvider?.CreateEffectiveOptions() ?? options;
        var status = await runtimeService.GetStatusAsync(cancellationToken);
        if (!status.Managed || !status.Running)
        {
            throw new SeqOperationException(
                status.WarningCode ?? "seq_runtime_not_running",
                StatusCodes.Status409Conflict,
                "A running MEM-managed Seq runtime is required before event delivery can be enabled.");
        }

        if (!SeqDiagnosticsOptionsValidator.TryNormalizeUrl(effectiveOptions.IngestionUrl, out _))
        {
            throw new SeqOperationException(
                "seq_ingestion_url_invalid",
                StatusCodes.Status409Conflict,
                "The server-side Seq ingestion URL is not configured correctly.");
        }

        var apiKey = secrets.ResolveApiKey(effectiveOptions);
        if (!apiKey.Available)
        {
            throw new SeqOperationException(
                apiKey.WarningCode ?? "seq_api_key_unavailable",
                StatusCodes.Status409Conflict,
                "The Seq ingestion API-key secret is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bootstrapState = bootstrapStateStore?.Read().State;
        if (bootstrapState?.IngestionCredentialState != "available" ||
            bootstrapState.DeliveryVerifiedAtUtc is null)
        {
            throw new SeqOperationException(
                "seq_connection_not_verified",
                StatusCodes.Status409Conflict,
                "Provision and verify the dedicated MEM Seq ingestion credential before enabling event delivery.");
        }

        await RequireHealthyAsync(cancellationToken);
    }

    private async Task RequireHealthyAsync(CancellationToken cancellationToken)
    {
        var verification = await healthVerifier.VerifyAsync(cancellationToken);
        if (!verification.Passed)
        {
            throw new SeqOperationException(
                verification.WarningCode ?? "seq_health_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "The Seq runtime changed state but did not pass the bounded health verification.");
        }
    }

    private async Task<DiagnosticsSeqOperationResponse> ExecuteAsync(
        ClaimsPrincipal principal,
        string operation,
        string hostMutationLevel,
        bool requiresConfirmation,
        bool dataRetained,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!await ProcessGate.WaitAsync(0, cancellationToken))
        {
            throw OperationInProgress();
        }

        RuntimeOperationEntity? operationEntity = null;
        var startedAt = timeProvider.GetUtcNow();
        try
        {
            var now = startedAt.UtcDateTime;
            var active = await db.RuntimeOperations
                .AsNoTracking()
                .AnyAsync(
                    entity => entity.Status == "running" &&
                              entity.Operation.StartsWith("seq.") &&
                              (entity.LockedUntilUtc == null || entity.LockedUntilUtc > now),
                    cancellationToken);
            if (active)
            {
                throw OperationInProgress();
            }

            operationEntity = new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = null,
                RestoreAttemptId = null,
                Operation = operation,
                Status = "running",
                IdempotencyKey = null,
                RequestedBy = ResolveOperatorName(principal),
                RequestedByUserId = ResolveOperatorId(principal),
                RequestedAtUtc = startedAt.UtcDateTime,
                StartedAtUtc = startedAt.UtcDateTime,
                CurrentStep = "started",
                AttemptCount = 1,
                LockedUntilUtc = startedAt.Add(OperationLock).UtcDateTime,
                InputJson = JsonSerializer.Serialize(
                    new { operation },
                    JsonOptions),
                HostMutationLevel = hostMutationLevel,
                RequiresConfirmation = requiresConfirmation,
                ConfirmedAtUtc = requiresConfirmation ? startedAt.UtcDateTime : null
            };
            db.RuntimeOperations.Add(operationEntity);
            await db.SaveChangesAsync(cancellationToken);
            await WriteLifecycleEventAsync(
                operationEntity,
                MemDiagnosticSeverities.Information,
                "diagnostics.seq_operation_started",
                "A reviewed Seq operation started.",
                createIncident: false,
                warningCode: null);

            operationEntity.CurrentStep = "executing";
            operationEntity.LockedUntilUtc = timeProvider.GetUtcNow()
                .Add(OperationLock)
                .UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);

            await action(cancellationToken);

            var completedAt = timeProvider.GetUtcNow();
            var overview = await diagnosticsSeqService.GetOverviewAsync(
                principal,
                cancellationToken);
            operationEntity.Status = "succeeded";
            operationEntity.CurrentStep = "completed";
            operationEntity.CompletedAtUtc = completedAt.UtcDateTime;
            operationEntity.LockedUntilUtc = null;
            operationEntity.ResultJson = JsonSerializer.Serialize(
                new
                {
                    status = "succeeded",
                    runtimeState = overview.Runtime.State,
                    deliveryEnabled = overview.Delivery.Enabled,
                    desiredDeliveryEnabled = overview.Delivery.DesiredEnabled,
                    restartRequired = overview.Delivery.RestartRequired,
                    dataRetained
                },
                JsonOptions);
            operationEntity.EvidenceJson = JsonSerializer.Serialize(
                new
                {
                    healthStatus = overview.Health.Status,
                    reachable = overview.Health.Reachable,
                    warningCodes = overview.Warnings
                },
                JsonOptions);
            operationEntity.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            await WriteLifecycleEventAsync(
                operationEntity,
                MemDiagnosticSeverities.Information,
                "diagnostics.seq_operation_completed",
                "A reviewed Seq operation completed.",
                createIncident: false,
                warningCode: null);

            return new DiagnosticsSeqOperationResponse(
                SchemaVersion: 1,
                OperationId: operationEntity.Id,
                Operation: operation,
                Status: "succeeded",
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                DataRetained: dataRetained,
                Overview: overview,
                Warnings: overview.Warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (operationEntity is not null)
            {
                await MarkFailedAsync(
                    operationEntity,
                    "seq_operation_cancelled",
                    "The Seq operation was cancelled.");
            }

            throw;
        }
        catch (SeqOperationException exception)
        {
            if (operationEntity is not null)
            {
                await MarkFailedAsync(operationEntity, exception.Code, exception.Message);
            }

            throw;
        }
        catch (Exception)
        {
            if (operationEntity is not null)
            {
                await MarkFailedAsync(
                    operationEntity,
                    "seq_operation_failed",
                    "The Seq operation failed unexpectedly.");
            }

            throw new SeqOperationException(
                "seq_operation_failed",
                StatusCodes.Status503ServiceUnavailable,
                "The Seq operation failed unexpectedly. Use Diagnostics for the recorded incident and safe evidence.");
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    private async Task MarkFailedAsync(
        RuntimeOperationEntity entity,
        string warningCode,
        string safeMessage)
    {
        var completedAt = timeProvider.GetUtcNow();
        entity.Status = "failed";
        entity.CurrentStep = "failed";
        entity.CompletedAtUtc = completedAt.UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = warningCode;
        entity.ResultJson = JsonSerializer.Serialize(
            new { status = "failed", warningCode },
            JsonOptions);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // The original operation failure remains authoritative.
        }

        await WriteLifecycleEventAsync(
            entity,
            MemDiagnosticSeverities.Error,
            "diagnostics.seq_operation_failed",
            safeMessage,
            createIncident: true,
            warningCode: warningCode);
    }

    private Task<MemDiagnosticWriteResult?> WriteLifecycleEventAsync(
        RuntimeOperationEntity entity,
        string severity,
        string eventCode,
        string message,
        bool createIncident,
        string? warningCode) =>
        diagnosticWriter.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: severity,
                EventCode: eventCode,
                Source: nameof(DiagnosticsSeqLifecycleService),
                Feature: "logging",
                Message: message,
                Stage: entity.Operation,
                CreateIncident: createIncident,
                OperationId: entity.Id,
                Resource: new MemDiagnosticResource(
                    Kind: "managed-service",
                    Id: "seq",
                    DisplayName: "Seq"),
                Details: warningCode is null
                    ? new Dictionary<string, string?>
                    {
                        ["operation"] = entity.Operation,
                        ["status"] = entity.Status
                    }
                    : new Dictionary<string, string?>
                    {
                        ["operation"] = entity.Operation,
                        ["status"] = entity.Status,
                        ["warningCode"] = warningCode
                    },
                SuggestedAction: createIncident
                    ? "Review the Seq workspace and recorded Diagnostics evidence before retrying."
                    : null,
                Retryable: createIncident));

    private static SeqOperationException OperationInProgress() => new(
        "seq_operation_in_progress",
        StatusCodes.Status409Conflict,
        "Another Seq operation is already running. Wait for it to finish before starting another action.");

    private static string ResolveOperatorName(ClaimsPrincipal principal) =>
        principal.Identity?.Name?.Trim() is { Length: > 0 } name
            ? name
            : "mem-operator";

    private static Guid? ResolveOperatorId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var parsed)
            ? parsed
            : null;
}
