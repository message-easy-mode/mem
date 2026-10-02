using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.Staging;

public sealed class MigrationStagingOrchestrator(
    MemDbContext db,
    IMigrationPrivateStagingRunner runner,
    MigrationStagingOperationLifetime operationLifetime,
    TimeProvider timeProvider,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    public async Task<MigrationStagingRunDto> StartAsync(
        string migrationId,
        StartMigrationStagingRequest request,
        CancellationToken cancellationToken)
    {
        var intake = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .Include(x => x.StagingRuns)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.CandidateArtifact)
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, cancellationToken)
            ?? throw new MigrationStagingException(
                "migration_not_found",
                "Migration session was not found.");

        if (intake.StagingRuns.Any(x => x.ActiveMigrationKey == migrationId))
        {
            throw new MigrationStagingException(
                "staging_already_active",
                "A retained private staging runtime already exists for this migration. Destroy it before starting another run.");
        }

        var authoritativeRevision = ResolveAuthoritativePackageRevision(intake);
        var candidate = intake.ConversionAttempts
            .Where(x => x.MigrationPackageRevisionEntityId == authoritativeRevision.Id)
            .Select(x => x.CandidateArtifact)
            .Where(x =>
                x is not null &&
                x.VerificationStatus == "verified" &&
                x.RetentionState == "active" &&
                string.Equals(
                    x.SourcePackageSha256,
                    authoritativeRevision.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x!.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new MigrationStagingException(
                authoritativeRevision.Purpose == "final"
                    ? "final_candidate_not_ready"
                    : "candidate_not_ready",
                authoritativeRevision.Purpose == "final"
                    ? "Convert the validated final package before starting private staging. The rehearsal candidate is no longer eligible."
                    : "A verified active migration candidate is required before private staging.");

        MigrationStagingRunEntity? retryOf = null;
        if (!string.IsNullOrWhiteSpace(request.RetryOfStagingRunId))
        {
            retryOf = intake.StagingRuns.SingleOrDefault(
                x => x.StagingRunId == request.RetryOfStagingRunId)
                ?? throw new MigrationStagingException(
                    "staging_retry_source_not_found",
                    "The staging run selected for retry was not found in this migration.");

            if (retryOf.ActiveMigrationKey is not null ||
                retryOf.Status is not ("failed" or "failed-cleaned" or "destroyed"))
            {
                throw new MigrationStagingException(
                    "staging_retry_not_allowed",
                    "Only a terminal failed or destroyed staging run without a retained runtime can be retried.");
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var run = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = $"mstg_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..45],
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationCandidateArtifactEntityId = candidate!.Id,
            CandidateArtifact = candidate,
            RetryOfStagingRunEntityId = retryOf?.Id,
            RetryOfStagingRun = retryOf,
            ActiveMigrationKey = migrationId,
            Status = "running",
            CurrentStep = "materialising-private-runtime",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            StartedAtUtc = now
        };

        db.MigrationStagingRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new MigrationStagingException(
                "staging_already_active",
                "A retained private staging runtime already exists for this migration.",
                exception);
        }

        using var operationScope = operationLifetime.BeginAfterAcceptance(
            migrationId,
            run.StagingRunId,
            cancellationToken);
        var operationToken = operationScope.CancellationToken;

        await RecordMigrationAsync(
            migrationId,
            eventCode: "migration.staging.started",
            severity: MemDiagnosticSeverities.Information,
            stage: run.CurrentStep,
            message: "Private migration staging started.",
            service: "synapse",
            details: new Dictionary<string, string?>
            {
                ["stagingRunId"] = run.StagingRunId,
                ["candidateArtifactId"] = candidate.CandidateArtifactId,
                ["operationLifetime"] = "server-owned"
            });

        try
        {
            var result = await runner.CreateAsync(
                intake,
                authoritativeRevision,
                candidate,
                run.StagingRunId,
                request.TargetStackSlug,
                operationToken);

            ApplyResult(run, result, migrationId);
            await db.SaveChangesAsync(operationToken);
            await RecordMigrationAsync(
                migrationId,
                eventCode: "migration.staging.completed",
                severity: string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)
                    ? MemDiagnosticSeverities.Information
                    : MemDiagnosticSeverities.Warning,
                stage: run.CurrentStep,
                message: string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)
                    ? "Private migration staging completed successfully."
                    : "Private migration staging completed with a non-success status.",
                service: "synapse",
                details: new Dictionary<string, string?>
                {
                    ["stagingRunId"] = run.StagingRunId,
                    ["privateRuntimeStagingId"] = run.PrivateRuntimeStagingId,
                    ["status"] = run.Status,
                    ["requestAborted"] = operationScope.RequestAborted.ToString()
                });
            return ToDto(run);
        }
        catch (Exception exception) when (exception is not MigrationStagingException)
        {
            var completedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            run.Status = "failed";
            run.CurrentStep = "failed";
            run.FailureCode = "staging_failed";
            run.FailureSummary = Sanitize(exception.Message, "Private staging failed.");
            run.CompletedAtUtc = completedAtUtc;
            run.UpdatedAtUtc = completedAtUtc;

            // No destroyable runtime identity was returned. Release the active key so the
            // operator can retry after reviewing the retained server-side evidence.
            run.ActiveMigrationKey = null;
            await db.SaveChangesAsync(CancellationToken.None);
            await RecordMigrationAsync(
                migrationId,
                eventCode: "migration.staging.failed",
                severity: MemDiagnosticSeverities.Error,
                stage: run.CurrentStep,
                message: "Private migration staging failed.",
                createIncident: true,
                exception: exception,
                service: "synapse",
                details: new Dictionary<string, string?>
                {
                    ["stagingRunId"] = run.StagingRunId,
                    ["failureCode"] = run.FailureCode,
                    ["failureSummary"] = run.FailureSummary,
                    ["requestAborted"] = operationScope.RequestAborted.ToString(),
                    ["operationTimeoutRequested"] = operationScope.OperationTimeoutRequested.ToString(),
                    ["applicationStoppingRequested"] = operationScope.ApplicationStoppingRequested.ToString()
                });

            throw new MigrationStagingException(
                "staging_failed",
                "Private migration staging failed. Review retained evidence before retrying.",
                exception);
        }
    }

    public async Task<IReadOnlyList<MigrationStagingRunDto>> ListAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var runs = await db.MigrationStagingRuns
            .AsNoTracking()
            .Include(x => x.MigrationIntake)
            .Include(x => x.Retirement)
            .Include(x => x.CandidateArtifact)
            .Include(x => x.RetryOfStagingRun)
            .Where(x => x.MigrationIntake.IntakeId == migrationId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return runs.Select(ToDto).ToArray();
    }

    private Task<MemDiagnosticWriteResult?> RecordMigrationAsync(
        string migrationId,
        string eventCode,
        string severity,
        string? stage,
        string message,
        bool createIncident = false,
        Exception? exception = null,
        string? service = null,
        IReadOnlyDictionary<string, string?>? details = null) =>
        diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "host-agent.migration-staging",
            Feature: "migration",
            Stage: stage,
            Message: message,
            CreateIncident: createIncident,
            Resource: new MemDiagnosticResource(
                Kind: "migration",
                Id: migrationId,
                DisplayName: "Migration session",
                Service: service,
                WorkspacePath: $"/migrations/{Uri.EscapeDataString(migrationId)}"),
            Details: details,
            Exception: exception,
            SuggestedAction: createIncident
                ? "Open the Migration Workspace and review the retained staging evidence before retrying."
                : null,
            Retryable: createIncident));

    private static MigrationPackageRevisionEntity ResolveAuthoritativePackageRevision(
        MigrationIntakeEntity intake)
    {
        var revision = MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            intake.PackageRevisions);
        if (revision is not null)
        {
            return revision;
        }

        var activeFinal = MigrationPackageRevisionAuthority.ResolveActivePurpose(
            intake.PackageRevisions,
            MigrationPackageRevisionAuthority.FinalPurpose);
        if (activeFinal is not null)
        {
            throw new MigrationStagingException(
                "final_package_not_ready",
                $"The active final package revision is '{activeFinal.Status}' and cannot be used or bypassed for private staging.");
        }

        throw new MigrationStagingException(
            "package_revision_not_ready",
            "A validated active package revision is required before private staging.");
    }


    private void ApplyResult(
        MigrationStagingRunEntity run,
        PrivateStagingRunResult result,
        string migrationId)
    {
        run.PrivateRuntimeStagingId = result.StagingId;
        run.WorkspacePath = result.WorkspacePath;
        run.Status = string.Equals(result.Status, "ready", StringComparison.OrdinalIgnoreCase)
            ? "verified"
            : string.Equals(result.Status, "failed-cleaned", StringComparison.OrdinalIgnoreCase)
                ? "failed-cleaned"
                : "failed";
        run.CurrentStep = run.Status switch
        {
            "verified" => "private-verification-complete",
            "failed-cleaned" => "private-verification-failed-cleaned",
            _ => "private-verification-failed"
        };
        run.PrivateOnly = result.Safety.PrivateOnly;
        run.PublicRoutesCreated = result.Safety.PublicRoutesCreated;
        run.DatabaseImportSucceeded = result.Database.ImportSucceeded;
        run.SynapseHealthPassed = result.Runtime.SynapseHealthPassed;
        run.ElementConfigPresent = result.Runtime.ElementConfigExtracted;
        run.ElementConfigSha256 = result.ElementConfigSha256;
        run.ElementContainerName = result.ElementContainerName;
        run.ElementContainerId = result.ElementContainerId;
        run.ElementImageReference = result.ElementApprovedReference;
        run.ElementImageId = result.ElementImage;
        run.ElementContainerStarted = result.Runtime.ElementContainerStarted;
        run.ElementHealthPassed = result.Runtime.ElementHealthPassed;
        run.ElementSynapseConnectivityPassed = result.Runtime.ElementSynapseConnectivityPassed;
        run.ElementNetworkAttached = result.Runtime.ElementNetworkAttached;
        run.SynapseImageReference = result.SynapseApprovedReference;
        run.SynapseImageId = result.SynapseImage;
        run.UsersCount = result.Database.UsersCount;
        run.RoomsCount = result.Database.RoomsCount;
        run.EventsCount = result.Database.EventsCount;
        run.MatrixServerName = result.MatrixServerName;

        var completedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        run.CompletedAtUtc = completedAtUtc;
        run.UpdatedAtUtc = completedAtUtc;

        var retainedRuntime = result.Destroy is null &&
            result.Safety.RequiresExplicitDestroy &&
            !string.IsNullOrWhiteSpace(result.StagingId);
        run.ActiveMigrationKey = retainedRuntime ? migrationId : null;

        if (run.Status != "verified")
        {
            run.FailureCode = "staging_verification_failed";
            run.FailureSummary = Sanitize(
                result.Detail,
                "Private staging verification failed.");
        }
    }

    private static MigrationStagingRunDto ToDto(MigrationStagingRunEntity run) => new(
        run.StagingRunId,
        run.MigrationIntake.IntakeId,
        run.CandidateArtifact.CandidateArtifactId,
        run.RetryOfStagingRun?.StagingRunId,
        run.Status,
        run.CurrentStep,
        run.CreatedAtUtc,
        run.UpdatedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.DestroyedAtUtc,
        run.PrivateOnly,
        run.PublicRoutesCreated,
        run.DatabaseImportSucceeded,
        run.SynapseHealthPassed,
        run.ElementConfigPresent,
        run.ElementConfigSha256,
        run.ElementContainerStarted,
        run.ElementHealthPassed,
        run.ElementSynapseConnectivityPassed,
        run.ElementNetworkAttached,
        run.ElementImageReference,
        run.SynapseImageReference,
        run.UsersCount,
        run.RoomsCount,
        run.EventsCount,
        run.MatrixServerName,
        run.FailureCode,
        run.FailureSummary,
        !string.IsNullOrWhiteSpace(run.PrivateRuntimeStagingId) &&
        (run.DestroyedAtUtc is null || run.Retirement is not null));

    private static string Sanitize(string? value, string fallback)
    {
        var redacted = RestoreDiagnosticRedactor.RedactText(value, 500);
        return string.IsNullOrWhiteSpace(redacted) ? fallback : redacted;
    }
}

public sealed class MigrationStagingException : Exception
{
    public MigrationStagingException(
        string code,
        string message,
        Exception? inner = null)
        : base(message, inner) => Code = code;

    public string Code { get; }
}
