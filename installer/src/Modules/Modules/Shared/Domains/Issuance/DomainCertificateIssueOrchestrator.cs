using System.Net.Mail;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Renewal;
using Shared.Diagnostics;

namespace Modules.Shared.Domains.Issuance;

public sealed class DomainCertificateIssueOrchestrator(
    MemDbContext db,
    IDomainSecretStore secretStore,
    IDomainCertificateIssueExecutor executor,
    DomainCertificateRenewalService renewalService,
    TimeProvider timeProvider,
    IMemDiagnosticEventWriter diagnostics,
    ILogger<DomainCertificateIssueOrchestrator> logger)
{
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    private static readonly TimeSpan OperationLease = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaximumProgressHistory = 24;

    public async Task<DomainCertificateIssueQueueResult> QueueAsync(
        Guid domainId,
        DomainCertificateIssueStartRequest request,
        Guid? requestedByUserId,
        CancellationToken cancellationToken)
    {
        var normalizedRequestId = NormalizeRequestId(request.RequestId);
        if (normalizedRequestId is null)
        {
            return new(false, "InvalidRequestId", "A valid issuance request id is required.", null);
        }

        var email = NormalizeEmail(request.Email);
        if (email is null)
        {
            return new(false, "AcmeEmailInvalid", "A valid ACME contact email is required.", null);
        }

        var providerToken = request.ProviderToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(providerToken))
        {
            return new(false, "DesecTokenMissing", "A deSEC provider credential is required.", null);
        }

        var domain = await db.Domains
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return new(false, "NotFound", $"Domain '{domainId}' was not found.", null);
        }

        if (!string.Equals(domain.DnsProvider, "desec", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "UnsupportedDnsProvider", "Guided certificate issuance currently supports deSEC only.", null);
        }

        var completedKey = DomainCertificateIssueOperation.BuildCompletedIdempotencyKey(normalizedRequestId);
        var existingCompleted = await db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.DomainId == domainId &&
                     x.Operation == DomainCertificateIssueOperation.OperationName &&
                     x.IdempotencyKey == completedKey,
                cancellationToken);

        if (existingCompleted is not null)
        {
            return new(
                true,
                "AlreadyCompleted",
                "This certificate issuance request was already recorded.",
                ToState(existingCompleted));
        }

        var active = await FindActiveAsync(domainId, cancellationToken);
        if (active is not null)
        {
            var activeInput = DeserializeInput(active.InputJson);
            var sameRequest = string.Equals(
                activeInput?.RequestId,
                normalizedRequestId,
                StringComparison.OrdinalIgnoreCase);

            return new(
                sameRequest,
                sameRequest ? "AlreadyQueued" : "OperationInProgress",
                sameRequest
                    ? "This certificate issuance request is already owned by the Control Plane."
                    : "Another certificate issuance operation is already active for this Domain.",
                ToState(active));
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var operationId = Guid.NewGuid();
        var input = new DomainCertificateIssueInput(
            normalizedRequestId,
            email,
            request.UseStaging);
        var operation = new RuntimeOperationEntity
        {
            Id = operationId,
            DomainId = domainId,
            Operation = DomainCertificateIssueOperation.OperationName,
            Status = DomainCertificateIssueOperation.QueuedStatus,
            IdempotencyKey = DomainCertificateIssueOperation.ActiveIdempotencyKey,
            RequestedBy = "operator",
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            CurrentStep = "certificate.queued",
            AttemptCount = 0,
            InputJson = JsonSerializer.Serialize(input, JsonOptions),
            EvidenceJson = JsonSerializer.Serialize(
                new DomainCertificateIssueProgressState(
                    "Waiting for the Control Plane certificate worker.",
                    [new DomainCertificateIssueProgressSnapshot(
                        "certificate.queued",
                        "Waiting for the Control Plane certificate worker.",
                        nowUtc)]),
                JsonOptions),
            HostMutationLevel = "certificate-issuance"
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.RuntimeOperations.Add(operation);
            await secretStore.SetProtectedAsync(
                domainId,
                DomainCertificateIssueOperation.CandidateSecretCategory,
                DomainCertificateIssueOperation.BuildCandidateSecretKey(operationId),
                providerToken,
                request.UseStaging
                    ? "Temporary deSEC credential for a staging certificate issuance operation"
                    : "Protected candidate deSEC credential pending successful production certificate issuance",
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();

            var raced = await FindActiveAsync(domainId, cancellationToken);
            if (raced is not null)
            {
                var racedInput = DeserializeInput(raced.InputJson);
                var sameRequest = string.Equals(
                    racedInput?.RequestId,
                    normalizedRequestId,
                    StringComparison.OrdinalIgnoreCase);

                return new(
                    sameRequest,
                    sameRequest ? "AlreadyQueued" : "OperationInProgress",
                    sameRequest
                        ? "This certificate issuance request is already owned by the Control Plane."
                        : "Another certificate issuance operation is already active for this Domain.",
                    ToState(raced));
            }

            throw;
        }

        return new(
            true,
            "Queued",
            "Certificate issuance was queued as a durable Control Plane operation.",
            ToState(operation));
    }

    public async Task<DomainCertificateIssueOperationState?> GetLatestAsync(
        Guid domainId,
        CancellationToken cancellationToken)
    {
        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.DomainId == domainId &&
                x.Operation == DomainCertificateIssueOperation.OperationName)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return operation is null ? null : ToState(operation);
    }

    public async Task<IReadOnlyList<DomainCertificateIssueActiveOperationState>> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        var operations = await db.RuntimeOperations
            .AsNoTracking()
            .Include(x => x.Domain)
            .Where(x =>
                x.Operation == DomainCertificateIssueOperation.OperationName &&
                x.DomainId != null &&
                x.Domain != null &&
                (x.Status == DomainCertificateIssueOperation.QueuedStatus ||
                 x.Status == DomainCertificateIssueOperation.RunningStatus))
            .OrderBy(x => x.RequestedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return operations
            .Select(operation =>
            {
                var state = ToState(operation);
                return new DomainCertificateIssueActiveOperationState(
                    state.OperationId,
                    state.DomainId,
                    operation.Domain!.BaseDomain,
                    state.Status,
                    state.PhaseCode,
                    state.PhaseSummary,
                    state.UseStaging,
                    state.RequestedAtUtc,
                    state.StartedAtUtc);
            })
            .ToList();
    }

    public async Task<DomainCertificateIssueOperationState?> GetAsync(
        Guid domainId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == operationId &&
                     x.DomainId == domainId &&
                     x.Operation == DomainCertificateIssueOperation.OperationName,
                cancellationToken);

        return operation is null ? null : ToState(operation);
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        await ScanGate.WaitAsync(cancellationToken);
        try
        {
            await CleanupTerminalCandidateSecretsAsync(cancellationToken);

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var operationIds = await db.RuntimeOperations
                .AsNoTracking()
                .Where(x =>
                    x.Operation == DomainCertificateIssueOperation.OperationName &&
                    (x.Status == DomainCertificateIssueOperation.QueuedStatus ||
                     (x.Status == DomainCertificateIssueOperation.RunningStatus &&
                      (x.LockedUntilUtc == null || x.LockedUntilUtc <= nowUtc))))
                .OrderBy(x => x.RequestedAtUtc)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            foreach (var operationId in operationIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessOneAsync(operationId, cancellationToken);
            }
        }
        finally
        {
            ScanGate.Release();
        }
    }

    private async Task ProcessOneAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!await TryAcquireLeaseAsync(operationId, nowUtc, cancellationToken))
        {
            return;
        }

        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == operationId &&
                     x.Operation == DomainCertificateIssueOperation.OperationName,
                cancellationToken);

        if (operation?.DomainId is not Guid domainId)
        {
            return;
        }

        var input = DeserializeInput(operation.InputJson);
        if (input is null)
        {
            await MarkTerminalAsync(
                operation,
                BuildFailure("IssuanceStateInvalid", "The durable certificate issuance state could not be read."),
                null,
                cancellationToken);
            return;
        }

        var domain = await db.Domains
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);
        if (domain is null)
        {
            await MarkTerminalAsync(
                operation,
                BuildFailure("DomainNotFound", "The owning Domain no longer exists."),
                null,
                cancellationToken);
            return;
        }

        var secretKey = DomainCertificateIssueOperation.BuildCandidateSecretKey(operationId);
        var providerToken = await secretStore.ResolveProtectedAsync(
            domainId,
            DomainCertificateIssueOperation.CandidateSecretCategory,
            secretKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(providerToken))
        {
            await MarkTerminalAsync(
                operation,
                BuildFailure("CandidateCredentialMissing", "The protected candidate deSEC credential is unavailable."),
                null,
                cancellationToken);
            return;
        }

        var zone = string.IsNullOrWhiteSpace(domain.DnsZone)
            ? domain.BaseDomain
            : domain.DnsZone!;
        var certificateRequest = new CertificateIssueRequest(
            Domain: $"*.{domain.BaseDomain}",
            Zone: zone,
            IsWildcard: true,
            Email: input.Email,
            Provider: "desec",
            ProviderToken: providerToken,
            UseStaging: input.UseStaging,
            StorageName: $"domain-{domain.BaseDomain}-{(input.UseStaging ? "staging" : "production")}");

        try
        {
            var result = await executor.ExecuteAsync(
                new DomainCertificateIssueExecutionRequest(operationId, domainId, certificateRequest),
                (progress, progressCancellation) => PersistProgressAsync(
                    operationId,
                    progress,
                    providerToken,
                    progressCancellation),
                cancellationToken);

            if (result.Succeeded)
            {
                result = await FinalizeSuccessfulIssuanceAsync(
                    operation,
                    domain,
                    input,
                    providerToken,
                    result,
                    cancellationToken);
            }

            string? diagnosticsIncidentId = null;
            if (!result.Succeeded)
            {
                var diagnostic = await DomainCertificateIssueDiagnostics.RecordFailureAsync(
                    diagnostics,
                    domainId,
                    certificateRequest,
                    result);
                diagnosticsIncidentId = diagnostic?.IncidentId;
                result = AddDiagnosticEvidence(result, diagnostic);
            }

            var safeResult = SanitizeResult(result, providerToken);
            await MarkTerminalAsync(
                operation,
                safeResult,
                diagnosticsIncidentId,
                cancellationToken);

            await DeleteCandidateSecretBestEffortAsync(domainId, operationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkInterruptedAsync(operationId);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Durable certificate issuance failed unexpectedly. OperationId={OperationId} DomainId={DomainId} FailureType={FailureType}",
                operationId,
                domainId,
                exception.GetType().Name);

            var result = BuildFailure(
                "CertificateIssueWorkerFailed",
                $"The Control Plane certificate worker failed unexpectedly ({exception.GetType().Name}).");
            var diagnostic = await DomainCertificateIssueDiagnostics.RecordFailureAsync(
                diagnostics,
                domainId,
                certificateRequest,
                result);
            var safeResult = SanitizeResult(AddDiagnosticEvidence(result, diagnostic), providerToken);

            await MarkTerminalAsync(
                operation,
                safeResult,
                diagnostic?.IncidentId,
                CancellationToken.None);
            await DeleteCandidateSecretBestEffortAsync(domainId, operationId);
        }
    }

    private async Task<CertificateOperationResult> FinalizeSuccessfulIssuanceAsync(
        RuntimeOperationEntity operation,
        DomainEntity domain,
        DomainCertificateIssueInput input,
        string providerToken,
        CertificateOperationResult result,
        CancellationToken cancellationToken)
    {
        var certificateId = EvidenceValue(result, "certificateId");
        if (string.IsNullOrWhiteSpace(certificateId))
        {
            return BuildFailure(
                "IssuedCertificateResolutionFailed",
                "Certificate issuance completed but the resulting certificate id was not recorded.");
        }

        var certificate = await db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);
        if (certificate is null || certificate.DomainId != domain.Id)
        {
            return BuildFailure(
                "CertificateOwnershipMismatch",
                "Certificate issuance completed but the stored certificate could not be resolved under its owning Domain.");
        }

        if (input.UseStaging)
        {
            return result with
            {
                Message = result.Message + " Staging issuance did not change the production renewal credential.",
                Evidence = result.Evidence.Concat([
                    new CertificateOperationEvidence(
                        "renewal.credential",
                        "unchanged by staging",
                        Sensitive: false,
                        Status: "Succeeded")
                ]).ToList()
            };
        }

        try
        {
            await PersistProgressAsync(
                operation.Id,
                new CertificateIssueProgress(
                    "certificate.renewal-credential",
                    "Securing the verified deSEC credential for unattended production renewal."),
                providerToken,
                cancellationToken);

            await renewalService.ConfigureFromSuccessfulProductionIssuanceAsync(
                domain.Id,
                input.Email,
                providerToken,
                cancellationToken);

            return result with
            {
                Message = result.Message + " The verified deSEC credential was stored securely for automatic renewal.",
                Evidence = result.Evidence.Concat([
                    new CertificateOperationEvidence(
                        "renewal.credential",
                        "configured",
                        Sensitive: false,
                        Status: "Succeeded")
                ]).ToList()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Production certificate issuance succeeded but renewal credential finalization failed. OperationId={OperationId} DomainId={DomainId} FailureType={FailureType}",
                operation.Id,
                domain.Id,
                exception.GetType().Name);

            return result with
            {
                Status = "Warning",
                Message = result.Message + " The certificate is valid, but MEM could not complete protected automatic-renewal credential persistence. Enrol the credential from Domains > Renewal.",
                ErrorCode = "RenewalCredentialPersistenceFailed",
                ErrorDetail = "Automatic renewal is not ready. The certificate itself was issued successfully.",
                Evidence = result.Evidence.Concat([
                    new CertificateOperationEvidence(
                        "renewal.credential",
                        "not configured",
                        Sensitive: false,
                        Status: "Warning")
                ]).ToList()
            };
        }
    }

    private async Task<bool> TryAcquireLeaseAsync(
        Guid operationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var lockedUntilUtc = nowUtc.Add(OperationLease);
        var updated = await db.RuntimeOperations
            .Where(x =>
                x.Id == operationId &&
                x.Operation == DomainCertificateIssueOperation.OperationName &&
                (x.Status == DomainCertificateIssueOperation.QueuedStatus ||
                 (x.Status == DomainCertificateIssueOperation.RunningStatus &&
                  (x.LockedUntilUtc == null || x.LockedUntilUtc <= nowUtc))))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, DomainCertificateIssueOperation.RunningStatus)
                    .SetProperty(x => x.StartedAtUtc, nowUtc)
                    .SetProperty(x => x.LockedUntilUtc, lockedUntilUtc)
                    .SetProperty(x => x.CurrentStep, "certificate.start")
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
                cancellationToken);

        if (updated == 1)
        {
            // ExecuteUpdateAsync bypasses EF's change tracker. If this operation was
            // queued earlier in the same scope, a tracked Queued entity can otherwise
            // mask the persisted Running state and cause real progress callbacks to be
            // discarded. Detach only this durable operation so subsequent reads observe
            // the lease state that was just written to the database.
            var trackedOperation = db.ChangeTracker
                .Entries<RuntimeOperationEntity>()
                .FirstOrDefault(entry => entry.Entity.Id == operationId);
            if (trackedOperation is not null)
            {
                trackedOperation.State = EntityState.Detached;
            }
        }

        return updated == 1;
    }

    private async Task PersistProgressAsync(
        Guid operationId,
        CertificateIssueProgress progress,
        string exactSecret,
        CancellationToken cancellationToken)
    {
        var operation = await db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken);
        if (operation is null ||
            !string.Equals(operation.Status, DomainCertificateIssueOperation.RunningStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var safeSummary = Redact(progress.SafeSummary, exactSecret);
        if (safeSummary.Length > 500)
        {
            safeSummary = safeSummary[..500];
        }

        var state = DeserializeProgress(operation.EvidenceJson);
        var history = state.History.ToList();
        if (history.Count == 0 ||
            !string.Equals(history[^1].PhaseCode, progress.PhaseCode, StringComparison.Ordinal) ||
            !string.Equals(history[^1].SafeSummary, safeSummary, StringComparison.Ordinal))
        {
            history.Add(new DomainCertificateIssueProgressSnapshot(
                progress.PhaseCode,
                safeSummary,
                nowUtc));
        }

        if (history.Count > MaximumProgressHistory)
        {
            history = history.Skip(history.Count - MaximumProgressHistory).ToList();
        }

        operation.CurrentStep = progress.PhaseCode;
        operation.EvidenceJson = JsonSerializer.Serialize(
            state with
            {
                PhaseSummary = safeSummary,
                History = history
            },
            JsonOptions);
        operation.LockedUntilUtc = nowUtc.Add(OperationLease);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkTerminalAsync(
        RuntimeOperationEntity source,
        CertificateOperationResult result,
        string? diagnosticsIncidentId,
        CancellationToken cancellationToken)
    {
        var operation = await db.RuntimeOperations
            .FirstOrDefaultAsync(x => x.Id == source.Id, cancellationToken);
        if (operation is null)
        {
            return;
        }

        var input = DeserializeInput(operation.InputJson);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var succeeded = result.Succeeded;
        var progress = DeserializeProgress(operation.EvidenceJson);
        var terminalPhase = succeeded ? "certificate.complete" : "certificate.failed";
        var terminalSummary = succeeded
            ? "Certificate issuance completed."
            : "Certificate issuance failed.";
        var history = progress.History.ToList();
        history.Add(new DomainCertificateIssueProgressSnapshot(
            terminalPhase,
            terminalSummary,
            nowUtc));
        if (history.Count > MaximumProgressHistory)
        {
            history = history.Skip(history.Count - MaximumProgressHistory).ToList();
        }

        operation.Status = succeeded
            ? DomainCertificateIssueOperation.SucceededStatus
            : DomainCertificateIssueOperation.FailedStatus;
        operation.CurrentStep = terminalPhase;
        operation.CompletedAtUtc = nowUtc;
        operation.LockedUntilUtc = null;
        operation.LastError = succeeded ? null : result.ErrorCode ?? "CertificateIssueFailed";
        operation.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
        operation.EvidenceJson = JsonSerializer.Serialize(
            progress with
            {
                PhaseSummary = terminalSummary,
                History = history,
                DiagnosticsIncidentId = diagnosticsIncidentId ?? progress.DiagnosticsIncidentId
            },
            JsonOptions);
        operation.IdempotencyKey = input is null
            ? $"issue:completed:{operation.Id:N}"
            : DomainCertificateIssueOperation.BuildCompletedIdempotencyKey(input.RequestId);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkInterruptedAsync(Guid operationId)
    {
        try
        {
            await db.RuntimeOperations
                .Where(x =>
                    x.Id == operationId &&
                    x.Operation == DomainCertificateIssueOperation.OperationName &&
                    x.Status == DomainCertificateIssueOperation.RunningStatus)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, DomainCertificateIssueOperation.QueuedStatus)
                        .SetProperty(x => x.LockedUntilUtc, (DateTime?)null)
                        .SetProperty(x => x.CurrentStep, "certificate.interrupted"),
                    CancellationToken.None);
        }
        catch
        {
            // Host shutdown recovery is best effort; an expired lease also permits restart recovery.
        }
    }

    private async Task CleanupTerminalCandidateSecretsAsync(CancellationToken cancellationToken)
    {
        // Scan only candidate-secret rows, not the unbounded operation history. In the
        // healthy case this set is empty; after a crash between terminal persistence and
        // secret cleanup it contains exactly the protected credentials that need attention.
        var candidates = await db.DomainSecrets
            .AsNoTracking()
            .Where(x => x.Category == DomainCertificateIssueOperation.CandidateSecretCategory)
            .Select(x => new { x.DomainId, x.Key })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (!TryParseCandidateOperationId(candidate.Key, out var operationId))
            {
                continue;
            }

            var operation = await db.RuntimeOperations
                .AsNoTracking()
                .Where(x =>
                    x.Id == operationId &&
                    x.DomainId == candidate.DomainId &&
                    x.Operation == DomainCertificateIssueOperation.OperationName)
                .Select(x => new { x.Status })
                .FirstOrDefaultAsync(cancellationToken);

            if (operation is null ||
                operation.Status == DomainCertificateIssueOperation.SucceededStatus ||
                operation.Status == DomainCertificateIssueOperation.FailedStatus)
            {
                await DeleteCandidateSecretBestEffortAsync(candidate.DomainId, operationId);
            }
        }
    }

    private static bool TryParseCandidateOperationId(string key, out Guid operationId)
    {
        operationId = Guid.Empty;
        const string prefix = "issue.";
        const string suffix = ".desec.provider-token";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
            !key.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = key[prefix.Length..^suffix.Length];
        return Guid.TryParseExact(value, "N", out operationId);
    }

    private async Task DeleteCandidateSecretBestEffortAsync(Guid domainId, Guid operationId)
    {
        try
        {
            await secretStore.DeleteAsync(
                domainId,
                DomainCertificateIssueOperation.CandidateSecretCategory,
                DomainCertificateIssueOperation.BuildCandidateSecretKey(operationId),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Could not remove a terminal certificate issuance candidate credential. OperationId={OperationId} DomainId={DomainId} FailureType={FailureType}",
                operationId,
                domainId,
                exception.GetType().Name);
        }
    }

    private Task<RuntimeOperationEntity?> FindActiveAsync(
        Guid domainId,
        CancellationToken cancellationToken) =>
        db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                x.DomainId == domainId &&
                x.Operation == DomainCertificateIssueOperation.OperationName &&
                x.IdempotencyKey == DomainCertificateIssueOperation.ActiveIdempotencyKey &&
                (x.Status == DomainCertificateIssueOperation.QueuedStatus ||
                 x.Status == DomainCertificateIssueOperation.RunningStatus))
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static string? NormalizeRequestId(string? value)
    {
        if (!Guid.TryParse(value?.Trim(), out var parsed))
        {
            return null;
        }

        return parsed.ToString("D");
    }

    private static string? NormalizeEmail(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            var mail = new MailAddress(trimmed);
            return string.Equals(mail.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                ? mail.Address
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? EvidenceValue(CertificateOperationResult result, string key) =>
        result.Evidence
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static CertificateOperationResult BuildFailure(string errorCode, string message) =>
        new(
            Succeeded: false,
            Status: "Failed",
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: null,
            Evidence: []);

    private static CertificateOperationResult AddDiagnosticEvidence(
        CertificateOperationResult result,
        MemDiagnosticWriteResult? diagnostic)
    {
        if (diagnostic is null)
        {
            return result;
        }

        var evidence = result.Evidence.ToList();
        if (!string.IsNullOrWhiteSpace(diagnostic.IncidentId))
        {
            evidence.Add(new CertificateOperationEvidence(
                "incidentId",
                diagnostic.IncidentId!,
                Sensitive: false,
                Status: "Info"));
        }
        if (!string.IsNullOrWhiteSpace(diagnostic.EventId))
        {
            evidence.Add(new CertificateOperationEvidence(
                "diagnosticEventId",
                diagnostic.EventId!,
                Sensitive: false,
                Status: diagnostic.Stored ? "Info" : "Warning"));
        }
        if (!diagnostic.Stored && !string.IsNullOrWhiteSpace(diagnostic.WarningCode))
        {
            evidence.Add(new CertificateOperationEvidence(
                "diagnosticCapture",
                diagnostic.WarningCode!,
                Sensitive: false,
                Status: "Warning"));
        }

        return result with { Evidence = evidence };
    }

    private static CertificateOperationResult SanitizeResult(
        CertificateOperationResult result,
        string exactSecret)
    {
        var evidence = result.Evidence
            .Select(item =>
            {
                var isPath = item.Key.Contains("path", StringComparison.OrdinalIgnoreCase);
                var value = item.Sensitive
                    ? "stored securely"
                    : isPath
                        ? "stored in MEM-managed certificate storage"
                        : Redact(item.Value, exactSecret);
                return item with { Value = value };
            })
            .ToList();

        return result with
        {
            Message = Redact(result.Message, exactSecret),
            ErrorDetail = result.ErrorDetail is null ? null : Redact(result.ErrorDetail, exactSecret),
            Evidence = evidence
        };
    }

    private static string Redact(string value, string exactSecret) =>
        string.IsNullOrEmpty(exactSecret)
            ? value
            : value.Replace(exactSecret, "[redacted]", StringComparison.Ordinal);

    private static DomainCertificateIssueInput? DeserializeInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DomainCertificateIssueInput>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DomainCertificateIssueProgressState DeserializeProgress(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var state = JsonSerializer.Deserialize<DomainCertificateIssueProgressState>(json, JsonOptions);
                if (state is not null)
                {
                    return state;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new DomainCertificateIssueProgressState(null, []);
    }

    private static CertificateOperationResult? DeserializeResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CertificateOperationResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DomainCertificateIssueOperationState ToState(RuntimeOperationEntity operation)
    {
        var input = DeserializeInput(operation.InputJson)
            ?? new DomainCertificateIssueInput("unknown", string.Empty, false);
        var progress = DeserializeProgress(operation.EvidenceJson);
        var result = DeserializeResult(operation.ResultJson);
        var certificateId = result is null ? null : EvidenceValue(result, "certificateId");

        return new DomainCertificateIssueOperationState(
            operation.Id,
            operation.DomainId ?? Guid.Empty,
            input.RequestId,
            operation.Status,
            operation.CurrentStep,
            progress.PhaseSummary,
            input.UseStaging,
            NormalizeUtc(operation.RequestedAtUtc),
            NormalizeUtc(operation.StartedAtUtc),
            NormalizeUtc(operation.CompletedAtUtc),
            operation.AttemptCount,
            progress.History,
            result,
            certificateId,
            progress.DiagnosticsIncidentId);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;
}
