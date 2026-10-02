using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Runtime.Migrations.BaselineBackup;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Migrations.Acceptance;

public sealed class MigrationAcceptanceService(
    MemDbContext db,
    MigrationBaselineBackupHandoffService baselineBackupHandoffService,
    RuntimeStackManifestStore manifestStore,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string CompletionReportSchemaVersion = "mem.migration.acceptance-completion.v1";
    private const string MemVersion = "0.2.0";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<MigrationAcceptanceStateResponse> GetStateAsync(
        string migrationId,
        CancellationToken ct)
    {
        var intake = await LoadIntakeAsync(migrationId, ct);
        if (intake.Acceptance is not null)
        {
            return BuildAcceptedState(intake);
        }

        var plan = await LoadProductionPlanAsync(intake.Id, tracking: false, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var staleReason = plan is null
            ? null
            : MigrationProductionAdoptionService.ResolveStaleReason(
                plan,
                intake.ProductionAuthorities);
        var blockers = MigrationAcceptancePolicy.BuildEligibilityBlockers(
            plan,
            nowUtc,
            staleReason).ToList();
        var assurance = plan is null
            ? null
            : MigrationAssuranceProjection.Resolve(intake, plan);
        var warnings = BuildAssuranceWarnings(assurance, accepted: false);

        return new MigrationAcceptanceStateResponse(
            Source: "control-plane",
            Status: blockers.Count == 0 ? "ready-for-acceptance" : "blocked",
            MigrationId: migrationId,
            AcceptanceEligible: blockers.Count == 0,
            Accepted: false,
            PublicVerificationRequired: true,
            Assurance: assurance,
            Acceptance: null,
            LegacyRetention: null,
            BaselineBackup: null,
            Blockers: blockers,
            Warnings: warnings,
            Detail: blockers.Count == 0
                ? "Fresh durable production-verification evidence is ready. Acceptance will bind that exact evidence to the normal MEM runtime and begin the first native baseline backup."
                : "Acceptance remains unavailable until final production adoption, controlled cutover, and fresh durable production verification are complete.");
    }

    public async Task<MigrationAcceptanceStateResponse> AcceptAsync(
        string migrationId,
        string acceptedBy,
        AcceptMigrationRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        MigrationAcceptancePolicy.ValidateRequest(request);

        var intake = await LoadIntakeAsync(migrationId, ct, tracking: true);
        if (intake.Acceptance is not null)
        {
            return BuildAcceptedState(intake);
        }

        var plan = await LoadProductionPlanAsync(intake.Id, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "Migration acceptance is blocked: a completed normal-runtime production adoption plan is required.");
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var blockers = MigrationAcceptancePolicy.BuildEligibilityBlockers(
            plan,
            nowUtc,
            MigrationProductionAdoptionService.ResolveStaleReason(
                plan,
                intake.ProductionAuthorities)).ToList();
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException($"Migration acceptance is blocked: {string.Join("; ", blockers)}");
        }

        ValidateProductionVerificationEvidence(plan, migrationId, nowUtc);
        var assurance = MigrationAssuranceProjection.Resolve(intake, plan);

        var acceptance = new MigrationAcceptanceEntity
        {
            Id = Guid.NewGuid(),
            AcceptanceId = CreateId("macc", nowUtc),
            MigrationIntakeEntityId = intake.Id,
            ExecutionId = plan.CutoverExecutionId!,
            CandidateArtifactId = plan.CandidateArtifact.CandidateArtifactId,
            StagingRunId = plan.StagingRun.StagingRunId,
            PublicVerificationStatus = "passed",
            PublicVerificationEvidenceJson = plan.ProductionVerificationEvidenceJson!,
            PublicVerificationEvidenceSha256 = plan.ProductionVerificationEvidenceSha256!,
            PublicCutoverAtUtc = plan.TargetPublicAtUtc!.Value,
            AcceptedAtUtc = nowUtc,
            AcceptedBy = string.IsNullOrWhiteSpace(acceptedBy) ? "operator" : acceptedBy.Trim(),
            Note = NormalizeNote(request.Note),
            FreshPublicVerificationAcknowledged = request.AcknowledgeFreshPublicVerification,
            TargetWriteDivergenceAcknowledged = request.AcknowledgeTargetWriteDivergence,
            RollbackBoundaryAcknowledged = request.AcknowledgeRollbackBoundaryChanges,
            LegacyRetentionAcknowledged = request.AcknowledgeLegacySourceResourcesRetained,
            NoAutomaticLegacyDeletionAcknowledged = request.AcknowledgeNoAutomaticLegacyDeletion,
        };
        var retainUntil = nowUtc.AddDays(request.RetentionDays);
        var retention = new LegacyRetentionRecordEntity
        {
            Id = Guid.NewGuid(),
            RetentionRecordId = CreateId("mlr", nowUtc),
            MigrationIntakeEntityId = intake.Id,
            MigrationAcceptanceEntityId = acceptance.Id,
            Status = "active",
            CreatedAtUtc = nowUtc,
            RetainUntilUtc = retainUntil,
            CleanupEligibleAtUtc = retainUntil,
            SourceMigrationId = plan.PackageRevision.ArchiveMigrationId ?? intake.IntakeId,
            SourceProduct = plan.PackageRevision.ArchiveSourceProduct ?? "unknown",
            SourceVersion = plan.PackageRevision.ArchiveSourceVersion,
            SourcePackageRetained = !string.IsNullOrWhiteSpace(
                plan.PackageRevision.PackageFileName),
            CandidateArtifactRetained = true,
            PrivateStagingEvidenceRetained = true,
            LegacySourceResourcesRetained = request.AcknowledgeLegacySourceResourcesRetained,
            AutomaticDeletionAllowed = false,
            Summary = "Legacy source resources and migration evidence remain retained. Cleanup is a separate future action and is never automatic.",
        };

        acceptance.LegacyRetentionRecord = retention;
        intake.Acceptance = acceptance;
        intake.LegacyRetentionRecord = retention;
        db.MigrationAcceptances.Add(acceptance);
        db.LegacyRetentionRecords.Add(retention);
        await MarkRuntimeAcceptedAsync(plan, assurance, acceptance.AcceptanceId, nowUtc, ct);
        await db.SaveChangesAsync(ct);

        // Acceptance is authoritative and durable before the baseline backup starts.
        // A failed backup remains independently retryable and never rewinds acceptance.
        _ = await baselineBackupHandoffService.EnsureAndAttemptAsync(migrationId, ct);
        return await GetStateAsync(migrationId, ct);
    }

    internal static void ValidateProductionVerificationEvidence(
        MigrationProductionAdoptionEntity plan,
        string migrationId,
        DateTime nowUtc)
    {
        var evidenceJson = plan.ProductionVerificationEvidenceJson
            ?? throw new InvalidDataException("Production-verification evidence JSON is missing.");
        var expectedSha = plan.ProductionVerificationEvidenceSha256
            ?? throw new InvalidDataException("Production-verification evidence SHA-256 is missing.");
        var actualSha = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(evidenceJson))).ToLowerInvariant();
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Production-verification evidence failed SHA-256 validation.");
        }

        using var document = JsonDocument.Parse(evidenceJson);
        var root = document.RootElement;
        RequireString(root, "verificationId", plan.ProductionVerificationId);
        RequireString(root, "migrationId", migrationId);
        RequireString(root, "adoptionPlanId", plan.AdoptionPlanId);
        RequireString(root, "cutoverExecutionId", plan.CutoverExecutionId);
        RequireString(root, "planSha256", plan.PlanSha256);
        RequireString(root, "packageRevisionId", plan.PackageRevision.PackageRevisionId);
        RequireString(root, "candidateArtifactId", plan.CandidateArtifact.CandidateArtifactId);
        RequireString(root, "stagingRunId", plan.StagingRun.StagingRunId);
        RequireString(root, "targetStackSlug", plan.TargetStackSlug);
        RequireString(root, "matrixServerName", plan.MatrixServerName);

        if (!root.TryGetProperty("runtimeStackId", out var runtimeStackId) ||
            !Guid.TryParse(runtimeStackId.GetString(), out var parsedRuntimeStackId) ||
            parsedRuntimeStackId != plan.RuntimeStackId)
        {
            throw new InvalidDataException("Production-verification runtime identity does not match the adoption plan.");
        }
        if (!root.TryGetProperty("passed", out var passed) || passed.ValueKind != JsonValueKind.True)
        {
            throw new InvalidDataException("Production-verification evidence does not record a passed result.");
        }
        if (!root.TryGetProperty("readinessReportId", out var readinessReportId) ||
            !Guid.TryParse(readinessReportId.GetString(), out var parsedReadinessReportId) ||
            parsedReadinessReportId != plan.ProductionVerificationReadinessReportId)
        {
            throw new InvalidDataException("Production-verification readiness-report identity is incomplete.");
        }
        // ValidUntilUtc is retained only as historical evidence for verification records
        // created before migration evidence expiry was retired. Acceptance is bound to the
        // evidence SHA-256 and durable production identities above, not to this retired
        // projection field.
        _ = nowUtc;
        if (!root.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array ||
            checks.GetArrayLength() != plan.ProductionVerificationCheckCount ||
            checks.GetArrayLength() == 0 ||
            checks.EnumerateArray().Any(check =>
                !check.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True))
        {
            throw new InvalidDataException("Production-verification checks are incomplete or contain failures.");
        }
    }

    private async Task MarkRuntimeAcceptedAsync(
        MigrationProductionAdoptionEntity plan,
        MigrationAssuranceSummary assurance,
        string acceptanceId,
        DateTime acceptedAtUtc,
        CancellationToken ct)
    {
        var stack = await db.RuntimeStacks
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .SingleOrDefaultAsync(x => x.Id == plan.RuntimeStackId, ct)
            ?? throw new InvalidOperationException("The adopted normal Runtime Stack record was not found.");

        plan.Status = "accepted-baseline-backup-pending";
        plan.CutoverStatus = "accepted";
        plan.BlockerSummary = null;
        plan.UpdatedAtUtc = acceptedAtUtc;

        stack.Status = "migration-accepted-baseline-pending";
        stack.LastVerifiedStatus = "migration-accepted-baseline-pending";
        stack.LastVerifiedAtUtc = plan.ProductionVerificationCompletedAtUtc;
        stack.UpdatedAtUtc = acceptedAtUtc;
        stack.LastError = null;
        stack.MetadataJson = MergeMetadata(stack.MetadataJson, new Dictionary<string, string?>
        {
            ["migrationAccepted"] = "true",
            ["migrationAcceptanceId"] = acceptanceId,
            ["migrationProductionVerificationId"] = plan.ProductionVerificationId,
            ["migrationAcceptancePending"] = "false",
            ["normalBackupLifecycle"] = "baseline-pending",
        });
        foreach (var service in stack.ServiceInstances)
        {
            service.Status = "migration-accepted-baseline-pending";
            service.LastObservedAtUtc = acceptedAtUtc;
            service.UpdatedAtUtc = acceptedAtUtc;
            service.LastError = null;
            service.RuntimeMetadataJson = MergeMetadata(service.RuntimeMetadataJson, new Dictionary<string, string?>
            {
                ["migrationAccepted"] = "true",
                ["migrationAcceptanceId"] = acceptanceId,
                ["acceptancePending"] = "false",
                ["normalBackupLifecycle"] = "baseline-pending",
            });
        }
        foreach (var route in stack.Routes)
        {
            route.Status = "verified";
            route.LastVerifiedAtUtc = plan.ProductionVerificationCompletedAtUtc;
            route.LastError = null;
        }

        var manifest = await manifestStore.FindAsync(plan.RuntimeStackId.ToString(), ct)
            ?? throw new InvalidOperationException("The adopted normal runtime manifest was not found.");
        var metadata = manifest.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["migrationAccepted"] = "true";
        metadata["migrationAcceptanceId"] = acceptanceId;
        metadata["migrationProductionVerificationId"] = plan.ProductionVerificationId;
        metadata["migrationAssurance"] = assurance.AuthorityType;
        metadata["migrationRollbackAssurance"] = assurance.RollbackAssurance;
        metadata["migrationProductionAuthorityId"] = assurance.ProductionAuthorityId;
        metadata["migrationAcceptancePending"] = "false";
        metadata["normalBackupLifecycle"] = "baseline-pending";
        var matrixMetadata = manifest.Matrix.RuntimeMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        matrixMetadata["migrationAccepted"] = "true";
        matrixMetadata["migrationAcceptanceId"] = acceptanceId;
        matrixMetadata["migrationAssurance"] = assurance.AuthorityType;
        matrixMetadata["migrationRollbackAssurance"] = assurance.RollbackAssurance;
        matrixMetadata["acceptancePending"] = "false";
        matrixMetadata["normalBackupLifecycle"] = "baseline-pending";
        RuntimeStackServiceManifest? element = null;
        if (manifest.Element is not null)
        {
            var elementMetadata = manifest.Element.RuntimeMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            elementMetadata["migrationAccepted"] = "true";
            elementMetadata["migrationAcceptanceId"] = acceptanceId;
            elementMetadata["migrationAssurance"] = assurance.AuthorityType;
            elementMetadata["migrationRollbackAssurance"] = assurance.RollbackAssurance;
            elementMetadata["acceptancePending"] = "false";
            elementMetadata["normalBackupLifecycle"] = "baseline-pending";
            element = manifest.Element with { RuntimeMetadata = elementMetadata };
        }
        var updated = manifest with
        {
            LastVerifiedStatus = "migration-accepted-baseline-pending",
            LastVerifiedAtUtc = new DateTimeOffset(acceptedAtUtc, TimeSpan.Zero),
            Matrix = manifest.Matrix with { RuntimeMetadata = matrixMetadata },
            Element = element,
            Warnings = ["Migration acceptance is durable. The first native MEM baseline backup is pending."],
            Metadata = metadata,
        };
        var path = Path.GetFullPath(plan.ManifestPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The adopted normal runtime manifest file was not found.", path);
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(updated, ManifestJsonOptions), ct);
    }

    private async Task<MigrationIntakeEntity> LoadIntakeAsync(
        string migrationId,
        CancellationToken ct,
        bool tracking = false)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new FileNotFoundException("Migration Session was not found.");
        }

        IQueryable<MigrationIntakeEntity> query = db.MigrationIntakes
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.LegacyRetentionRecord)
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.BaselineBackupHandoff)
            .Include(x => x.LegacyRetentionRecord)
            .Include(x => x.BaselineBackupHandoff)
            .Include(x => x.ProductionAuthorities)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.ProductionAdoption)!
                .ThenInclude(x => x!.PackageRevision);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(x => x.IntakeId == migrationId, ct)
            ?? throw new FileNotFoundException("Migration Session was not found.");
    }

    private async Task<MigrationProductionAdoptionEntity?> LoadProductionPlanAsync(
        Guid migrationIntakeEntityId,
        bool tracking,
        CancellationToken ct)
    {
        var query = db.MigrationProductionAdoptions
            .Include(x => x.MigrationIntake)
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .Include(x => x.StagingRun)
            .AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }
        return await query.SingleOrDefaultAsync(
            x => x.MigrationIntakeEntityId == migrationIntakeEntityId,
            ct);
    }

    private static MigrationAcceptanceStateResponse BuildAcceptedState(MigrationIntakeEntity intake)
    {
        var acceptance = intake.Acceptance
            ?? throw new InvalidOperationException("Migration acceptance state is incomplete.");
        var retention = acceptance.LegacyRetentionRecord ?? intake.LegacyRetentionRecord
            ?? throw new InvalidOperationException("Legacy retention state is incomplete.");
        var (checkCount, failedCount) = ReadVerificationCounts(acceptance.PublicVerificationEvidenceJson);
        var plan = intake.ProductionAdoption
            ?? throw new InvalidOperationException("Accepted migration is missing its production adoption plan.");
        var assurance = MigrationAssuranceProjection.Resolve(intake, plan);
        var handoff = acceptance.BaselineBackupHandoff ?? intake.BaselineBackupHandoff;
        var baseline = handoff is null
            ? null
            : MigrationBaselineBackupHandoffService.ToSummary(handoff);
        var status = handoff?.Status switch
        {
            "created" => "accepted-baseline-backup-created",
            "failed" => "accepted-baseline-backup-failed",
            _ => "accepted-baseline-backup-pending",
        };
        var detail = handoff?.Status switch
        {
            "created" => "Migration acceptance and legacy retention are durable. The first native MEM baseline backup is in the Backup Catalog and the adopted Runtime Stack now owns the normal Backup/Restore lifecycle.",
            "failed" => "Migration acceptance and legacy retention are durable. Baseline backup creation failed and may be retried without undoing acceptance.",
            _ => "Migration acceptance and legacy retention are durable. The first native MEM baseline backup is pending.",
        };

        return new MigrationAcceptanceStateResponse(
            Source: "control-plane",
            Status: status,
            MigrationId: intake.IntakeId,
            AcceptanceEligible: false,
            Accepted: true,
            PublicVerificationRequired: false,
            Assurance: assurance,
            Acceptance: new MigrationAcceptanceRecord(
                acceptance.AcceptanceId,
                acceptance.ExecutionId,
                acceptance.CandidateArtifactId,
                acceptance.StagingRunId,
                acceptance.PublicCutoverAtUtc,
                acceptance.AcceptedAtUtc,
                acceptance.AcceptedBy,
                acceptance.Note,
                new MigrationPublicVerificationSummary(
                    acceptance.PublicVerificationStatus,
                    true,
                    string.Equals(acceptance.PublicVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase),
                    checkCount,
                    failedCount,
                    acceptance.PublicVerificationEvidenceSha256,
                    "The exact durable production-verification evidence was bound to acceptance without running a second independent verifier."),
                acceptance.FreshPublicVerificationAcknowledged,
                acceptance.TargetWriteDivergenceAcknowledged,
                acceptance.RollbackBoundaryAcknowledged,
                acceptance.LegacyRetentionAcknowledged,
                acceptance.NoAutomaticLegacyDeletionAcknowledged),
            LegacyRetention: new MigrationLegacyRetentionSummary(
                retention.RetentionRecordId,
                retention.Status,
                retention.CreatedAtUtc,
                retention.RetainUntilUtc,
                retention.CleanupEligibleAtUtc,
                retention.SourceMigrationId,
                retention.SourceProduct,
                retention.SourceVersion,
                retention.SourcePackageRetained,
                retention.CandidateArtifactRetained,
                retention.PrivateStagingEvidenceRetained,
                retention.LegacySourceResourcesRetained,
                retention.AutomaticDeletionAllowed,
                retention.Summary),
            BaselineBackup: baseline,
            Blockers: Array.Empty<string>(),
            Warnings: BuildAssuranceWarnings(assurance, accepted: true),
            Detail: detail);
    }

    public async Task<MigrationAcceptanceCompletionEvidenceEnvelope> GetCompletionReportAsync(
        string migrationId,
        CancellationToken ct)
    {
        var intake = await LoadIntakeAsync(migrationId, ct);
        var state = BuildAcceptedState(intake);
        if (!state.Accepted ||
            state.Acceptance is null ||
            state.LegacyRetention is null ||
            state.BaselineBackup is null ||
            !string.Equals(state.BaselineBackup.Status, "created", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The migration completion report is available only after acceptance and the first native baseline backup are complete.");
        }

        var plan = intake.ProductionAdoption!;
        var payload = new MigrationAcceptanceCompletionEvidencePayload(
            ReportId: $"mcompletion_{state.Acceptance.AcceptanceId}",
            GeneratedAtUtc: state.BaselineBackup.CompletedAtUtc!.Value,
            MemVersion: MemVersion,
            MigrationId: migrationId,
            Assurance: state.Assurance ?? throw new InvalidOperationException(
                "Accepted migration is missing its production assurance evidence."),
            AdoptionPlanId: plan.AdoptionPlanId,
            RuntimeStackId: plan.RuntimeStackId,
            TargetStackSlug: plan.TargetStackSlug,
            ProductionVerificationId: plan.ProductionVerificationId!,
            ProductionVerificationEvidenceSha256: plan.ProductionVerificationEvidenceSha256!,
            Acceptance: state.Acceptance,
            LegacyRetention: state.LegacyRetention,
            BaselineBackup: state.BaselineBackup);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var sha = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new MigrationAcceptanceCompletionEvidenceEnvelope(
            CompletionReportSchemaVersion,
            sha,
            payload);
    }

    private static IReadOnlyList<string> BuildAssuranceWarnings(
        MigrationAssuranceSummary? assurance,
        bool accepted)
    {
        var warnings = new List<string>();
        if (assurance is null)
        {
            warnings.Add("Production assurance has not yet been established.");
        }
        else if (MigrationAssuranceProjection.IsSimplified(assurance))
        {
            warnings.Add("Migration assurance is Simplified. Source-freeze evidence was not collected, no final recapture was performed, and post-capture source writes were not independently excluded.");
            warnings.Add("Production authority is the operator-attested verified snapshot. Rollback assurance is reduced and retained source resources are not an automatically synchronized replica.");
        }
        else if (assurance is not null)
        {
            warnings.Add("Migration assurance is High. Production is bound to a validated final frozen recapture with coordinated source rollback evidence.");
        }

        warnings.Add(accepted
            ? "The guaranteed lossless quick-rollback period has ended. Target-side writes after acceptance may not exist in retained source resources."
            : "Accepting the migration ends the guaranteed lossless quick-rollback period. Target-side writes after public cutover may not exist in retained source resources.");
        warnings.Add("Legacy source resources remain retained. MEM does not automatically delete external source containers, databases, files, or migration packages.");
        return warnings;
    }

    private static (int CheckCount, int FailedCount) ReadVerificationCounts(string evidenceJson)
    {
        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (!document.RootElement.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array)
            {
                return (0, 0);
            }
            var values = checks.EnumerateArray().ToArray();
            return (
                values.Length,
                values.Count(check =>
                    !check.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True));
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }

    private static void RequireString(JsonElement root, string propertyName, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) ||
            !root.TryGetProperty(propertyName, out var property) ||
            !string.Equals(property.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Production-verification evidence property '{propertyName}' does not match durable adoption authority.");
        }
    }

    private static string MergeMetadata(
        string? json,
        IReadOnlyDictionary<string, string?> additions)
    {
        Dictionary<string, string?> values;
        try
        {
            values = string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions)
                    ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            values = new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        foreach (var item in additions)
        {
            values[item.Key] = item.Value;
        }
        return JsonSerializer.Serialize(values, JsonOptions);
    }

    private static string CreateId(string prefix, DateTime utc) =>
        $"{prefix}_{utc:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}";

    private static string? NormalizeNote(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 1000)];
}
