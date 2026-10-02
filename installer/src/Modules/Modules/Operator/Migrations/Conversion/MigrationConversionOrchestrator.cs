using System.Security.Cryptography;
using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Operator.Migrations;
using Modules.Shared.RuntimeImages;
using Shared.Diagnostics;

namespace Modules.Operator.Migrations.Conversion;

public sealed class MigrationConversionOrchestrator(
    MemDbContext db,
    IMigrationConversionWorkerRunner workerRunner,
    MigrationConversionOperationLifetime operationLifetime,
    IApprovedPostgresRuntimeProvider approvedPostgresRuntimeProvider,
    IApprovedOperationalRuntimeImageProvider approvedOperationalRuntimeImageProvider,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    public async Task<MigrationConversionOptionsDto> GetOptionsAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var intake = await db.MigrationIntakes
            .AsNoTracking()
            .Include(x => x.PackageRevisions)
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, cancellationToken)
            ?? throw new MigrationConversionException("migration_not_found", "Migration session was not found.");

        var packageRevision = ResolveAuthoritativePackageRevision(intake);
        ValidatePackageRevision(packageRevision);
        var archivePath = ResolveArchivePath(migrationId, packageRevision);
        if (!File.Exists(archivePath))
        {
            throw new MigrationConversionException(
                "conversion_package_archive_unavailable",
                "The validated migration archive is no longer available on the target server.");
        }

        IReadOnlyList<MigrationPackageArchiveStackIdentity> stacks;
        try
        {
            stacks = await MigrationPackageArchiveValidator.InspectStackIdentitiesAsync(
                archivePath,
                cancellationToken);
        }
        catch (SecureMigrationIntakeException exception)
        {
            throw new MigrationConversionException(
                "conversion_package_archive_invalid",
                "The retained migration archive can no longer be inspected safely.",
                exception);
        }

        ValidateArchiveStackCount(packageRevision, stacks.Count);

        if (stacks.Count != 1)
        {
            throw new MigrationConversionException(
                "conversion_package_stack_count_invalid",
                "Current MEM migration packages must contain exactly one selected source stack.");
        }

        return new MigrationConversionOptionsDto(
            packageRevision.PackageRevisionId,
            ToSourceStackDto(stacks[0]));
    }

    public async Task<MigrationConversionAttemptDto> StartAsync(
        string migrationId,
        string? retryOfConversionAttemptId,
        CancellationToken cancellationToken)
    {
        var intake = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.PackageRevision)
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, cancellationToken)
            ?? throw new MigrationConversionException("migration_not_found", "Migration session was not found.");

        var packageRevision = ResolveAuthoritativePackageRevision(intake);
        ValidatePackageRevision(packageRevision);

        if (intake.ConversionAttempts.Any(x => x.ActiveMigrationKey == migrationId))
        {
            throw new MigrationConversionException(
                "conversion_already_active",
                "A conversion attempt is already active for this migration.");
        }

        MigrationConversionAttemptEntity? retryOf = null;
        if (!string.IsNullOrWhiteSpace(retryOfConversionAttemptId))
        {
            retryOf = intake.ConversionAttempts.SingleOrDefault(
                x => x.ConversionAttemptId == retryOfConversionAttemptId)
                ?? throw new MigrationConversionException(
                    "conversion_retry_source_not_found",
                    "The conversion attempt selected for retry was not found in this migration.");
            if (retryOf.ActiveMigrationKey is not null || retryOf.Status is not ("failed" or "cancelled"))
            {
                throw new MigrationConversionException(
                    "conversion_retry_not_allowed",
                    "Only a terminal failed or cancelled conversion attempt can be retried.");
            }
        }

        if (retryOf?.PackageRevision is not null && retryOf.PackageRevision.Id != packageRevision.Id)
        {
            throw new MigrationConversionException(
                "conversion_retry_package_superseded",
                "The selected retry belongs to a superseded package revision. Start a new conversion from the current final package authority.");
        }

        var root = ResolveDataRoot();
        var archivePath = ResolveArchivePath(root, migrationId, packageRevision);
        if (!File.Exists(archivePath))
        {
            throw new MigrationConversionException(
                "conversion_package_archive_unavailable",
                "The validated migration archive is no longer available on the target server.");
        }

        var archiveHash = await HashFileAsync(archivePath, cancellationToken);
        if (!string.Equals(
                archiveHash,
                packageRevision.DecryptedArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MigrationConversionException(
                "conversion_package_archive_changed",
                "The validated migration archive no longer matches its retained package evidence.");
        }

        var selectedSourceStack = await ResolveSourceStackAsync(
            archivePath,
            packageRevision,
            cancellationToken);

        ApprovedPostgresRuntimeDescriptor approvedPostgresRuntime;
        try
        {
            approvedPostgresRuntime = await approvedPostgresRuntimeProvider
                .ResolveForOperationAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new MigrationConversionException(
                "conversion_runtime_unavailable",
                exception.Message,
                exception);
        }

        ApprovedOperationalRuntimeImageDescriptor approvedSynapseRuntime;
        ApprovedOperationalRuntimeImageDescriptor approvedElementRuntime;
        try
        {
            // "Prepare migration data" is the explicit acquisition boundary.
            // Exact repository digests may be pulled here, before a conversion
            // attempt exists. The operation-facing resolutions below are
            // deliberately local-only and can never pull.
            await approvedOperationalRuntimeImageProvider
                .PrepareSynapseAsync(cancellationToken);
            await approvedOperationalRuntimeImageProvider
                .PrepareElementAsync(cancellationToken);

            approvedSynapseRuntime = await approvedOperationalRuntimeImageProvider
                .ResolveSynapseForOperationAsync(cancellationToken);
            approvedElementRuntime = await approvedOperationalRuntimeImageProvider
                .ResolveElementForOperationAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new MigrationConversionException(
                "conversion_runtime_unavailable",
                exception.Message,
                exception);
        }

        var conversionPostgresImage = approvedPostgresRuntime.ResolvedImageId;
        var conversionSynapseImage = approvedSynapseRuntime.ResolvedImageId;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attemptId = $"conv_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..45];
        var attempt = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = attemptId,
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = packageRevision?.Id,
            PackageRevision = packageRevision,
            ActiveMigrationKey = migrationId,
            SourcePackageSha256 = packageRevision.DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = packageRevision.ArchiveSourceVersion ?? "0.1.0",
            ConverterId = "mem-migrate-worker",
            ConverterVersion = "mem-conversion-worker-request/v2",
            Status = "pending",
            CurrentStep = "queued",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RetryOfConversionAttemptEntityId = retryOf?.Id,
        };

        db.MigrationConversionAttempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new MigrationConversionException(
                "conversion_already_active",
                "A conversion attempt is already active for this migration.",
                exception);
        }

        using var operationScope = operationLifetime.BeginAfterAcceptance(
            migrationId,
            attemptId,
            cancellationToken);
        var operationToken = operationScope.CancellationToken;

        try
        {
            var operationRoot = Path.GetFullPath(Path.Combine(root, "migration-conversions", migrationId, attemptId));
            var workspacePath = Path.Combine(operationRoot, "work");
            var outputPath = Path.Combine(operationRoot, "output");
            var logsPath = Path.Combine(operationRoot, "logs");
            Directory.CreateDirectory(workspacePath);
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(logsPath);

            attempt.WorkspacePath = workspacePath;
            attempt.EvidenceDirectoryPath = outputPath;
            attempt.LogDirectoryPath = logsPath;
            attempt.Status = "running";
            attempt.CurrentStep = "worker-starting";
            attempt.StartedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            attempt.UpdatedAtUtc = attempt.StartedAtUtc.Value;
            await db.SaveChangesAsync(operationToken);
            await RecordMigrationAsync(
                migrationId,
                eventCode: "migration.conversion.started",
                severity: MemDiagnosticSeverities.Information,
                stage: attempt.CurrentStep,
                message: "Migration package conversion started.",
                details: new Dictionary<string, string?>
                {
                    ["conversionAttemptId"] = attempt.ConversionAttemptId,
                    ["packageRevisionId"] = packageRevision.PackageRevisionId,
                    ["synapseApprovedReference"] = approvedSynapseRuntime.ApprovedReference,
                    ["synapseImageId"] = approvedSynapseRuntime.ResolvedImageId,
                    ["elementApprovedReference"] = approvedElementRuntime.ApprovedReference,
                    ["elementImageId"] = approvedElementRuntime.ResolvedImageId,
                    ["runtimeImagePreparation"] = "complete",
                    ["operationLifetime"] = "server-owned"
                });
            var request = new MigrationConversionWorkerRequest
            {
                OperationId = attemptId,
                ArchivePath = archivePath,
                WorkspacePath = workspacePath,
                OutputPath = outputPath,
                ConversionId = attemptId,
                StackId = selectedSourceStack.SourceStackId,
                SynapseImage = conversionSynapseImage,
                PostgresImage = conversionPostgresImage,
            };
            var result = await workerRunner.RunAsync(
                request,
                Path.Combine(operationRoot, "worker-request.json"),
                Path.Combine(logsPath, "worker-events.jsonl"),
                Path.Combine(logsPath, "worker-stderr.log"),
                operationToken);

            foreach (var workerEvent in result.Events)
            {
                attempt.CurrentStep = workerEvent.Phase;
                attempt.UpdatedAtUtc = workerEvent.OccurredAtUtc.UtcDateTime;
            }

            if (result.ExitCode is not (0 or 2) || result.Report is null ||
                !string.Equals(result.Report.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The conversion worker did not produce a completed conversion report.");
            }

            var report = result.Report;
            if (report.SourceStackId != selectedSourceStack.SourceStackId ||
                !string.Equals(
                    report.MatrixServerName,
                    selectedSourceStack.MatrixServerName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The conversion report source stack does not match the operator-selected archive stack.");
            }

            if (!string.Equals(report.ArchiveSha256, archiveHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The conversion report archive checksum does not match the validated package.");
            }

            var dumpPath = EnsureContained(outputPath, report.PostgreSqlDumpPath);
            var evidencePath = EnsureContained(outputPath, report.EvidencePath);
            var reportPath = EnsureContained(outputPath, result.ReportPath);
            var dumpHash = await HashFileAsync(dumpPath, operationToken);
            if (!string.Equals(dumpHash, report.PostgreSqlDumpSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The conversion output checksum does not match the worker report.");
            }

            var candidate = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(),
                CandidateArtifactId = $"mca_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..44],
                MigrationConversionAttemptEntityId = attempt.Id,
                ConversionAttempt = attempt,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "mem-conversion-output/v1",
                SourcePackageSha256 = archiveHash,
                ArtifactSha256 = dumpHash,
                ManifestSha256 = await HashFileAsync(reportPath, operationToken),
                ChecksumsSha256 = await HashFileAsync(evidencePath, operationToken),
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    report.MigrationId,
                    report.SourceStackId,
                    report.MatrixServerName,
                    report.SynapseImage,
                    report.PostgresImage,
                    report.Warnings,
                    PackageRevisionId = packageRevision.PackageRevisionId,
                    PackagePurpose = packageRevision.Purpose,
                    PackageRevisionNumber = packageRevision.RevisionNumber,
                }),
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-filesystem",
                ArtifactPath = dumpPath,
                VerificationReportPath = evidencePath,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                VerifiedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            };
            db.MigrationCandidateArtifacts.Add(candidate);

            attempt.Status = result.ExitCode == 2 ? "completed-with-warnings" : "completed";
            attempt.CurrentStep = "candidate-created";
            attempt.ResultCode = result.ExitCode.ToString();
            attempt.CompletionReportPath = reportPath;
            attempt.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            attempt.UpdatedAtUtc = attempt.CompletedAtUtc.Value;
            attempt.ActiveMigrationKey = null;
            await db.SaveChangesAsync(operationToken);
            await RecordMigrationAsync(
                migrationId,
                eventCode: "migration.conversion.completed",
                severity: result.ExitCode == 2
                    ? MemDiagnosticSeverities.Warning
                    : MemDiagnosticSeverities.Information,
                stage: attempt.CurrentStep,
                message: result.ExitCode == 2
                    ? "Migration package conversion completed with warnings."
                    : "Migration package conversion completed successfully.",
                details: new Dictionary<string, string?>
                {
                    ["conversionAttemptId"] = attempt.ConversionAttemptId,
                    ["candidateArtifactId"] = candidate.CandidateArtifactId,
                    ["status"] = attempt.Status,
                    ["requestAborted"] = operationScope.RequestAborted.ToString()
                });
            return ToDto(attempt, candidate, selectedSourceStack.SourceStackId);
        }
        catch (Exception exception) when (exception is not MigrationConversionException)
        {
            attempt.Status = "failed";
            attempt.CurrentStep = "failed";
            attempt.FailureCode = "conversion_failed";
            attempt.FailureSummary = SanitizeFailure(exception.Message);
            attempt.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            attempt.UpdatedAtUtc = attempt.CompletedAtUtc.Value;
            attempt.ActiveMigrationKey = null;
            await db.SaveChangesAsync(CancellationToken.None);
            await RecordMigrationAsync(
                migrationId,
                eventCode: "migration.conversion.failed",
                severity: MemDiagnosticSeverities.Error,
                stage: attempt.CurrentStep,
                message: "Migration package conversion failed.",
                createIncident: true,
                exception: exception,
                details: new Dictionary<string, string?>
                {
                    ["conversionAttemptId"] = attempt.ConversionAttemptId,
                    ["failureCode"] = attempt.FailureCode,
                    ["failureSummary"] = attempt.FailureSummary,
                    ["requestAborted"] = operationScope.RequestAborted.ToString(),
                    ["operationTimeoutRequested"] = operationScope.OperationTimeoutRequested.ToString(),
                    ["applicationStoppingRequested"] = operationScope.ApplicationStoppingRequested.ToString()
                });
            throw new MigrationConversionException(
                "conversion_failed",
                "The migration conversion failed. Review the retained attempt evidence before retrying.",
                exception);
        }
    }

    public async Task<IReadOnlyList<MigrationConversionAttemptDto>> ListAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var attempts = await db.MigrationConversionAttempts
            .AsNoTracking()
            .Include(x => x.MigrationIntake)
            .Include(x => x.CandidateArtifact)
            .Where(x => x.MigrationIntake.IntakeId == migrationId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return attempts
            .Select(x => ToDto(x, x.CandidateArtifact, TryReadAttemptSourceStackId(x)))
            .ToArray();
    }

    private Task<MemDiagnosticWriteResult?> RecordMigrationAsync(
        string migrationId,
        string eventCode,
        string severity,
        string? stage,
        string message,
        bool createIncident = false,
        Exception? exception = null,
        IReadOnlyDictionary<string, string?>? details = null) =>
        diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "api.migration-conversion",
            Feature: "migration",
            Stage: stage,
            Message: message,
            CreateIncident: createIncident,
            Resource: new MemDiagnosticResource(
                Kind: "migration",
                Id: migrationId,
                DisplayName: "Migration session",
                WorkspacePath: $"/migrations/{Uri.EscapeDataString(migrationId)}"),
            Details: details,
            Exception: exception,
            SuggestedAction: createIncident
                ? "Open the Migration Workspace and review the retained conversion evidence before retrying."
                : null,
            Retryable: createIncident));

    private static MigrationConversionAttemptDto ToDto(
        MigrationConversionAttemptEntity attempt,
        MigrationCandidateArtifactEntity? candidate,
        Guid? sourceStackId) => new(
            attempt.ConversionAttemptId,
            attempt.MigrationIntake.IntakeId,
            attempt.Status,
            attempt.CurrentStep,
            attempt.CreatedAtUtc,
            attempt.UpdatedAtUtc,
            attempt.StartedAtUtc,
            attempt.CompletedAtUtc,
            attempt.ResultCode,
            attempt.FailureCode,
            attempt.FailureSummary,
            sourceStackId,
            candidate?.CandidateArtifactId,
            candidate?.ArtifactKind,
            candidate?.SourcePackageSha256,
            candidate?.ArtifactSha256,
            candidate?.ManifestSha256,
            candidate?.ChecksumsSha256,
            candidate?.VerificationStatus,
            candidate?.RetentionState,
            candidate?.CreatedAtUtc,
            candidate?.VerifiedAtUtc);

    private static MigrationPackageRevisionEntity ResolveAuthoritativePackageRevision(
        MigrationIntakeEntity intake)
    {
        var revision = MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            intake.PackageRevisions);
        if (revision is null)
        {
            var activeFinal = MigrationPackageRevisionAuthority.ResolveActivePurpose(
                intake.PackageRevisions,
                MigrationPackageRevisionAuthority.FinalPurpose);
            if (activeFinal is not null)
            {
                throw new MigrationConversionException(
                    "conversion_final_package_not_ready",
                    $"The active final package revision is '{activeFinal.Status}' and cannot be used or bypassed for conversion.");
            }

            var activePreview = MigrationPackageRevisionAuthority.ResolveActivePurpose(
                intake.PackageRevisions,
                MigrationPackageRevisionAuthority.PreviewPurpose);
            if (activePreview is not null)
            {
                return activePreview;
            }

            throw new MigrationConversionException(
                "conversion_package_revision_not_found",
                "A validated active package revision is required before conversion can start.");
        }

        return revision;
    }

    private static void ValidatePackageRevision(
        MigrationPackageRevisionEntity packageRevision)
    {
        if (!string.Equals(
                packageRevision.Status,
                MigrationPackageRevisionAuthority.ValidatedStatus,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(packageRevision.DecryptedArchiveSha256))
        {
            throw new MigrationConversionException(
                "conversion_package_not_ready",
                "The migration package revision must be validated before conversion can start.");
        }

        if (!string.Equals(
                packageRevision.ArchiveSourceVersion,
                "0.1.0",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MigrationConversionException(
                "conversion_source_not_supported",
                "The current conversion worker supports only the MEM 0.1.0 source adapter.");
        }
    }



    private async Task<MigrationPackageArchiveStackIdentity> ResolveSourceStackAsync(
        string archivePath,
        MigrationPackageRevisionEntity packageRevision,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MigrationPackageArchiveStackIdentity> stacks;
        try
        {
            stacks = await MigrationPackageArchiveValidator.InspectStackIdentitiesAsync(
                archivePath,
                cancellationToken);
        }
        catch (SecureMigrationIntakeException exception)
        {
            throw new MigrationConversionException(
                "conversion_package_archive_invalid",
                "The retained migration archive can no longer be inspected safely.",
                exception);
        }

        ValidateArchiveStackCount(packageRevision, stacks.Count);
        if (stacks.Count != 1)
        {
            throw new MigrationConversionException(
                "conversion_package_stack_count_invalid",
                "Current MEM migration packages must contain exactly one source stack.");
        }

        return stacks[0];
    }

    private static void ValidateArchiveStackCount(
        MigrationPackageRevisionEntity packageRevision,
        int actualStackCount)
    {
        if (packageRevision.ArchiveStackCount is { } expectedStackCount &&
            expectedStackCount != actualStackCount)
        {
            throw new MigrationConversionException(
                "conversion_package_archive_changed",
                "The retained migration archive stack inventory no longer matches its validated package evidence.");
        }
    }

    private string ResolveArchivePath(
        string migrationId,
        MigrationPackageRevisionEntity packageRevision) =>
        ResolveArchivePath(ResolveDataRoot(), migrationId, packageRevision);

    private static string ResolveArchivePath(
        string root,
        string migrationId,
        MigrationPackageRevisionEntity packageRevision)
    {
        var intakeRoot = MigrationPackageRevisionStorage.ResolveIntakeRoot(root, migrationId);
        var archivePath = MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
            root,
            migrationId,
            packageRevision.PackageRevisionId,
            allowLegacyPreviewFallback: packageRevision.Purpose == "preview");
        return EnsureContained(intakeRoot, archivePath);
    }

    private static MigrationConversionSourceStackDto ToSourceStackDto(
        MigrationPackageArchiveStackIdentity stack) =>
        new(stack.SourceStackId, stack.Slug, stack.MatrixServerName);

    private static Guid? TryReadAttemptSourceStackId(MigrationConversionAttemptEntity attempt)
    {
        if (string.IsNullOrWhiteSpace(attempt.WorkspacePath))
        {
            return null;
        }

        try
        {
            var operationRoot = Directory.GetParent(Path.GetFullPath(attempt.WorkspacePath));
            if (operationRoot is null)
            {
                return null;
            }

            var requestPath = Path.Combine(operationRoot.FullName, "worker-request.json");
            if (!File.Exists(requestPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(requestPath));
            if (!document.RootElement.TryGetProperty("stackId", out var stackIdElement) ||
                stackIdElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return stackIdElement.TryGetGuid(out var stackId) && stackId != Guid.Empty
                ? stackId
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static string EnsureContained(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The conversion worker returned a path outside its server-owned output root.");
        }
        return fullPath;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string SanitizeFailure(string message) =>
        string.IsNullOrWhiteSpace(message) ? "Conversion failed." : message.Length <= 500 ? message : message[..500];
}

public sealed class MigrationConversionException : Exception
{
    public MigrationConversionException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;
    public string Code { get; }
}
