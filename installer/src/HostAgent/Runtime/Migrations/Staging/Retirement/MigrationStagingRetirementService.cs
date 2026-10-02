using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Services.TemporaryStaging;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.Staging.Retirement;

/// <summary>Admission and durable state for one reviewed Migration staging retirement.</summary>
public sealed class MigrationStagingRetirementService(
    MemDbContext db, ITemporaryStagingInventorySource inventory,
    IMigrationStagingRetirementRuntime runtime, TimeProvider clock,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MigrationStagingRetirementReview> ReviewAsync(string migrationId, string stagingRunId, CancellationToken ct)
    {
        var run = await LoadRunAsync(migrationId, stagingRunId, ct);
        var operation = await FindOperationAsync(run.Id, ct);
        if (operation?.Status is "queued" or "running" or "retired")
            return ToReview(run, operation, null, false, operation.Status == "retired" ? "already-retired" : "retirement-in-progress");
        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(TimeSpan.FromSeconds(30));
            var inspected = await ReviewCoreAsync(run, operation, bounded.Token);
            return ToReview(run, operation, inspected, true, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return ToReview(run, operation, null, false,
                ex is MigrationStagingRetirementException known ? known.Code : "inspection-unavailable");
        }
    }

    public async Task<MigrationStagingRetirementOperation> AcceptAsync(
        string migrationId, string stagingRunId, Guid operatorId, MigrationStagingRetirementRequest request, CancellationToken ct)
    {
        if (operatorId == Guid.Empty || !request.ConfirmRetirement || string.IsNullOrWhiteSpace(request.ReviewFingerprint))
            throw new MigrationStagingRetirementException("confirmation-required");
        var run = await LoadRunAsync(migrationId, stagingRunId, ct);
        var operation = await FindOperationAsync(run.Id, ct);
        // Duplicate submission/navigation from either workspace resolves to one operation.
        if (operation?.Status is "queued" or "running" or "retired") return ToOperation(operation);
        if (operation is not null && !request.Retry)
            throw new MigrationStagingRetirementException("reviewed-retry-required");
        if (operation is null && request.Retry)
            throw new MigrationStagingRetirementException("operation-not-found");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        MigrationStagingRetirementInspection inspected;
        try { inspected = await ReviewCoreAsync(run, operation, bounded.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (MigrationStagingRetirementException) { throw; }
        catch (Exception) { throw new MigrationStagingRetirementException("inspection-unavailable"); }
        if (request.ReviewFingerprint != Fingerprint(run, operation, inspected.Plan))
            throw new MigrationStagingRetirementException("review-stale");
        var now = clock.GetUtcNow().UtcDateTime;
        if (operation is null)
        {
            operation = new MigrationStagingRetirementEntity
            {
                Id = Guid.NewGuid(), MigrationIntake = run.MigrationIntake, MigrationIntakeEntityId = run.MigrationIntakeEntityId,
                StagingRun = run, MigrationStagingRunEntityId = run.Id, PrivateRuntimeStagingId = inspected.Plan.StagingId,
                ResourcePlanJson = JsonSerializer.Serialize(inspected.Plan, JsonOptions),
                RequestedByOperatorId = operatorId,
                RequestedAtUtc = now, UpdatedAtUtc = now
            };
            db.MigrationStagingRetirements.Add(operation);
        }
        else
        {
            operation.Status = "queued";
            operation.CurrentStep = "queued";
            operation.FailureCode = null;
            operation.UpdatedAtUtc = now;
            operation.StateVersion++;
        }
        run.Status = "retiring";
        run.CurrentStep = "retirement-queued";
        run.UpdatedAtUtc = now;
        AddAudit(operation, request.Retry ? "retry-confirmed" : "accepted", operatorId);
        try { await db.SaveChangesAsync(bounded.Token); }
        catch (DbUpdateException)
        {
            // A concurrent acceptance or lifecycle write cannot create a second destructive
            // worker. Never recover an unrelated DB failure by inventing acceptance.
            db.ChangeTracker.Clear();
            var accepted = await FindOperationAsync(run.Id, ct);
            if (accepted?.Status is "queued" or "running" or "retired") return ToOperation(accepted);
            throw new MigrationStagingRetirementException("review-stale");
        }
        await RecordDiagnosticAsync(operation, migrationId, "accepted", false);
        return ToOperation(operation);
    }

    // Called only by the singleton server-owned worker, in a fresh DI scope. No HTTP token.
    internal async Task ExecuteAsync(Guid operationId, CancellationToken stoppingToken)
    {
        var operation = await db.MigrationStagingRetirements.SingleAsync(x => x.Id == operationId, stoppingToken);
        if (operation.Status is not ("queued" or "running")) return;
        var run = await LoadRunByIdAsync(operation.MigrationStagingRunEntityId, stoppingToken);
        var migrationId = run.MigrationIntake.IntakeId;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        bounded.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = bounded.Token;
        try
        {
            operation.Status = "running";
            operation.AttemptCount++;
            operation.StartedAtUtc ??= clock.GetUtcNow().UtcDateTime;
            await CheckpointAsync(operation, "revalidate", ct);
            var inspection = await ReviewCoreAsync(run, operation, ct);
            var accepted = ReadPlan(operation);
            if (inspection.Plan != accepted)
                throw new MigrationStagingRetirementException("ownership-changed");
            await runtime.RetireAsync(accepted, (step, token) => CheckpointAsync(operation, step, token), ct);

            // Evidence is historical, not continuing authority to use a removed runtime.
            // Unfinished migrations must perform a fresh private test before continuing.
            run.DestroyedAtUtc ??= clock.GetUtcNow().UtcDateTime;
            run.ActiveMigrationKey = null;
            run.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            run.Status = "destroyed";
            run.CurrentStep = "destroyed";
            operation.Status = "retired";
            operation.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
            operation.FailureCode = null;
            AddAudit(operation, "retired", operation.RequestedByOperatorId);
            await CheckpointAsync(operation, "retired", ct);
            await RecordDiagnosticAsync(operation, migrationId, "retired", false);
        }
        catch (Exception ex)
        {
            // Reopen tracker state before recording failure: a failed commit is not a
            // successful release of the runtime/session reservation.
            db.ChangeTracker.Clear();
            using var journal = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            operation = await db.MigrationStagingRetirements.SingleAsync(x => x.Id == operationId, journal.Token);
            if (operation.Status == "retired") return;
            var shuttingDown = stoppingToken.IsCancellationRequested;
            operation.Status = shuttingDown ? "running" : "needs-attention";
            operation.FailureCode = shuttingDown ? "control-plane-restarting"
                : ex is MigrationStagingRetirementException known ? known.Code
                : ex is OperationCanceledException ? "operation-timeout" : "cleanup-incomplete";
            var retainedRun = await db.MigrationStagingRuns.SingleAsync(x => x.Id == operation.MigrationStagingRunEntityId, journal.Token);
            retainedRun.Status = shuttingDown ? "retiring" : "retirement-needs-attention";
            retainedRun.CurrentStep = shuttingDown ? "retirement-interrupted" : "retirement-needs-attention";
            retainedRun.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            AddAudit(operation, shuttingDown ? "interrupted" : "needs-attention", operation.RequestedByOperatorId);
            await CheckpointAsync(operation, shuttingDown ? "interrupted" : "needs-attention", journal.Token);
            await RecordDiagnosticAsync(operation, migrationId, shuttingDown ? "interrupted" : "needs-attention", !shuttingDown);
        }
    }

    private async Task<MigrationStagingRetirementInspection> ReviewCoreAsync(
        MigrationStagingRunEntity run, MigrationStagingRetirementEntity? operation, CancellationToken ct)
    {
        var blocker = LifecycleBlocker(run, operation is not null);
        if (blocker is not null) throw new MigrationStagingRetirementException(blocker);
        if (await db.MigrationStagingRetirements.AsNoTracking().AnyAsync(x =>
            x.MigrationIntakeEntityId == run.MigrationIntakeEntityId && x.MigrationStagingRunEntityId != run.Id && x.Status != "retired", ct))
            throw new MigrationStagingRetirementException("other-retirement-active");
        var snapshot = await inventory.ReadAsync(ct);
        if (!snapshot.ContainersAvailable || !snapshot.NetworksAvailable || !snapshot.HistoryAvailable || !snapshot.OwnershipAvailable)
            throw new MigrationStagingRetirementException("inspection-unavailable");
        // The directory projection is a necessary association check, not sufficient
        // destructive authority. The runtime adapter additionally inspects exact resources,
        // private networking, NPM consumers and filesystem boundaries.
        var owners = snapshot.Owners.Where(x => x.StagingId == run.PrivateRuntimeStagingId).ToArray();
        if (owners.Length != 1 || !owners[0].BindingValid || owners[0].Kind != "migration" ||
            owners[0].OwnerId != run.MigrationIntake.IntakeId || owners[0].SourceIdentity != run.CandidateArtifact.CandidateArtifactId)
            throw new MigrationStagingRetirementException("ownership-unproven");
        var projected = TemporaryStagingInventoryService.Project(snapshot, clock.GetUtcNow()).Items
            .SingleOrDefault(x => x.StagingId == run.PrivateRuntimeStagingId);
        // The inventory omits fully retired resources. An accepted operation can still
        // need its DB completion commit after the runtime/history cleanup was durable.
        if (projected is not null && projected.OwnershipStatus == "unresolved")
            throw new MigrationStagingRetirementException("ownership-unproven");
        var inspection = await runtime.InspectAsync(run.PrivateRuntimeStagingId!, run.CandidateArtifact.CandidateArtifactId, ct);
        if (operation is not null && ReadPlan(operation) != inspection.Plan)
            throw new MigrationStagingRetirementException("ownership-changed");
        return inspection;
    }

    internal static string? LifecycleBlocker(MigrationStagingRunEntity run, bool acceptedRetirement = false)
    {
        var intake = run.MigrationIntake;
        if (string.IsNullOrWhiteSpace(run.PrivateRuntimeStagingId) ||
            run.CandidateArtifact.ConversionAttempt.MigrationIntakeEntityId != intake.Id)
            return "ownership-unproven";
        if (!run.PrivateOnly || run.PublicRoutesCreated) return "public-resource";
        if (intake.LifecycleStatus is "cancelled" or "closed" || intake.CancelledAtUtc is not null) return "session-closed";
        if (run.Status is not ("verified" or "failed" or "failed-cleaned" or "destroyed") &&
            !(acceptedRetirement && run.Status is ("retiring" or "retirement-needs-attention"))) return "workflow-busy";
        if (intake.StagingRuns.Any(x => x.Status is "running" or "pending") ||
            intake.ConversionAttempts.Any(x => x.Status is "running" or "pending") ||
            intake.PackageRevisions.Any(x => x.Status is "uploading" or "validating" or "package-uploading" or "package-validating"))
            return "workflow-busy";
        // 01B stops at the production-adoption boundary. A retained adoption plan can
        // bind private/production resources even before its status appears complete.
        // Completed automatic cleanup keeps its existing accepted+baseline path.
        if (intake.Acceptance is not null || intake.BaselineBackupHandoff is not null ||
            intake.LifecycleStatus == MigrationSessionLifecycleStatuses.Completed) return "completed-recovery-required";
        if (intake.ProductionAdoption is not null) return "private-target-recovery-required";
        return null;
    }

    private Task<MigrationStagingRetirementEntity?> FindOperationAsync(Guid runId, CancellationToken ct) =>
        db.MigrationStagingRetirements.SingleOrDefaultAsync(x => x.MigrationStagingRunEntityId == runId, ct);

    private IQueryable<MigrationStagingRunEntity> RunQuery() => db.MigrationStagingRuns
        .Include(x => x.CandidateArtifact).ThenInclude(x => x.ConversionAttempt)
        .Include(x => x.MigrationIntake).ThenInclude(x => x.StagingRuns)
        .Include(x => x.MigrationIntake).ThenInclude(x => x.ConversionAttempts)
        .Include(x => x.MigrationIntake).ThenInclude(x => x.PackageRevisions)
        .Include(x => x.MigrationIntake).ThenInclude(x => x.ProductionAdoption)
        .Include(x => x.MigrationIntake).ThenInclude(x => x.Acceptance)
        .Include(x => x.MigrationIntake).ThenInclude(x => x.BaselineBackupHandoff);

    private async Task<MigrationStagingRunEntity> LoadRunAsync(string migrationId, string stagingRunId, CancellationToken ct) =>
        await RunQuery().SingleOrDefaultAsync(x => x.StagingRunId == stagingRunId && x.MigrationIntake.IntakeId == migrationId, ct)
        ?? throw new MigrationStagingRetirementException("staging-not-found");
    private async Task<MigrationStagingRunEntity> LoadRunByIdAsync(Guid id, CancellationToken ct) =>
        await RunQuery().SingleAsync(x => x.Id == id, ct);

    private static MigrationStagingRetirementPlan ReadPlan(MigrationStagingRetirementEntity operation) =>
        JsonSerializer.Deserialize<MigrationStagingRetirementPlan>(operation.ResourcePlanJson, JsonOptions)
        ?? throw new MigrationStagingRetirementException("ownership-unproven");

    private static string Fingerprint(MigrationStagingRunEntity run, MigrationStagingRetirementEntity? operation, MigrationStagingRetirementPlan plan)
    {
        var payload = JsonSerializer.Serialize(new { run.Id, run.MigrationIntake.StateVersion,
            RetirementVersion = operation?.StateVersion ?? 0, Plan = plan }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
    private static MigrationStagingRetirementReview ToReview(MigrationStagingRunEntity run, MigrationStagingRetirementEntity? operation,
        MigrationStagingRetirementInspection? inspection, bool canRetire, string? blocker) =>
        new(run.MigrationIntake.IntakeId, run.StagingRunId, run.MigrationIntake.DisplayName, canRetire, blocker,
            inspection is not null && canRetire ? Fingerprint(run, operation, inspection.Plan) : null,
            inspection?.ContainerCount ?? 0, inspection?.NetworkCount ?? 0, inspection?.WorkspacePresent ?? false,
            operation is null ? null : ToOperation(operation));
    internal static MigrationStagingRetirementOperation ToOperation(MigrationStagingRetirementEntity operation) =>
        new(operation.Id, operation.Status, operation.CurrentStep, operation.AttemptCount, operation.RequestedAtUtc,
            operation.UpdatedAtUtc, operation.CompletedAtUtc, operation.FailureCode);

    private async Task CheckpointAsync(MigrationStagingRetirementEntity operation, string step, CancellationToken ct)
    {
        operation.CurrentStep = step;
        operation.StateVersion++;
        operation.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
    private void AddAudit(MigrationStagingRetirementEntity operation, string outcome, Guid actor) =>
        db.MemOperatorAuditEvents.Add(new MemOperatorAuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = clock.GetUtcNow(), EventType = "migration.staging.retirement." + outcome,
            Outcome = outcome, ActorOperatorId = actor, CorrelationId = operation.Id.ToString("D"), ReasonCode = operation.FailureCode
        });
    private Task<MemDiagnosticWriteResult?> RecordDiagnosticAsync(MigrationStagingRetirementEntity operation, string migrationId, string state, bool incident) =>
        diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: incident ? MemDiagnosticSeverities.Error : MemDiagnosticSeverities.Information,
            EventCode: "migration.staging.retirement." + state, Source: "host-agent.migration-staging-retirement",
            Feature: "migration", Stage: operation.CurrentStep,
            Message: incident ? "Migration staging retirement needs attention. Review the retained operation before retrying."
                : "Migration staging retirement lifecycle updated.", CreateIncident: incident,
            Resource: new MemDiagnosticResource(Kind: "migration", Id: migrationId, DisplayName: "Migration session",
                WorkspacePath: $"/migrations/{Uri.EscapeDataString(migrationId)}"),
            Details: new Dictionary<string, string?> { ["operationId"] = operation.Id.ToString("D"),
                ["status"] = operation.Status, ["failureCode"] = operation.FailureCode }, Retryable: incident));
}
