using System.Security.Claims;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Owns the policy-aware, secret-free operation boundary that connects MEM to an
/// already healthy managed Seq runtime. The administrator password is passed
/// directly to the provisioner and is never included in operation persistence.
/// </summary>
public sealed class DiagnosticsSeqConnectionService(
    ISeqConnectionProvisioner provisioner,
    DiagnosticsSeqService diagnosticsSeqService,
    MemDbContext db,
    IMemDiagnosticEventWriter diagnosticWriter,
    TimeProvider timeProvider)
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private static readonly TimeSpan OperationLock = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<DiagnosticsSeqConnectionResponse> ConnectAsync(
        DiagnosticsSeqConnectionRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        if (!await ProcessGate.WaitAsync(0, cancellationToken))
        {
            throw OperationInProgress();
        }

        RuntimeOperationEntity? entity = null;
        var startedAt = timeProvider.GetUtcNow();
        try
        {
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
                throw OperationInProgress();
            }

            entity = new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                Operation = "seq.connect",
                Status = "running",
                RequestedBy = ResolveOperatorName(principal),
                RequestedByUserId = ResolveOperatorId(principal),
                RequestedAtUtc = startedAt.UtcDateTime,
                StartedAtUtc = startedAt.UtcDateTime,
                CurrentStep = "provisioning-ingestion-key",
                AttemptCount = 1,
                LockedUntilUtc = startedAt.Add(OperationLock).UtcDateTime,
                InputJson = JsonSerializer.Serialize(
                    new { operation = "seq.connect" },
                    JsonOptions),
                HostMutationLevel = "seq-configuration",
                RequiresConfirmation = true,
                ConfirmedAtUtc = startedAt.UtcDateTime
            };
            db.RuntimeOperations.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            var result = await provisioner.ProvisionAndVerifyAsync(
                request.AdministratorPassword,
                entity.Id,
                cancellationToken);

            var completedAt = timeProvider.GetUtcNow();
            entity.Status = "succeeded";
            entity.CurrentStep = "completed";
            entity.CompletedAtUtc = completedAt.UtcDateTime;
            entity.LockedUntilUtc = null;
            entity.LastError = null;
            entity.ResultJson = JsonSerializer.Serialize(
                new
                {
                    status = "succeeded",
                    credentialState = "available",
                    verificationState = "verified",
                    apiKeyId = result.ApiKeyId,
                    verificationId = result.VerificationId,
                    eventId = result.EventId,
                    verifiedAtUtc = result.VerifiedAtUtc,
                    result.ReusedCredential,
                    eventDeliveryChanged = false
                },
                JsonOptions);
            entity.EvidenceJson = JsonSerializer.Serialize(
                new
                {
                    keyIdentity = result.ApiKeyId,
                    verificationEventId = result.EventId,
                    verificationState = "verified"
                },
                JsonOptions);
            await db.SaveChangesAsync(cancellationToken);

            await WriteEventAsync(
                entity,
                MemDiagnosticSeverities.Information,
                "diagnostics.seq_connection_verified",
                "MEM provisioned and verified dedicated authenticated Seq ingestion.",
                createIncident: false,
                warningCode: null);

            var overview = await diagnosticsSeqService.GetOverviewAsync(
                principal,
                cancellationToken);
            return new DiagnosticsSeqConnectionResponse(
                SchemaVersion: 1,
                OperationId: entity.Id,
                Status: "succeeded",
                CredentialState: "available",
                VerificationState: "verified",
                ApiKeyId: result.ApiKeyId,
                VerificationId: result.VerificationId,
                EventId: result.EventId,
                VerifiedAtUtc: result.VerifiedAtUtc,
                ReusedCredential: result.ReusedCredential,
                Overview: overview);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (entity is not null)
            {
                await MarkFailedAsync(
                    entity,
                    "seq_connection_cancelled",
                    499,
                    "The Seq connection operation was cancelled.");
            }

            throw;
        }
        catch (SeqOperationException exception)
        {
            if (entity is not null)
            {
                await MarkFailedAsync(
                    entity,
                    exception.Code,
                    exception.StatusCode,
                    exception.Message);
            }

            throw;
        }
        catch (Exception)
        {
            if (entity is not null)
            {
                await MarkFailedAsync(
                    entity,
                    "seq_connection_failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "MEM could not complete the Seq connection operation.");
            }

            throw new SeqOperationException(
                "seq_connection_failed",
                StatusCodes.Status503ServiceUnavailable,
                "MEM could not complete the Seq connection operation. Review the safe Diagnostics evidence before retrying.");
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    private async Task MarkFailedAsync(
        RuntimeOperationEntity entity,
        string warningCode,
        int httpStatus,
        string safeMessage)
    {
        entity.Status = "failed";
        entity.CurrentStep = "failed";
        entity.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.LockedUntilUtc = null;
        entity.LastError = warningCode;
        entity.ResultJson = JsonSerializer.Serialize(
            new { status = "failed", warningCode, httpStatus },
            JsonOptions);
        entity.EvidenceJson = JsonSerializer.Serialize(
            new { verificationState = "failed", warningCode, httpStatus },
            JsonOptions);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // The original safe failure remains authoritative.
        }

        await WriteEventAsync(
            entity,
            MemDiagnosticSeverities.Error,
            "diagnostics.seq_connection_failed",
            safeMessage,
            createIncident: true,
            warningCode: warningCode,
            httpStatus: httpStatus);
    }

    private Task<MemDiagnosticWriteResult?> WriteEventAsync(
        RuntimeOperationEntity entity,
        string severity,
        string eventCode,
        string message,
        bool createIncident,
        string? warningCode,
        int? httpStatus = null) =>
        diagnosticWriter.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: severity,
                EventCode: eventCode,
                Source: nameof(DiagnosticsSeqConnectionService),
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
                    ["warningCode"] = warningCode,
                    ["httpStatus"] = httpStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                SuggestedAction: createIncident
                    ? "Review the Seq connection state and safe operation evidence before retrying."
                    : null,
                Retryable: createIncident));

    private static SeqOperationException OperationInProgress() => new(
        "seq_operation_in_progress",
        StatusCodes.Status409Conflict,
        "Another Seq operation is already running. Wait for it to finish before starting the connection workflow.");

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
