using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsSeqBootstrapOperationProcessor(
    MemDbContext db,
    SeqRuntimeImageResolver imageResolver,
    SeqPasswordHashService passwordHashService,
    SeqSecretResolver secretResolver,
    SeqSecretFileWriter secretWriter,
    SeqDiagnosticsOptions options,
    SeqBootstrapStorageInspector storageInspector,
    ISeqBootstrapStateStore stateStore,
    SeqRuntimeService runtimeService,
    ISeqRuntimeHealthVerifier healthVerifier,
    ISeqConnectionProvisioner connectionProvisioner,
    ISeqDeliveryStateStore deliveryStateStore,
    IMemDiagnosticEventWriter diagnosticWriter,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan OperationLock = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(
        SeqBootstrapWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var entity = await db.RuntimeOperations.SingleAsync(
            item => item.Id == workItem.OperationId && item.Operation == "seq.bootstrap",
            cancellationToken);
        entity.Status = "running";
        entity.StartedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.CurrentStep = "preparing-image";
        await db.SaveChangesAsync(cancellationToken);
        await WriteEventAsync(
            entity,
            MemDiagnosticSeverities.Information,
            "diagnostics.seq_bootstrap_started",
            "Guided Seq setup started.",
            createIncident: false,
            warningCode: null);

        string? passwordHash = null;
        string? failedStep = null;
        var runtimeVerified = false;
        try
        {
            ValidateReviewPolicy(workItem.Review);

            await RunStageAsync(
                entity,
                "preparing-image",
                "approved_image",
                async ct => _ = await imageResolver.ResolveForSetupAsync(ct),
                cancellationToken);

            var existingAdministratorSecret = secretResolver
                .ResolveAdminPasswordHash(options);
            var storageInspection = storageInspector.Inspect();
            if (storageInspection.State == "initialized" &&
                !existingAdministratorSecret.Available)
            {
                throw new SeqOperationException(
                    "seq_initialized_data_requires_existing_administrator_secret",
                    StatusCodes.Status409Conflict,
                    "Initialized Seq data requires its existing administrator secret. MEM will not replace first-run authority.");
            }

            await RunStageAsync(
                entity,
                "preparing-storage",
                "storage",
                _ =>
                {
                    storageInspector.Prepare();
                    return Task.CompletedTask;
                },
                cancellationToken);

            if (existingAdministratorSecret.Available)
            {
                await RunStageAsync(
                    entity,
                    "using-existing-administrator-secret",
                    "password_hash",
                    _ => Task.CompletedTask,
                    cancellationToken);
                await RunStageAsync(
                    entity,
                    "using-existing-administrator-secret",
                    "administrator_secret",
                    _ => Task.CompletedTask,
                    cancellationToken);
            }
            else
            {
                if (workItem.AdministratorPassword.Length == 0)
                {
                    throw new SeqOperationException(
                        "seq_administrator_password_required",
                        StatusCodes.Status409Conflict,
                        "The administrator password hash is unavailable. Review Seq setup again before continuing.");
                }

                await RunStageAsync(
                    entity,
                    "hashing-password",
                    "password_hash",
                    async ct => passwordHash = await passwordHashService.HashAsync(
                        workItem.AdministratorPassword.AsMemory(),
                        ct),
                    cancellationToken);

                await RunStageAsync(
                    entity,
                    "writing-administrator-secret",
                    "administrator_secret",
                    ct => secretWriter.WriteAdministratorPasswordHashAsync(
                        passwordHash ?? throw new InvalidOperationException(
                            "The generated Seq password hash was unavailable."),
                        ct),
                    cancellationToken);
                passwordHash = null;
            }

            await RunStageAsync(
                entity,
                "writing-bootstrap-state",
                "bootstrap_state",
                ct => WriteBootstrapStateAsync(
                    workItem,
                    setupStage: "deploying-runtime",
                    runtimeVerifiedAtUtc: null,
                    ct),
                cancellationToken);

            await RunStageAsync(
                entity,
                "deploying-runtime",
                "docker_runtime",
                ct => EnsureRuntimeAsync(workItem.Review.SelectedHostPort, ct),
                cancellationToken);

            await RunStageAsync(
                entity,
                "verifying-health",
                "seq_health",
                RequireHealthyAsync,
                cancellationToken);

            var runtimeVerifiedAt = timeProvider.GetUtcNow();
            runtimeVerified = true;
            await WriteBootstrapStateAsync(
                workItem,
                setupStage: "connecting",
                runtimeVerifiedAtUtc: runtimeVerifiedAt,
                cancellationToken);

            await RunStageAsync(
                entity,
                "provisioning-ingestion",
                "ingestion_connection",
                ct => ProvisionAndVerifyConnectionAsync(workItem, ct),
                cancellationToken);

            if (workItem.Review.EnableEventDelivery)
            {
                await RunStageAsync(
                    entity,
                    "preparing-event-delivery",
                    "event_delivery",
                    async ct => _ = await deliveryStateStore.SetDesiredAsync(
                        enabled: true,
                        cancellationToken: ct),
                    cancellationToken);
            }
            else
            {
                await MarkStageSkippedAsync(
                    entity,
                    "leaving-event-delivery-disabled",
                    "event_delivery",
                    cancellationToken);
            }

            var completedAt = timeProvider.GetUtcNow();
            await WriteBootstrapStateAsync(
                workItem,
                setupStage: "completed",
                runtimeVerifiedAtUtc: runtimeVerifiedAt,
                cancellationToken);

            entity.Status = "succeeded";
            entity.CurrentStep = "completed";
            entity.CompletedAtUtc = completedAt.UtcDateTime;
            entity.LockedUntilUtc = null;
            entity.LastError = null;
            entity.ResultJson = JsonSerializer.Serialize(
                new
                {
                    status = "succeeded",
                    runtime = "running",
                    health = "healthy",
                    connection = "verified",
                    dataRetained = true,
                    eventDeliveryChanged = workItem.Review.EnableEventDelivery,
                    desiredEventDeliveryEnabled = workItem.Review.EnableEventDelivery,
                    apiRestartRequired = workItem.Review.EnableEventDelivery
                },
                JsonOptions);
            await db.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(
                entity,
                MemDiagnosticSeverities.Information,
                "diagnostics.seq_bootstrap_completed",
                workItem.Review.EnableEventDelivery
                    ? "Guided Seq setup completed, authenticated ingestion was verified, and event delivery was prepared for the next API start."
                    : "Guided Seq setup completed and authenticated MEM ingestion was verified with event delivery left disabled.",
                createIncident: false,
                warningCode: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            failedStep = entity.CurrentStep;
            if (runtimeVerified)
            {
                await MarkNeedsAttentionAsync(
                    entity,
                    failedStep,
                    "seq_bootstrap_cancelled",
                    "Seq is running and healthy, but guided MEM connection or delivery preparation was cancelled.");
            }
            else
            {
                await MarkFailedAsync(
                    entity,
                    failedStep,
                    "seq_bootstrap_cancelled",
                    "Guided Seq setup was cancelled.");
            }
        }
        catch (SeqOperationException exception)
        {
            failedStep = entity.CurrentStep;
            if (runtimeVerified)
            {
                await MarkNeedsAttentionAsync(
                    entity,
                    failedStep,
                    exception.Code,
                    exception.Message);
            }
            else
            {
                await MarkFailedAsync(
                    entity,
                    failedStep,
                    exception.Code,
                    exception.Message);
            }
        }
        catch (Exception)
        {
            failedStep = entity.CurrentStep;
            if (runtimeVerified)
            {
                await MarkNeedsAttentionAsync(
                    entity,
                    failedStep,
                    "seq_bootstrap_completion_failed",
                    "Seq is running and healthy, but MEM could not complete connection or delivery preparation.");
            }
            else
            {
                await MarkFailedAsync(
                    entity,
                    failedStep,
                    "seq_bootstrap_failed",
                    "Guided Seq setup failed unexpectedly.");
            }
        }
        finally
        {
            passwordHash = null;
        }
    }


    private void ValidateReviewPolicy(SeqBootstrapReviewSnapshot review)
    {
        if (!review.AcceptEula ||
            !string.Equals(
                review.ApprovedImageReference,
                options.ApprovedImageReference,
                StringComparison.Ordinal) ||
            !string.Equals(
                review.ExpectedVersion,
                options.ExpectedVersion,
                StringComparison.Ordinal) ||
            review.SelectedHostPort != options.PreferredHostPort)
        {
            throw new SeqOperationException(
                "seq_bootstrap_review_stale",
                StatusCodes.Status409Conflict,
                "The reviewed Seq setup no longer matches current server policy. Review setup again before continuing.");
        }
    }

    private async Task ProvisionAndVerifyConnectionAsync(
        SeqBootstrapWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var passwordCharacters = workItem.Review.AdministratorPasswordRequired
            ? workItem.AdministratorPassword
            : workItem.ConnectionAdministratorPassword;
        if (passwordCharacters.Length == 0)
        {
            throw new SeqOperationException(
                "seq_connection_administrator_password_required",
                StatusCodes.Status409Conflict,
                "The current Seq administrator password is required to provision and verify MEM ingestion.");
        }

        var password = new string(passwordCharacters);
        try
        {
            _ = await connectionProvisioner.ProvisionAndVerifyAsync(
                password,
                workItem.OperationId,
                cancellationToken);
        }
        finally
        {
            password = string.Empty;
        }
    }

    private Task MarkStageSkippedAsync(
        RuntimeOperationEntity entity,
        string step,
        string checkCode,
        CancellationToken cancellationToken) =>
        UpdateProgressAsync(
            entity,
            step,
            checkCode,
            "skipped",
            warningCode: null,
            cancellationToken);

    private async Task RunStageAsync(
        RuntimeOperationEntity entity,
        string step,
        string checkCode,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await UpdateProgressAsync(
            entity,
            step,
            checkCode,
            "running",
            warningCode: null,
            cancellationToken);
        await action(cancellationToken);
        await UpdateProgressAsync(
            entity,
            step,
            checkCode,
            "passed",
            warningCode: null,
            cancellationToken);
    }

    private async Task UpdateProgressAsync(
        RuntimeOperationEntity entity,
        string step,
        string checkCode,
        string status,
        string? warningCode,
        CancellationToken cancellationToken)
    {
        entity.CurrentStep = step;
        entity.LockedUntilUtc = timeProvider.GetUtcNow()
            .Add(OperationLock)
            .UtcDateTime;
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            entity.EvidenceJson);
        var checks = progress.Checks
            .Select(check => string.Equals(check.Code, checkCode, StringComparison.Ordinal)
                ? check with { Status = status, WarningCode = warningCode }
                : check)
            .ToArray();
        entity.EvidenceJson = JsonSerializer.Serialize(
            progress with { Checks = checks },
            JsonOptions);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureRuntimeAsync(
        int selectedHostPort,
        CancellationToken cancellationToken)
    {
        var status = await runtimeService.GetStatusAsync(cancellationToken);
        if (status.Exists)
        {
            if (!status.Managed)
            {
                throw new SeqOperationException(
                    status.WarningCode ?? "seq_unmanaged_container",
                    StatusCodes.Status409Conflict,
                    "The detected Seq container is not proven MEM-managed and will not be changed.");
            }

            if (!status.Running)
            {
                await runtimeService.StartAsync(cancellationToken);
            }

            return;
        }

        await runtimeService.DeployAsync(
            new SeqDeployRequest(
                PreferredHostPort: selectedHostPort,
                ForcePreferredPort: true),
            cancellationToken);
    }

    private async Task RequireHealthyAsync(CancellationToken cancellationToken)
    {
        SeqRuntimeHealthVerification? last = null;
        for (var attempt = 1; attempt <= options.BootstrapHealthAttemptCount; attempt++)
        {
            last = await healthVerifier.VerifyAsync(cancellationToken);
            if (last.Passed)
            {
                return;
            }

            if (attempt < options.BootstrapHealthAttemptCount)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.BootstrapHealthPollIntervalSeconds),
                    cancellationToken);
            }
        }

        throw new SeqOperationException(
            last?.WarningCode ?? "seq_health_verification_failed",
            StatusCodes.Status503ServiceUnavailable,
            "Seq reached a Docker runtime state but did not pass the bounded startup health verification.");
    }

    private Task WriteBootstrapStateAsync(
        SeqBootstrapWorkItem workItem,
        string setupStage,
        DateTimeOffset? runtimeVerifiedAtUtc,
        CancellationToken cancellationToken)
    {
        var previous = stateStore.Read().State ?? new SeqBootstrapState();
        return stateStore.WriteAsync(
            previous with
            {
                ManagementEnabled = true,
                EulaAccepted = true,
                EulaAcceptedAtUtc = previous.EulaAcceptedAtUtc ?? timeProvider.GetUtcNow(),
                EulaAcceptedByUserId = previous.EulaAcceptedByUserId ?? workItem.RequestedByUserId,
                SelectedHostPort = workItem.Review.SelectedHostPort,
                PrivateUiUrl = workItem.Review.PrivateUiUrl,
                SetupStage = setupStage,
                RuntimeVerifiedAtUtc = runtimeVerifiedAtUtc ?? previous.RuntimeVerifiedAtUtc,
                LastOperationId = workItem.OperationId
            },
            cancellationToken);
    }

    private async Task MarkNeedsAttentionAsync(
        RuntimeOperationEntity entity,
        string? failedStep,
        string warningCode,
        string safeMessage)
    {
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            entity.EvidenceJson);
        var checkCode = MapStepToCheck(failedStep);
        var checks = progress.Checks
            .Select(check => string.Equals(check.Code, checkCode, StringComparison.Ordinal)
                ? check with { Status = "failed", WarningCode = warningCode }
                : check.Status == "running"
                    ? check with { Status = "not-started" }
                    : check)
            .ToArray();
        entity.Status = "attention";
        entity.CurrentStep = "needs-attention";
        entity.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = warningCode;
        entity.EvidenceJson = JsonSerializer.Serialize(
            progress with
            {
                Checks = checks,
                Warnings = progress.Warnings.Append(warningCode).Distinct().ToArray()
            },
            JsonOptions);
        entity.ResultJson = JsonSerializer.Serialize(
            new
            {
                status = "attention",
                runtime = "running",
                health = "healthy",
                failedStep,
                warningCode,
                dataRetained = true
            },
            JsonOptions);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // The healthy runtime and original safe failure remain authoritative.
        }

        try
        {
            var state = stateStore.Read().State;
            if (state is not null)
            {
                await stateStore.WriteAsync(
                    state with
                    {
                        SetupStage = $"attention:{failedStep ?? "unknown"}",
                        LastOperationId = entity.Id
                    },
                    CancellationToken.None);
            }
        }
        catch
        {
            // The durable RuntimeOperation remains the primary evidence.
        }

        await WriteEventAsync(
            entity,
            MemDiagnosticSeverities.Warning,
            "diagnostics.seq_bootstrap_needs_attention",
            safeMessage,
            createIncident: true,
            warningCode: warningCode);
    }

    private async Task MarkFailedAsync(
        RuntimeOperationEntity entity,
        string? failedStep,
        string warningCode,
        string safeMessage)
    {
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            entity.EvidenceJson);
        var checkCode = MapStepToCheck(failedStep);
        var checks = progress.Checks
            .Select(check => string.Equals(check.Code, checkCode, StringComparison.Ordinal)
                ? check with { Status = "failed", WarningCode = warningCode }
                : check.Status == "running"
                    ? check with { Status = "not-started" }
                    : check)
            .ToArray();
        entity.Status = "failed";
        entity.CurrentStep = "failed";
        entity.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = warningCode;
        entity.EvidenceJson = JsonSerializer.Serialize(
            progress with
            {
                Checks = checks,
                Warnings = progress.Warnings.Append(warningCode).Distinct().ToArray()
            },
            JsonOptions);
        entity.ResultJson = JsonSerializer.Serialize(
            new { status = "failed", failedStep, warningCode },
            JsonOptions);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // The original safe failure remains authoritative.
        }

        try
        {
            var state = stateStore.Read().State;
            if (state is not null)
            {
                await stateStore.WriteAsync(
                    state with
                    {
                        SetupStage = $"failed:{failedStep ?? "unknown"}",
                        LastOperationId = entity.Id
                    },
                    CancellationToken.None);
            }
        }
        catch
        {
            // The durable RuntimeOperation remains the primary evidence.
        }

        await WriteEventAsync(
            entity,
            MemDiagnosticSeverities.Error,
            "diagnostics.seq_bootstrap_failed",
            safeMessage,
            createIncident: true,
            warningCode: warningCode);
    }

    private Task<MemDiagnosticWriteResult?> WriteEventAsync(
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
                Source: nameof(DiagnosticsSeqBootstrapOperationProcessor),
                Feature: "logging",
                Message: message,
                Stage: entity.Operation,
                CreateIncident: createIncident,
                OperationId: entity.Id,
                Resource: new MemDiagnosticResource(
                    Kind: "managed-service",
                    Id: "seq",
                    DisplayName: "Seq"),
                Details: new Dictionary<string, string?>
                {
                    ["operation"] = entity.Operation,
                    ["status"] = entity.Status,
                    ["warningCode"] = warningCode
                },
                SuggestedAction: createIncident
                    ? "Review the Seq setup progress and safe Diagnostics evidence before retrying."
                    : null,
                Retryable: createIncident));

    private static string? MapStepToCheck(string? step) => step switch
    {
        "preparing-image" => "approved_image",
        "hashing-password" => "password_hash",
        "writing-administrator-secret" => "administrator_secret",
        "using-existing-administrator-secret" => "administrator_secret",
        "preparing-storage" => "storage",
        "writing-bootstrap-state" => "bootstrap_state",
        "deploying-runtime" => "docker_runtime",
        "verifying-health" => "seq_health",
        "provisioning-ingestion" => "ingestion_connection",
        "preparing-event-delivery" => "event_delivery",
        "leaving-event-delivery-disabled" => "event_delivery",
        _ => null
    };
}
