using System.Security.Claims;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;
using Shared.ControlPlane.Runtime;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Verifies the currently running API process after staged Seq delivery has
/// become effective. The verification event is emitted through the normal
/// ILogger/Serilog pipeline, not posted directly to Seq, so the operator can
/// search the returned identifier in Seq as proof of live delivery.
/// </summary>
public sealed class DiagnosticsSeqDeliveryVerificationService(
    ISeqDeliveryStateStore deliveryStateStore,
    SeqEffectiveConfigurationProvider effectiveConfigurationProvider,
    SeqSecretResolver secrets,
    ISeqRuntimeStatusReader runtimeStatusReader,
    ISeqRuntimeHealthVerifier healthVerifier,
    ISeqBootstrapStateStore bootstrapStateStore,
    SeqDeliveryProcessIdentity processIdentity,
    SeqLoggingRuntimeState loggingRuntimeState,
    DiagnosticsSeqService diagnosticsSeqService,
    MemDbContext db,
    IMemDiagnosticEventWriter diagnosticWriter,
    MemControlPlaneRuntimeContext runtimeContext,
    TimeProvider timeProvider,
    ILogger<DiagnosticsSeqDeliveryVerificationService> logger)
{
    public const string VerificationEventCode =
        "diagnostics.seq_active_delivery_verification";

    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private static readonly TimeSpan OperationLock = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<DiagnosticsSeqDeliveryVerificationResponse> VerifyAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!await ProcessGate.WaitAsync(0, cancellationToken))
        {
            throw new SeqOperationException(
                "seq_operation_in_progress",
                StatusCodes.Status409Conflict,
                "Another Seq operation is already running. Wait for it to finish before verifying active delivery.");
        }

        RuntimeOperationEntity? entity = null;
        try
        {
            var startedAt = timeProvider.GetUtcNow();
            var active = await db.RuntimeOperations
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.Status == "running" &&
                                 candidate.Operation.StartsWith("seq.") &&
                                 (candidate.LockedUntilUtc == null ||
                                  candidate.LockedUntilUtc > startedAt.UtcDateTime),
                    cancellationToken);
            if (active)
            {
                throw new SeqOperationException(
                    "seq_operation_in_progress",
                    StatusCodes.Status409Conflict,
                    "Another Seq operation is already running. Wait for it to finish before verifying active delivery.");
            }

            entity = new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                Operation = "seq.delivery.verify",
                Status = "running",
                RequestedBy = ResolveOperatorName(principal),
                RequestedByUserId = ResolveOperatorId(principal),
                RequestedAtUtc = startedAt.UtcDateTime,
                StartedAtUtc = startedAt.UtcDateTime,
                CurrentStep = "verifying-active-delivery",
                AttemptCount = 1,
                LockedUntilUtc = startedAt.Add(OperationLock).UtcDateTime,
                InputJson = JsonSerializer.Serialize(
                    new { operation = "seq.delivery.verify" },
                    JsonOptions),
                HostMutationLevel = "none",
                RequiresConfirmation = false
            };
            db.RuntimeOperations.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            var delivery = deliveryStateStore.GetState();
            var effectiveOptions = effectiveConfigurationProvider.CreateEffectiveOptions();
            if (delivery.RestartRequired)
            {
                throw new SeqOperationException(
                    "seq_delivery_restart_required",
                    StatusCodes.Status409Conflict,
                    "Restart the MEM API before verifying the staged Seq delivery state.");
            }

            if (!delivery.EffectiveEnabled ||
                !delivery.DesiredEnabled ||
                !effectiveOptions.SinkEnabled)
            {
                throw new SeqOperationException(
                    "seq_delivery_not_enabled",
                    StatusCodes.Status409Conflict,
                    "Seq delivery is not effective in the current MEM API process.");
            }

            if (!loggingRuntimeState.SinkConfigured)
            {
                throw new SeqOperationException(
                    loggingRuntimeState.WarningCode ??
                        "diagnostics.seq_sink_configuration_failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "The current MEM API process did not attach the Seq logging sink at startup.");
            }

            var apiKey = secrets.ResolveApiKey(effectiveOptions);
            if (!apiKey.Available)
            {
                throw new SeqOperationException(
                    apiKey.WarningCode ?? "seq_api_key_unavailable",
                    StatusCodes.Status409Conflict,
                    "The Seq ingestion credential is unavailable to the current MEM API process.");
            }

            var runtime = await runtimeStatusReader.GetStatusAsync(cancellationToken);
            if (!runtime.Managed || !runtime.Running || !runtime.UsesApprovedRuntime)
            {
                throw new SeqOperationException(
                    runtime.WarningCode ?? "seq_runtime_not_ready_for_delivery",
                    StatusCodes.Status409Conflict,
                    "A running MEM-managed Seq runtime using the approved image is required for active delivery verification.");
            }

            var stateRead = bootstrapStateStore.Read();
            var state = stateRead.State;
            if (state is null ||
                state.IngestionCredentialState != "available" ||
                state.DeliveryVerifiedAtUtc is null)
            {
                throw new SeqOperationException(
                    stateRead.WarningCode ?? "seq_connection_not_verified",
                    StatusCodes.Status409Conflict,
                    "The dedicated MEM Seq ingestion connection has not been verified.");
            }

            var health = await healthVerifier.VerifyAsync(cancellationToken);
            if (!health.Passed)
            {
                throw new SeqOperationException(
                    health.WarningCode ?? "seq_health_verification_failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "Seq did not pass the bounded health check required for active delivery verification.");
            }

            if (!logger.IsEnabled(LogLevel.Information))
            {
                throw new SeqOperationException(
                    "seq_delivery_information_logging_disabled",
                    StatusCodes.Status409Conflict,
                    "Information-level logging is disabled in the current MEM API process, so an active Seq delivery verification event cannot be emitted.");
            }

            var verificationId = $"seq-active-{Guid.NewGuid():N}";
            var emittedAt = timeProvider.GetUtcNow();

            logger.LogInformation(
                "MEM active Seq delivery verification event. VerificationId={VerificationId} EventCode={EventCode} Feature={Feature} Source={Source} RuntimeMode={RuntimeMode} ControlPlaneInstanceId={ControlPlaneInstanceId} ApiProcessInstanceId={ApiProcessInstanceId} Environment={Environment} Version={Version} Commit={Commit}",
                verificationId,
                VerificationEventCode,
                "diagnostics",
                "MEM Control Plane",
                runtimeContext.RuntimeMode,
                runtimeContext.ControlPlaneInstanceId,
                runtimeContext.ApiProcessInstanceId,
                runtimeContext.EnvironmentName,
                runtimeContext.Version,
                runtimeContext.Commit);

            await bootstrapStateStore.WriteAsync(
                state with
                {
                    SetupStage = "delivery-active",
                    ActiveDeliveryProcessId = processIdentity.Value,
                    ActiveDeliveryVerifiedAtUtc = emittedAt,
                    LastActiveDeliveryVerificationId = verificationId,
                    LastOperationId = entity.Id
                },
                cancellationToken);

            entity.Status = "succeeded";
            entity.CurrentStep = "completed";
            entity.CompletedAtUtc = emittedAt.UtcDateTime;
            entity.LockedUntilUtc = null;
            entity.LastError = null;
            entity.ResultJson = JsonSerializer.Serialize(
                new
                {
                    status = "succeeded",
                    verificationId,
                    emittedAtUtc = emittedAt,
                    effectiveDeliveryEnabled = true,
                    desiredDeliveryEnabled = true,
                    restartRequired = false
                },
                JsonOptions);
            entity.EvidenceJson = JsonSerializer.Serialize(
                new
                {
                    verificationState = "emitted-through-active-logger",
                    verificationId,
                    apiProcessInstanceId = runtimeContext.ApiProcessInstanceId,
                    controlPlaneInstanceId = runtimeContext.ControlPlaneInstanceId,
                    runtimeMode = runtimeContext.RuntimeMode,
                    environment = runtimeContext.EnvironmentName,
                    version = runtimeContext.Version,
                    commit = runtimeContext.Commit
                },
                JsonOptions);
            await db.SaveChangesAsync(cancellationToken);

            await diagnosticWriter.TryWriteWorkflowEventAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Information,
                    EventCode: "diagnostics.seq_delivery_activation_verified",
                    Source: nameof(DiagnosticsSeqDeliveryVerificationService),
                    Feature: "logging",
                    Message: "The current MEM API process emitted an active Seq delivery verification event.",
                    Stage: entity.Operation,
                    CreateIncident: false,
                    OperationId: entity.Id,
                    Resource: new MemDiagnosticResource(
                        Kind: "managed-service",
                        Id: "seq",
                        DisplayName: "Seq"),
                    Details: new Dictionary<string, string?>
                    {
                        ["verificationId"] = verificationId,
                        ["deliveryState"] = "active",
                        ["runtimeMode"] = runtimeContext.RuntimeMode,
                        ["controlPlaneInstanceId"] = runtimeContext.ControlPlaneInstanceId.ToString("D"),
                        ["apiProcessInstanceId"] = runtimeContext.ApiProcessInstanceId.ToString("D"),
                        ["environment"] = runtimeContext.EnvironmentName,
                        ["version"] = runtimeContext.Version,
                        ["commit"] = runtimeContext.Commit
                    }));

            var overview = await diagnosticsSeqService.GetOverviewAsync(
                principal,
                cancellationToken);
            return new DiagnosticsSeqDeliveryVerificationResponse(
                SchemaVersion: 1,
                OperationId: entity.Id,
                Status: "succeeded",
                VerificationId: verificationId,
                EmittedAtUtc: emittedAt,
                Overview: overview);
        }
        catch (SeqOperationException exception)
        {
            if (entity is not null)
            {
                await MarkFailedAsync(entity, exception.Code);
            }

            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (entity is not null)
            {
                await MarkFailedAsync(entity, "seq_delivery_verification_cancelled");
            }

            throw;
        }
        catch (Exception)
        {
            if (entity is not null)
            {
                await MarkFailedAsync(entity, "seq_delivery_verification_failed");
            }

            throw new SeqOperationException(
                "seq_delivery_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "MEM could not verify active Seq delivery. MEM-native Diagnostics remain available.");
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    private async Task MarkFailedAsync(
        RuntimeOperationEntity entity,
        string warningCode)
    {
        entity.Status = "failed";
        entity.CurrentStep = "failed";
        entity.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = warningCode;
        entity.ResultJson = JsonSerializer.Serialize(
            new { status = "failed", warningCode },
            JsonOptions);
        entity.EvidenceJson = JsonSerializer.Serialize(
            new
            {
                verificationState = "failed",
                warningCode,
                apiProcessInstanceId = runtimeContext.ApiProcessInstanceId,
                controlPlaneInstanceId = runtimeContext.ControlPlaneInstanceId,
                runtimeMode = runtimeContext.RuntimeMode,
                environment = runtimeContext.EnvironmentName,
                version = runtimeContext.Version,
                commit = runtimeContext.Commit
            },
            JsonOptions);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // The original safe failure remains authoritative.
        }
    }

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
