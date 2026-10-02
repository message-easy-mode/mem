using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

public sealed class RuntimeStackBackupProductionRestorePlanService
{
    private const string ManifestPath = "mem-stack-export/mem-export-manifest.json";
    private const string ZipDatabaseDumpPath = "mem-stack-export/database/synapse.sql";
    private const string ZipHomeserverPath = "mem-stack-export/matrix/homeserver.yaml";
    private const string ZipSigningKeyPath = "mem-stack-export/matrix/signing.key";
    private const string ZipMediaStorePrefix = "mem-stack-export/matrix/media_store/";
    private const string ZipElementConfigPath = "mem-stack-export/element/config.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly MemDbContext _db;
    private readonly NpmProxyHostService _npmProxyHostService;
    private readonly PrivateStagingHistoryService _stagingHistory;
    private readonly ValidatedImportArtifactService _validatedImportArtifacts;
    private readonly RuntimeStackBackupProductionRestorePlanHistoryService _planHistory;
    private readonly ILogger<RuntimeStackBackupProductionRestorePlanService> _logger;

    public RuntimeStackBackupProductionRestorePlanService(
    IConfiguration configuration,
    MemDbContext db,
    NpmProxyHostService npmProxyHostService,
    PrivateStagingHistoryService stagingHistory,
    ValidatedImportArtifactService validatedImportArtifacts,
    RuntimeStackBackupProductionRestorePlanHistoryService planHistory,
    ILogger<RuntimeStackBackupProductionRestorePlanService> logger)
    {
        _configuration = configuration;
        _db = db;
        _npmProxyHostService = npmProxyHostService;
        _stagingHistory = stagingHistory;
        _validatedImportArtifacts = validatedImportArtifacts;
        _planHistory = planHistory;
        _logger = logger;
    }

    public async Task<RuntimeStackBackupProductionRestorePlanResult> CreatePlanAsync(
        string validationId,
        string? stagingId,
        string? candidateId,
        string? targetStackSlug,
        string? restoreMode,
        string? intendedMatrixHost,
        string? intendedElementHost,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(validationId))
        {
            throw new InvalidOperationException("Validation id is required.");
        }

        if (!IsSafePathSegment(validationId))
        {
            throw new InvalidOperationException("Validation id contains unsafe characters.");
        }

        await _validatedImportArtifacts.EnsureRestoreCanContinueAsync(
            validationId,
            ct);

        ValidateEvidenceRuntimeSelection(stagingId, candidateId);

        var checks = new List<RuntimeStackBackupProductionRestoreCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        var source = await DiscoverSourceAsync(
            validationId,
            checks,
            blockers,
            warnings,
            errors,
            ct);

        return await CreatePlanForResolvedSourceAsync(
            validationId: validationId,
            sourceReference: validationId,
            catalogEntryId: null,
            sourceKind: "validated-import",
            source: source,
            stagingId: stagingId,
            candidateId: candidateId,
            targetStackSlug: targetStackSlug,
            restoreMode: restoreMode,
            intendedMatrixHost: intendedMatrixHost,
            intendedElementHost: intendedElementHost,
            checks: checks,
            blockers: blockers,
            warnings: warnings,
            errors: errors,
            ct: ct);
    }

    /// <summary>
    /// Builds the same read-only Production Restore plan from a source summary
    /// already resolved from the managed Backup Catalog payload. This method
    /// never accesses the validated-import artifact store.
    /// </summary>
    public async Task<RuntimeStackBackupProductionRestorePlanResult>
        CreatePlanFromCatalogSourceAsync(
            string catalogEntryId,
            string? legacyValidationId,
            RuntimeStackBackupProductionRestoreSourceSummary source,
            string? stagingId,
            string? candidateId,
            string? targetStackSlug,
            string? restoreMode,
            string? intendedMatrixHost,
            string? intendedElementHost,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(catalogEntryId))
        {
            throw new InvalidOperationException("Backup Catalog entry id is required.");
        }

        if (!IsSafePathSegment(catalogEntryId))
        {
            throw new InvalidOperationException("Backup Catalog entry id contains unsafe characters.");
        }

        ArgumentNullException.ThrowIfNull(source);

        ValidateEvidenceRuntimeSelection(stagingId, candidateId);

        var checks = new List<RuntimeStackBackupProductionRestoreCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        AddCatalogSourceChecks(
            source,
            checks,
            blockers,
            warnings);

        return await CreatePlanForResolvedSourceAsync(
            validationId: legacyValidationId,
            sourceReference: catalogEntryId,
            catalogEntryId: catalogEntryId,
            sourceKind: "backup-catalog",
            source: source,
            stagingId: stagingId,
            candidateId: candidateId,
            targetStackSlug: targetStackSlug,
            restoreMode: restoreMode,
            intendedMatrixHost: intendedMatrixHost,
            intendedElementHost: intendedElementHost,
            checks: checks,
            blockers: blockers,
            warnings: warnings,
            errors: errors,
            ct: ct);
    }

    /// <summary>
    /// Builds a read-only Production Restore plan from a trusted Migration-owned
    /// candidate and verified staging runtime. This path never reads a legacy
    /// validation receipt, Backup Catalog payload, or normal Restore Session.
    /// </summary>
    public async Task<RuntimeStackBackupProductionRestorePlanResult>
        CreatePlanFromMigrationSourceAsync(
            string migrationId,
            RuntimeStackBackupProductionRestoreSourceSummary source,
            string candidateId,
            string? targetStackSlug,
            string? restoreMode,
            string? intendedMatrixHost,
            string? intendedElementHost,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId) || !IsSafePathSegment(migrationId))
        {
            throw new InvalidOperationException("Migration id is required and must be a safe identifier.");
        }

        ArgumentNullException.ThrowIfNull(source);
        ValidateEvidenceRuntimeSelection(stagingId: null, candidateId: candidateId);

        var checks = new List<RuntimeStackBackupProductionRestoreCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        AddMigrationSourceChecks(source, checks, blockers, warnings);

        return await CreatePlanForResolvedSourceAsync(
            validationId: migrationId,
            sourceReference: migrationId,
            catalogEntryId: null,
            sourceKind: "migration-session",
            source: source,
            stagingId: null,
            candidateId: candidateId,
            targetStackSlug: targetStackSlug,
            restoreMode: restoreMode,
            intendedMatrixHost: intendedMatrixHost,
            intendedElementHost: intendedElementHost,
            checks: checks,
            blockers: blockers,
            warnings: warnings,
            errors: errors,
            ct: ct);
    }

    private async Task<RuntimeStackBackupProductionRestorePlanResult>
        CreatePlanForResolvedSourceAsync(
            string? validationId,
            string sourceReference,
            string? catalogEntryId,
            string sourceKind,
            RuntimeStackBackupProductionRestoreSourceSummary source,
            string? stagingId,
            string? candidateId,
            string? targetStackSlug,
            string? restoreMode,
            string? intendedMatrixHost,
            string? intendedElementHost,
            List<RuntimeStackBackupProductionRestoreCheck> checks,
            List<string> blockers,
            List<string> warnings,
            List<string> errors,
            CancellationToken ct)
    {
        var evidenceRuntimeId = FirstNonEmpty(
            candidateId,
            stagingId);

        var planId = CreatePlanId();
        var confirmations = new List<RuntimeStackBackupProductionRestoreConfirmation>();
        var steps = new List<RuntimeStackBackupProductionRestoreStep>();

        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            AddCheck(
                checks,
                "production-restore.candidate.private-runtime-selected",
                "production-candidate",
                "info",
                true,
                "satisfied",
                "Private Production Restore candidate was supplied as restore evidence.",
                candidateId);
        }

        var staging = await DiscoverStagingEvidenceAsync(
            evidenceRuntimeId,
            checks,
            blockers,
            warnings,
            ct);

        var resolvedRestoreMode = string.IsNullOrWhiteSpace(restoreMode)
            ? "replacement"
            : restoreMode.Trim();

        var resolvedTargetStackSlug = string.IsNullOrWhiteSpace(targetStackSlug)
            ? Slugify(source.SourceStackSlug ?? staging.TargetStackSlug ?? $"production-restore-{sourceReference}")
            : Slugify(targetStackSlug);

        var target = await DiscoverTargetAsync(
            resolvedTargetStackSlug,
            resolvedRestoreMode,
            checks,
            warnings,
            ct);

        var matrixHost = FirstNonEmpty(
            intendedMatrixHost,
            target.MatrixService?.PublicHost,
            ExtractHost(target.MatrixPublicBaseUrl),
            source.ManifestMatrixHost,
            ExtractHost(source.MatrixPublicUrl),
            source.SourceMatrixServerName);

        var elementHost = FirstNonEmpty(
            intendedElementHost,
            target.ElementService?.PublicHost,
            ExtractHost(target.ElementPublicBaseUrl),
            source.ManifestElementHost,
            ExtractHost(source.ElementPublicUrl));

        var matrixIdentity = await DiscoverMatrixIdentityAsync(
            source,
            target,
            staging,
            checks,
            blockers,
            warnings,
            ct);

        var dns = await DiscoverDnsAsync(
            matrixHost,
            elementHost,
            checks,
            warnings,
            ct);

        var certificates = await DiscoverCertificatesAsync(
            matrixHost,
            elementHost,
            dns,
            checks,
            warnings,
            ct);

        var npm = await DiscoverNpmRoutesAsync(
            matrixHost,
            elementHost,
            target,
            checks,
            warnings,
            ct);

        var replacement = BuildReplacementSummary(
            resolvedRestoreMode,
            target,
            matrixIdentity,
            checks,
            blockers,
            warnings);

        AddProductionConfirmations(confirmations);
        AddDryRunSteps(steps);

        AddCheck(
            checks,
            "production-restore.execution.locked",
            "execution-lock",
            "info",
            true,
            "satisfied",
            "Production execution is locked in this sprint.",
            "This cutover plan is read-only. No runtime, DNS, NPM, certificate, or public route mutation is available.");

        var mutations = new RuntimeStackBackupProductionRestoreMutationSummary(
            RuntimeChanged: false,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            DnsChanged: false,
            NpmRoutesChanged: false,
            CertificatesChanged: false,
            PublicRoutesChanged: false,
            FederationExposureChanged: false,
            Notes:
            [
                "This Production Restore v1A plan is read-only.",
                "No production containers were stopped, deleted, or modified.",
                "No production databases were dropped, overwritten, or modified.",
                "No DNS records were created, updated, or deleted.",
                "No NPM routes were created, updated, enabled, disabled, or deleted.",
                "No certificates were requested, imported, renewed, attached, or removed.",
                "No public cutover or federation exposure was created."
            ]);

        var status = errors.Count > 0 || blockers.Count > 0
            ? "blocked"
            : "ready_for_review";

        var result = new RuntimeStackBackupProductionRestorePlanResult(
            Source: "control-plane",
            Status: status,
            PlanId: planId,
            ValidationId: validationId,
            StagingId: evidenceRuntimeId,
            RestoreMode: resolvedRestoreMode,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ProductionExecutionLocked: true,
            Mutations: mutations,
            BackupSource: source,
            StagingEvidence: staging,
            Target: target,
            MatrixIdentity: matrixIdentity,
            Dns: dns,
            Certificates: certificates,
            Npm: npm,
            Replacement: replacement,
            Confirmations: confirmations,
            Checks: checks,
            Steps: steps,
            Blockers: blockers,
            Warnings: warnings,
            Errors: errors,
            Detail: status == "blocked"
                ? "Production cutover plan was generated, but blockers must be resolved before any future execution sprint."
                : "Production cutover plan is ready for operator review. Production execution remains locked.",
            CatalogEntryId: catalogEntryId,
            SourceKind: sourceKind);

        await _planHistory.SavePlanAsync(
            result,
            ct);

        _logger.LogInformation(
            "Created read-only Production Restore plan. PlanId={PlanId} SourceKind={SourceKind} CatalogEntryId={CatalogEntryId} ValidationId={ValidationId} StagingId={StagingId} TargetStackSlug={TargetStackSlug} Status={Status}",
            planId,
            sourceKind,
            catalogEntryId,
            validationId,
            stagingId,
            resolvedTargetStackSlug,
            status);

        return result;
    }

    private static void ValidateEvidenceRuntimeSelection(
        string? stagingId,
        string? candidateId)
    {
        if (!string.IsNullOrWhiteSpace(stagingId) &&
            !IsSafePathSegment(stagingId))
        {
            throw new InvalidOperationException("Staging id contains unsafe characters.");
        }

        if (!string.IsNullOrWhiteSpace(candidateId) &&
            !IsSafePathSegment(candidateId))
        {
            throw new InvalidOperationException("Production candidate id contains unsafe characters.");
        }

        if (!string.IsNullOrWhiteSpace(stagingId) &&
            !string.IsNullOrWhiteSpace(candidateId) &&
            !string.Equals(stagingId.Trim(), candidateId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Specify either stagingId or candidateId, not both. If both are supplied, they must refer to the same private runtime id.");
        }
    }

    private static void AddMigrationSourceChecks(
        RuntimeStackBackupProductionRestoreSourceSummary source,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        AddCheck(
            checks,
            "production-restore.source.migration-candidate-present",
            "source",
            "info",
            true,
            "satisfied",
            "A verified Migration Candidate Artifact and active Migration Staging Run were resolved by the server.",
            source.SourceStackSlug);

        var required = new[]
        {
            (source.ManifestPresent, "manifest", "Migration source manifest is available."),
            (source.DatabaseDumpPresent, "database-dump", "Migration candidate PostgreSQL dump is available."),
            (source.HomeserverConfigPresent, "homeserver-config", "Migration homeserver.yaml is available."),
            (source.SigningKeyPresent, "signing-key", "Migration signing key is available."),
        };

        foreach (var item in required)
        {
            AddCheck(
                checks,
                $"production-restore.source.migration-{item.Item2}",
                "source",
                item.Item1 ? "info" : "blocker",
                item.Item1,
                item.Item1 ? "satisfied" : "blocked",
                item.Item3,
                null);

            if (!item.Item1)
            {
                blockers.Add($"Required Migration source material is missing: {item.Item2}.");
            }
        }

        if (!source.ElementConfigPresent)
        {
            warnings.Add("Migration candidate contains no Element configuration; Element public route planning may be deferred.");
        }

        warnings.AddRange(source.ManifestWarnings);
    }

    private static void AddCatalogSourceChecks(
        RuntimeStackBackupProductionRestoreSourceSummary source,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        AddCheck(
            checks,
            "production-restore.source.backup-catalog-payload-present",
            "source",
            "info",
            true,
            "satisfied",
            "Managed Backup Catalog payload was resolved for Production Restore planning.",
            source.CatalogEntryId);

        AddCheck(
            checks,
            "production-restore.source.manifest-present",
            "source",
            source.ManifestPresent ? "info" : "blocker",
            source.ManifestPresent,
            source.ManifestPresent ? "satisfied" : "blocked",
            source.ManifestPresent
                ? "Export manifest was resolved from the managed Backup Catalog payload."
                : "Export manifest is missing from the managed Backup Catalog payload.",
            source.CatalogEntryId);

        AddCheck(
            checks,
            "production-restore.source.database-dump-present",
            "source",
            source.DatabaseDumpPresent ? "info" : "blocker",
            source.DatabaseDumpPresent,
            source.DatabaseDumpPresent ? "satisfied" : "blocked",
            source.DatabaseDumpPresent
                ? "Synapse SQL dump is present in the managed Backup Catalog payload."
                : "Synapse SQL dump is missing from the managed Backup Catalog payload.",
            source.DatabaseDumpPath);

        AddCheck(
            checks,
            "production-restore.source.homeserver-config-present",
            "source",
            source.HomeserverConfigPresent ? "info" : "blocker",
            source.HomeserverConfigPresent,
            source.HomeserverConfigPresent ? "satisfied" : "blocked",
            source.HomeserverConfigPresent
                ? "homeserver.yaml is present in the managed Backup Catalog payload."
                : "homeserver.yaml is missing from the managed Backup Catalog payload.",
            source.HomeserverConfigPath);

        AddCheck(
            checks,
            "production-restore.source.signing-key-present",
            "source",
            source.SigningKeyPresent ? "info" : "blocker",
            source.SigningKeyPresent,
            source.SigningKeyPresent ? "satisfied" : "blocked",
            source.SigningKeyPresent
                ? "Matrix signing key is present in the managed Backup Catalog payload."
                : "Matrix signing key is missing from the managed Backup Catalog payload.",
            source.SigningKeyPath);

        if (!source.ManifestPresent)
        {
            blockers.Add("Export manifest is missing from the managed Backup Catalog payload.");
        }

        if (!source.DatabaseDumpPresent)
        {
            blockers.Add("Synapse SQL dump is missing from the managed Backup Catalog payload.");
        }

        if (!source.HomeserverConfigPresent)
        {
            blockers.Add("homeserver.yaml is missing from the managed Backup Catalog payload.");
        }

        if (!source.SigningKeyPresent)
        {
            blockers.Add("Matrix signing key is missing from the managed Backup Catalog payload.");
        }

        if (!source.ElementConfigPresent)
        {
            warnings.Add("Element config.json is not present in the managed Backup Catalog payload. Production Matrix restore may still be possible, but Element route/config work may need manual review.");
        }

        foreach (var manifestWarning in source.ManifestWarnings)
        {
            warnings.Add($"Backup Catalog manifest warning: {manifestWarning}");
        }
    }

    private async Task<RuntimeStackBackupProductionRestoreSourceSummary> DiscoverSourceAsync(
        string validationId,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> blockers,
        List<string> warnings,
        List<string> errors,
        CancellationToken ct)
    {
        var dataRoot = ResolveDataRoot();

        var uploadedZipPath = ResolveUploadedZipPathOrNull(
            dataRoot,
            validationId);

        if (uploadedZipPath is null)
        {
            const string message = "Uploaded export ZIP was not found for this validation id.";

            AddCheck(
                checks,
                "production-restore.source.uploaded-zip-present",
                "source",
                "blocker",
                false,
                "blocked",
                message,
                Path.Combine(dataRoot, "imports", "uploads", validationId));

            blockers.Add(message);

            return EmptySourceSummary(
                uploadedZipFound: false);
        }

        var zipInfo = new FileInfo(uploadedZipPath);

        AddCheck(
            checks,
            "production-restore.source.uploaded-zip-present",
            "source",
            "info",
            true,
            "satisfied",
            "Uploaded export ZIP was found.",
            uploadedZipPath);

        try
        {
            using var archive = ZipFile.OpenRead(uploadedZipPath);

            var manifest = await ReadManifestAsync(
                archive,
                ct);

            var manifestPresent = manifest is not null;

            AddCheck(
                checks,
                "production-restore.source.manifest-present",
                "source",
                manifestPresent ? "info" : "blocker",
                manifestPresent,
                manifestPresent ? "satisfied" : "blocked",
                manifestPresent
                    ? "Export manifest was parsed."
                    : "Export manifest is missing or could not be parsed.",
                ManifestPath);

            if (!manifestPresent)
            {
                blockers.Add("Export manifest is missing or could not be parsed.");
            }

            var databaseDumpPresent = ArchiveEntryExists(
                archive,
                ZipDatabaseDumpPath);

            AddCheck(
                checks,
                "production-restore.source.database-dump-present",
                "source",
                databaseDumpPresent ? "info" : "blocker",
                databaseDumpPresent,
                databaseDumpPresent ? "satisfied" : "blocked",
                databaseDumpPresent
                    ? "Synapse SQL dump is present in the export ZIP."
                    : "Synapse SQL dump is missing from the export ZIP.",
                ZipDatabaseDumpPath);

            if (!databaseDumpPresent)
            {
                blockers.Add("Synapse SQL dump is missing from the export ZIP.");
            }

            var homeserverConfigPresent = ArchiveEntryExists(
                archive,
                ZipHomeserverPath);

            AddCheck(
                checks,
                "production-restore.source.homeserver-config-present",
                "source",
                homeserverConfigPresent ? "info" : "blocker",
                homeserverConfigPresent,
                homeserverConfigPresent ? "satisfied" : "blocked",
                homeserverConfigPresent
                    ? "homeserver.yaml is present in the export ZIP."
                    : "homeserver.yaml is missing from the export ZIP.",
                ZipHomeserverPath);

            if (!homeserverConfigPresent)
            {
                blockers.Add("homeserver.yaml is missing from the export ZIP.");
            }

            var signingKeyPresent = ArchiveEntryExists(
                archive,
                ZipSigningKeyPath);

            AddCheck(
                checks,
                "production-restore.source.signing-key-present",
                "source",
                signingKeyPresent ? "info" : "blocker",
                signingKeyPresent,
                signingKeyPresent ? "satisfied" : "blocked",
                signingKeyPresent
                    ? "Matrix signing key is present in the export ZIP."
                    : "Matrix signing key is missing from the export ZIP.",
                ZipSigningKeyPath);

            if (!signingKeyPresent)
            {
                blockers.Add("Matrix signing key is missing from the export ZIP.");
            }

            var mediaEntries = archive.Entries
                .Where(entry =>
                    entry.FullName.StartsWith(ZipMediaStorePrefix, StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .ToList();

            var mediaFiles = mediaEntries.Count;
            var mediaBytes = mediaEntries.Sum(entry => entry.Length);

            var mediaStorePresent = archive.Entries.Any(entry =>
                entry.FullName.StartsWith(ZipMediaStorePrefix, StringComparison.OrdinalIgnoreCase));

            var elementConfigPresent = ArchiveEntryExists(
                archive,
                ZipElementConfigPath);

            if (!elementConfigPresent)
            {
                warnings.Add("Element config.json is not present in the export ZIP. Production Matrix restore may still be possible, but Element route/config work may need manual review.");
            }

            return new RuntimeStackBackupProductionRestoreSourceSummary(
                UploadedZipFound: true,
                UploadedZipPath: uploadedZipPath,
                UploadedZipName: Path.GetFileName(uploadedZipPath),
                UploadedZipSizeBytes: zipInfo.Length,
                ManifestPresent: manifestPresent,
                ManifestVersion: manifest?.ManifestVersion ?? 0,
                ExportKind: manifest?.ExportKind,
                CreatedAtUtc: manifest?.CreatedAtUtc,
                CreatedBy: manifest?.CreatedBy,
                MemVersion: manifest?.MemVersion,
                SourceStackSlug: manifest?.Stack?.Slug,
                SourceDisplayName: manifest?.Stack?.DisplayName,
                SourceMatrixServerName: manifest?.Stack?.MatrixServerName,
                MatrixPublicUrl: manifest?.Stack?.MatrixPublicUrl,
                ElementPublicUrl: manifest?.Stack?.ElementPublicUrl,
                ManifestMatrixHost: manifest?.Routes?.MatrixHost,
                ManifestElementHost: manifest?.Routes?.ElementHost,
                DatabaseDumpPresent: databaseDumpPresent,
                DatabaseDumpPath: ZipDatabaseDumpPath,
                HomeserverConfigPresent: homeserverConfigPresent,
                HomeserverConfigPath: ZipHomeserverPath,
                SigningKeyPresent: signingKeyPresent,
                SigningKeyPath: ZipSigningKeyPath,
                MediaStorePresent: mediaStorePresent,
                MediaFiles: mediaFiles,
                MediaBytes: mediaBytes,
                ElementConfigPresent: elementConfigPresent,
                ElementConfigPath: elementConfigPresent ? ZipElementConfigPath : null,
                ManifestWarnings: manifest?.Warnings ?? []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"Could not inspect uploaded export ZIP: {ex.Message}");

            AddCheck(
                checks,
                "production-restore.source.zip-readable",
                "source",
                "blocker",
                false,
                "failed",
                "Uploaded export ZIP could not be inspected.",
                ex.Message);

            return EmptySourceSummary(
                uploadedZipFound: true,
                uploadedZipPath: uploadedZipPath,
                uploadedZipSizeBytes: zipInfo.Length);
        }
    }

    private async Task<RuntimeStackBackupProductionRestoreStagingEvidenceSummary> DiscoverStagingEvidenceAsync(
        string? stagingId,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> blockers,
        List<string> warnings,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stagingId))
        {
            const string message = "A selected private staging run is required before production cutover planning.";

            AddCheck(
                checks,
                "production-restore.staging.id-provided",
                "staging",
                "blocker",
                false,
                "blocked",
                message,
                null);

            blockers.Add(message);

            return EmptyStagingEvidence(
                stagingIdProvided: false,
                stagingId: null,
                detail: message);
        }

        AddCheck(
            checks,
            "production-restore.staging.id-provided",
            "staging",
            "info",
            true,
            "satisfied",
            "A private staging run was selected.",
            stagingId);

        var stagingResponse = await _stagingHistory.GetRunAsync(
            stagingId,
            ct);

        var run = stagingResponse?.Run;

        if (run is null)
        {
            const string message = "Selected private staging run was not found.";

            AddCheck(
                checks,
                "production-restore.staging.run-present",
                "staging",
                "blocker",
                false,
                "blocked",
                message,
                stagingId);

            blockers.Add(message);

            return EmptyStagingEvidence(
                stagingIdProvided: true,
                stagingId: stagingId,
                detail: message);
        }

        AddCheck(
            checks,
            "production-restore.staging.run-present",
            "staging",
            "info",
            true,
            "satisfied",
            "Selected private staging run was found.",
            run.StagingId);

        var destroyed = run.Destroy is not null ||
            string.Equals(run.Status, "destroyed", StringComparison.OrdinalIgnoreCase);

        var wasReady =
            string.Equals(run.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
            destroyed && run.Database.ImportSucceeded && run.Runtime.SynapseHealthPassed;

        AddCheck(
            checks,
            "production-restore.staging.ready",
            "staging",
            wasReady ? "info" : "blocker",
            wasReady,
            wasReady ? "satisfied" : "blocked",
            wasReady
                ? "Selected staging run reached a usable ready state."
                : "Selected staging run has not proven a usable ready state.",
            run.Status);

        if (!wasReady)
        {
            blockers.Add("Selected staging run has not proven a usable ready state.");
        }

        AddCheck(
            checks,
            "production-restore.staging.database-import",
            "staging",
            run.Database.ImportSucceeded ? "info" : "blocker",
            run.Database.ImportSucceeded,
            run.Database.ImportSucceeded ? "satisfied" : "blocked",
            run.Database.ImportSucceeded
                ? "Private staging database import succeeded."
                : "Private staging database import did not succeed.",
            $"publicTables={run.Database.PublicTableCount}; knownSynapseTables={run.Database.SynapseKnownTableCount}");

        if (!run.Database.ImportSucceeded)
        {
            blockers.Add("Private staging database import did not succeed.");
        }

        AddCheck(
            checks,
            "production-restore.staging.synapse-health",
            "staging",
            run.Runtime.SynapseHealthPassed ? "info" : "blocker",
            run.Runtime.SynapseHealthPassed,
            run.Runtime.SynapseHealthPassed ? "satisfied" : "blocked",
            run.Runtime.SynapseHealthPassed
                ? "Private staging Synapse health check passed."
                : "Private staging Synapse health check did not pass.",
            run.Runtime.HealthResponse);

        if (!run.Runtime.SynapseHealthPassed)
        {
            blockers.Add("Private staging Synapse health check did not pass.");
        }

        var privateSafetyPassed =
            run.Safety.PrivateOnly &&
            run.Safety.DockerNetworkInternal &&
            !run.Safety.PublicRoutesCreated &&
            !run.Safety.DnsChanged &&
            !run.Safety.CertificatesChanged &&
            !run.Safety.ProductionContainersTouched &&
            !run.Safety.ProductionDatabasesTouched;

        AddCheck(
            checks,
            "production-restore.staging.private-only",
            "staging",
            privateSafetyPassed ? "info" : "blocker",
            privateSafetyPassed,
            privateSafetyPassed ? "satisfied" : "blocked",
            privateSafetyPassed
                ? "Private staging safety boundary was preserved."
                : "Private staging safety boundary was not preserved.",
            null);

        if (!privateSafetyPassed)
        {
            blockers.Add("Private staging safety boundary was not preserved.");
        }

        if (run.Errors.Count > 0)
        {
            blockers.Add("Selected staging run contains errors.");
        }

        if (run.Warnings.Count > 0)
        {
            warnings.Add("Selected staging run contains warnings. Review before future production execution.");
        }

        var accepted =
            wasReady &&
            run.Database.ImportSucceeded &&
            run.Runtime.SynapseHealthPassed &&
            privateSafetyPassed &&
            run.Errors.Count == 0;

        return new RuntimeStackBackupProductionRestoreStagingEvidenceSummary(
            StagingIdProvided: true,
            StagingId: run.StagingId,
            RunFound: true,
            Status: run.Status,
            Mode: run.Mode,
            StartedAtUtc: run.StartedAtUtc,
            FinishedAtUtc: run.FinishedAtUtc,
            TargetStackSlug: run.TargetStackSlug,
            MatrixServerName: run.MatrixServerName,
            WasReady: wasReady,
            Destroyed: destroyed,
            AcceptedAsEvidence: accepted,
            DatabaseImportSucceeded: run.Database.ImportSucceeded,
            SynapseHealthPassed: run.Runtime.SynapseHealthPassed,
            PrivateOnly: run.Safety.PrivateOnly,
            InternalDockerNetwork: run.Safety.DockerNetworkInternal,
            PublicRoutesCreated: run.Safety.PublicRoutesCreated,
            DnsChanged: run.Safety.DnsChanged,
            CertificatesChanged: run.Safety.CertificatesChanged,
            ProductionContainersTouched: run.Safety.ProductionContainersTouched,
            ProductionDatabasesTouched: run.Safety.ProductionDatabasesTouched,
            WarningCount: run.Warnings.Count,
            ErrorCount: run.Errors.Count,
            Detail: accepted
                ? "Selected private staging run is acceptable as production planning evidence."
                : "Selected private staging run is not acceptable as production planning evidence.");
    }

    private async Task<RuntimeStackBackupProductionRestoreTargetSummary> DiscoverTargetAsync(
        string targetStackSlug,
        string restoreMode,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> warnings,
        CancellationToken ct)
    {
        var stack = await _db.RuntimeStacks
            .AsNoTracking()
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .FirstOrDefaultAsync(
                x => x.Slug == targetStackSlug,
                ct);

        if (stack is null)
        {
            var recreateProductionModeRequested =
                string.Equals(restoreMode, "recreate-production", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(restoreMode, "new-production-from-backup", StringComparison.OrdinalIgnoreCase);

            if (recreateProductionModeRequested)
            {
                AddCheck(
                    checks,
                    "production-restore.runtime.no-existing-target-for-recreate",
                    "runtime-target",
                    "info",
                    true,
                    "satisfied",
                    "No existing target runtime stack was found, which is valid for recreate-production mode.",
                    targetStackSlug);
            }
            else
            {
                warnings.Add($"Target runtime stack '{targetStackSlug}' was not found. Replacement execution cannot be planned fully until target ownership is resolved.");

                AddCheck(
                    checks,
                    "production-restore.runtime.target-stack-discovered",
                    "runtime-target",
                    "warning",
                    false,
                    "requires_confirmation",
                    "Target runtime stack was not found.",
                    targetStackSlug);
            }

            return new RuntimeStackBackupProductionRestoreTargetSummary(
                        TargetStackSlug: targetStackSlug,
                        StackFound: false,
                        StackId: null,
                        DisplayName: null,
                        Status: null,
                        LastVerifiedStatus: null,
                        LastVerifiedAtUtc: null,
                        BaseDomain: null,
                        DomainId: null,
                        ActiveCertificateId: null,
                        ActiveNpmCertificateId: null,
                        RuntimeNetworkName: null,
                        DataRoot: null,
                        ManifestPath: null,
                        MatrixPublicBaseUrl: null,
                        ElementPublicBaseUrl: null,
                        MatrixService: null,
                        ElementService: null,
                        Database: null,
                        Routes: []);
        }

        AddCheck(
            checks,
            "production-restore.runtime.target-stack-discovered",
            "runtime-target",
            "info",
            true,
            "satisfied",
            "Target runtime stack was discovered.",
            stack.Slug);

        var database = await _db.RuntimeStackDatabases
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.RuntimeStackId == stack.Id,
                ct);

        var matrixService = stack.ServiceInstances
            .FirstOrDefault(x => string.Equals(x.ServiceKey, "matrix", StringComparison.OrdinalIgnoreCase));

        var elementService = stack.ServiceInstances
            .FirstOrDefault(x =>
                string.Equals(x.ServiceKey, "element-web", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.ServiceKey, "element", StringComparison.OrdinalIgnoreCase));

        return new RuntimeStackBackupProductionRestoreTargetSummary(
            TargetStackSlug: targetStackSlug,
            StackFound: true,
            StackId: stack.Id,
            DisplayName: stack.DisplayName,
            Status: stack.Status,
            LastVerifiedStatus: stack.LastVerifiedStatus,
            LastVerifiedAtUtc: ToUtcOffset(stack.LastVerifiedAtUtc),
            BaseDomain: stack.BaseDomain,
            DomainId: stack.DomainId,
            ActiveCertificateId: stack.ActiveCertificateId,
            ActiveNpmCertificateId: stack.ActiveNpmCertificateId,
            RuntimeNetworkName: stack.RuntimeNetworkName,
            DataRoot: stack.DataRoot,
            ManifestPath: stack.ManifestPath,
            MatrixPublicBaseUrl: stack.MatrixPublicBaseUrl,
            ElementPublicBaseUrl: stack.ElementPublicBaseUrl,
            MatrixService: matrixService is null ? null : ToServiceTarget(matrixService),
            ElementService: elementService is null ? null : ToServiceTarget(elementService),
            Database: database is null ? null : ToDatabaseTarget(database),
            Routes: stack.Routes
                .OrderBy(x => x.PublicHost)
                .Select(ToRouteTarget)
                .ToList());
    }

    private async Task<RuntimeStackBackupProductionRestoreMatrixIdentitySummary> DiscoverMatrixIdentityAsync(
        RuntimeStackBackupProductionRestoreSourceSummary source,
        RuntimeStackBackupProductionRestoreTargetSummary target,
        RuntimeStackBackupProductionRestoreStagingEvidenceSummary staging,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> blockers,
        List<string> warnings,
        CancellationToken ct)
    {
        var sourceServerName = FirstNonEmpty(
            source.SourceMatrixServerName,
            staging.MatrixServerName);

        var targetServerName = FirstNonEmpty(
            target.MatrixService?.ServerName,
            ExtractHost(target.MatrixPublicBaseUrl),
            sourceServerName);

        var sameServerName =
            !string.IsNullOrWhiteSpace(sourceServerName) &&
            !string.IsNullOrWhiteSpace(targetServerName) &&
            string.Equals(sourceServerName, targetServerName, StringComparison.OrdinalIgnoreCase);

        AddCheck(
            checks,
            "production-restore.matrix.server-name-known",
            "matrix-identity",
            string.IsNullOrWhiteSpace(sourceServerName) ? "blocker" : "info",
            !string.IsNullOrWhiteSpace(sourceServerName),
            string.IsNullOrWhiteSpace(sourceServerName) ? "blocked" : "satisfied",
            string.IsNullOrWhiteSpace(sourceServerName)
                ? "Source Matrix server_name could not be determined."
                : $"Source Matrix server_name is {sourceServerName}.",
            sourceServerName);

        if (string.IsNullOrWhiteSpace(sourceServerName))
        {
            blockers.Add("Source Matrix server_name could not be determined.");
        }

        AddCheck(
            checks,
            "production-restore.matrix.signing-key-present",
            "matrix-identity",
            source.SigningKeyPresent ? "info" : "blocker",
            source.SigningKeyPresent,
            source.SigningKeyPresent ? "satisfied" : "blocked",
            source.SigningKeyPresent
                ? "Matrix signing key is present."
                : "Matrix signing key is missing.",
            source.SigningKeyPath);

        if (!source.SigningKeyPresent)
        {
            blockers.Add("Matrix signing key is missing.");
        }

        var otherStacks = new List<string>();

        if (!string.IsNullOrWhiteSpace(sourceServerName))
        {
            otherStacks = await _db.RuntimeServiceInstances
                .AsNoTracking()
                .Include(x => x.RuntimeStack)
                .Where(x =>
                    x.ServerName == sourceServerName &&
                    x.RuntimeStack.Slug != target.TargetStackSlug)
                .Select(x => x.RuntimeStack.Slug)
                .Distinct()
                .ToListAsync(ct);
        }

        var duplicateRisk = otherStacks.Count > 0;

        if (duplicateRisk)
        {
            warnings.Add("Another MEM runtime stack appears to use the same Matrix server_name. This must be resolved before public production cutover.");
        }

        AddCheck(
            checks,
            "production-restore.matrix.no-duplicate-public-server",
            "matrix-identity",
            duplicateRisk ? "warning" : "info",
            !duplicateRisk,
            duplicateRisk ? "requires_confirmation" : "satisfied",
            duplicateRisk
                ? "Another MEM runtime stack appears to use the same Matrix server_name."
                : "No other MEM runtime stack with the same Matrix server_name was discovered.",
            duplicateRisk ? string.Join(", ", otherStacks) : null);

        return new RuntimeStackBackupProductionRestoreMatrixIdentitySummary(
            SourceServerName: sourceServerName,
            TargetServerName: targetServerName,
            SameServerName: sameServerName,
            SigningKeyPresent: source.SigningKeyPresent,
            SigningKeyReuseLikely: source.SigningKeyPresent && sameServerName,
            OldPublicServerStopRequired: sameServerName,
            DuplicatePublicServerRisk: duplicateRisk,
            OtherStacksWithSameServerName: otherStacks,
            Notes:
            [
                "Matrix server_name and signing keys are identity material.",
                "A same-name production restore must not run publicly beside the old homeserver.",
                "Old public homeserver stop and no-duplicate-public-server confirmations are required before any future execution."
            ]);
    }

    private async Task<RuntimeStackBackupProductionRestoreDnsSummary> DiscoverDnsAsync(
        string? matrixHost,
        string? elementHost,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> warnings,
        CancellationToken ct)
    {
        var domains = await _db.Domains
            .AsNoTracking()
            .ToListAsync(ct);

        var orderedDomains = domains
            .OrderByDescending(x => x.BaseDomain.Length)
            .ToList();

        var matrixDomain = MatchDomain(
            matrixHost,
            orderedDomains);

        var elementDomain = MatchDomain(
            elementHost,
            orderedDomains);

        AddCheck(
            checks,
            "production-restore.dns.matrix-domain-known",
            "dns",
            matrixDomain is null ? "warning" : "info",
            matrixDomain is not null,
            matrixDomain is null ? "requires_confirmation" : "satisfied",
            matrixDomain is null
                ? "Intended Matrix host does not match a known MEM domain registry entry."
                : "Intended Matrix host matches a known MEM domain registry entry.",
            matrixHost);

        AddCheck(
            checks,
            "production-restore.dns.element-domain-known",
            "dns",
            elementDomain is null ? "warning" : "info",
            elementDomain is not null,
            elementDomain is null ? "requires_confirmation" : "satisfied",
            elementDomain is null
                ? "Intended Element host does not match a known MEM domain registry entry."
                : "Intended Element host matches a known MEM domain registry entry.",
            elementHost);

        if (!string.IsNullOrWhiteSpace(matrixHost) && matrixDomain is null)
        {
            warnings.Add($"Matrix host '{matrixHost}' is not mapped to a known MEM domain record.");
        }

        if (!string.IsNullOrWhiteSpace(elementHost) && elementDomain is null)
        {
            warnings.Add($"Element host '{elementHost}' is not mapped to a known MEM domain record.");
        }

        return new RuntimeStackBackupProductionRestoreDnsSummary(
            IntendedMatrixHost: matrixHost,
            IntendedElementHost: elementHost,
            MatrixHostProvided: !string.IsNullOrWhiteSpace(matrixHost),
            ElementHostProvided: !string.IsNullOrWhiteSpace(elementHost),
            MatrixDomain: matrixDomain,
            ElementDomain: elementDomain,
            DnsMutationRequiredLater: true,
            DnsChanged: false,
            Notes:
            [
                "Production Restore v1A does not query or mutate live DNS.",
                "Domain registry matching is read-only.",
                "DNS ownership must be confirmed before any future public cutover."
            ]);
    }

    private async Task<RuntimeStackBackupProductionRestoreCertificateSummary> DiscoverCertificatesAsync(
        string? matrixHost,
        string? elementHost,
        RuntimeStackBackupProductionRestoreDnsSummary dns,
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        List<string> warnings,
        CancellationToken ct)
    {
        var certificates = await _db.Certificates
            .AsNoTracking()
            .Include(x => x.Domain)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ExpiresAtUtc)
            .ToListAsync(ct);

        var candidates = certificates
            .Select(ToCertificateMatch)
            .ToList();

        var matrixCertificate = MatchCertificate(
            matrixHost,
            certificates,
            dns.MatrixDomain);

        var elementCertificate = MatchCertificate(
            elementHost,
            certificates,
            dns.ElementDomain);

        AddCheck(
            checks,
            "production-restore.certificate.matrix-available",
            "certificate",
            matrixCertificate is null ? "warning" : "info",
            matrixCertificate is not null,
            matrixCertificate is null ? "requires_confirmation" : "satisfied",
            matrixCertificate is null
                ? "No active matching certificate was found for the intended Matrix host."
                : "A matching certificate was found for the intended Matrix host.",
            matrixCertificate?.CommonName ?? matrixHost);

        AddCheck(
            checks,
            "production-restore.certificate.element-available",
            "certificate",
            elementCertificate is null ? "warning" : "info",
            elementCertificate is not null,
            elementCertificate is null ? "requires_confirmation" : "satisfied",
            elementCertificate is null
                ? "No active matching certificate was found for the intended Element host."
                : "A matching certificate was found for the intended Element host.",
            elementCertificate?.CommonName ?? elementHost);

        if (matrixCertificate is null && !string.IsNullOrWhiteSpace(matrixHost))
        {
            warnings.Add($"No active matching certificate was found for Matrix host '{matrixHost}'.");
        }

        if (elementCertificate is null && !string.IsNullOrWhiteSpace(elementHost))
        {
            warnings.Add($"No active matching certificate was found for Element host '{elementHost}'.");
        }

        return new RuntimeStackBackupProductionRestoreCertificateSummary(
            CertificatesChanged: false,
            MatrixCertificateAvailable: matrixCertificate is not null,
            ElementCertificateAvailable: elementCertificate is not null,
            MatrixCertificate: matrixCertificate,
            ElementCertificate: elementCertificate,
            CandidateCertificates: candidates,
            Notes:
            [
                "Production Restore v1A does not request, renew, import, attach, or remove certificates.",
                "Certificate readiness is based on MEM certificate registry records only.",
                "A future executor must verify the selected certificate is valid and imported to NPM before public route attach."
            ]);
    }

    private async Task<RuntimeStackBackupProductionRestoreNpmSummary> DiscoverNpmRoutesAsync(
    string? matrixHost,
    string? elementHost,
    RuntimeStackBackupProductionRestoreTargetSummary target,
    List<RuntimeStackBackupProductionRestoreCheck> checks,
    List<string> warnings,
    CancellationToken ct)
    {
        var matrixRoute = MatchRuntimeRoute(
            matrixHost,
            target.Routes);

        var elementRoute = MatchRuntimeRoute(
            elementHost,
            target.Routes);

        if (matrixRoute is null)
        {
            matrixRoute = await TryGetLiveNpmRouteAsync(
                matrixHost,
                "matrix",
                warnings,
                ct);
        }

        if (elementRoute is null)
        {
            elementRoute = await TryGetLiveNpmRouteAsync(
                elementHost,
                "element-web",
                warnings,
                ct);
        }

        AddCheck(
            checks,
            "production-restore.npm.matrix-route-discovered",
            "npm-route",
            matrixRoute is null ? "warning" : "info",
            matrixRoute is not null,
            matrixRoute is null ? "requires_confirmation" : "satisfied",
            matrixRoute is null
                ? "No Matrix route was found in the MEM runtime route registry or live NPM."
                : matrixRoute.FoundInRuntimeRouteRegistry
                    ? "Matrix route was found in the MEM runtime route registry."
                    : "Matrix route was found in live NPM by hostname.",
            matrixHost);

        AddCheck(
            checks,
            "production-restore.npm.element-route-discovered",
            "npm-route",
            elementRoute is null ? "warning" : "info",
            elementRoute is not null,
            elementRoute is null ? "requires_confirmation" : "satisfied",
            elementRoute is null
                ? "No Element route was found in the MEM runtime route registry or live NPM."
                : elementRoute.FoundInRuntimeRouteRegistry
                    ? "Element route was found in the MEM runtime route registry."
                    : "Element route was found in live NPM by hostname.",
            elementHost);

        if (matrixRoute is null && !string.IsNullOrWhiteSpace(matrixHost))
        {
            warnings.Add($"No Matrix route was found in the MEM runtime route registry or live NPM for host '{matrixHost}'.");
        }

        if (elementRoute is null && !string.IsNullOrWhiteSpace(elementHost))
        {
            warnings.Add($"No Element route was found in the MEM runtime route registry or live NPM for host '{elementHost}'.");
        }

        return new RuntimeStackBackupProductionRestoreNpmSummary(
            NpmRoutesChanged: false,
            MatrixRouteDiscovered: matrixRoute is not null,
            ElementRouteDiscovered: elementRoute is not null,
            MatrixRoute: matrixRoute,
            ElementRoute: elementRoute,
            Notes:
            [
                "Production Restore v1A does not call NPM create/update/delete APIs.",
            "NPM discovery first checks MEM runtime route registry records.",
            "If no MEM runtime route exists, NPM discovery performs a read-only live lookup by hostname.",
            "Route ownership must be confirmed before any future public cutover."
            ]);
    }

    private async Task<RuntimeStackBackupProductionRestoreNpmRouteSummary?> TryGetLiveNpmRouteAsync(
    string? host,
    string serviceKey,
    List<string> warnings,
    CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        try
        {
            var liveRoute = await _npmProxyHostService.GetByDomainAsync(
                host,
                ct);

            if (liveRoute is null)
            {
                return null;
            }

            return ToLiveNpmRouteSummary(
                liveRoute,
                host,
                serviceKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Live NPM route lookup failed for host '{host}': {ex.Message}");
            return null;
        }
    }

    private static RuntimeStackBackupProductionRestoreNpmRouteSummary ToLiveNpmRouteSummary(
        NpmProxyHost host,
        string requestedHost,
        string serviceKey)
    {
        var matchedHost =
            host.domain_names?.FirstOrDefault(domain =>
                string.Equals(
                    NormalizeHost(domain ?? string.Empty),
                    NormalizeHost(requestedHost),
                    StringComparison.OrdinalIgnoreCase)) ??
            requestedHost;

        var status = host.enabled == true
            ? "enabled"
            : "disabled";

        if (host.meta?.nginx_online == true)
        {
            status = $"{status}/nginx-online";
        }
        else if (host.meta?.nginx_online == false)
        {
            status = $"{status}/nginx-offline";
        }

        return new RuntimeStackBackupProductionRestoreNpmRouteSummary(
            FoundInRuntimeRouteRegistry: false,
            Host: matchedHost,
            ServiceKey: serviceKey,
            Provider: "npm-live",
            RouteKind: "proxy-host",
            ForwardScheme: host.forward_scheme,
            ForwardHost: host.forward_host,
            ForwardPort: host.forward_port,
            ProviderRouteId: host.id.ToString(),
            CertificateId: null,
            NpmCertificateId: host.certificate_id,
            SslConfigured: host.ssl_forced,
            ForceSsl: host.ssl_forced,
            Http2: host.http2_support,
            Status: status,
            LastError: host.meta?.nginx_err);
    }

    private static RuntimeStackBackupProductionRestoreReplacementSummary BuildReplacementSummary(
    string restoreMode,
    RuntimeStackBackupProductionRestoreTargetSummary target,
    RuntimeStackBackupProductionRestoreMatrixIdentitySummary matrixIdentity,
    List<RuntimeStackBackupProductionRestoreCheck> checks,
    List<string> blockers,
    List<string> warnings)
    {
        var replacementModeRequested = string.Equals(
            restoreMode,
            "replacement",
            StringComparison.OrdinalIgnoreCase);

        var recreateProductionModeRequested =
            string.Equals(restoreMode, "recreate-production", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(restoreMode, "new-production-from-backup", StringComparison.OrdinalIgnoreCase);

        var recognisedMode =
            replacementModeRequested ||
            recreateProductionModeRequested;

        AddCheck(
            checks,
            "production-restore.mode.recognised",
            "replacement",
            recognisedMode ? "info" : "blocker",
            recognisedMode,
            recognisedMode ? "satisfied" : "blocked",
            recognisedMode
                ? $"Production restore mode '{restoreMode}' is recognised."
                : $"Production restore mode '{restoreMode}' is not recognised.",
            restoreMode);

        if (!recognisedMode)
        {
            blockers.Add($"Production restore mode '{restoreMode}' is not recognised.");
        }

        if (replacementModeRequested)
        {
            AddCheck(
                checks,
                "production-restore.replacement.mode",
                "replacement",
                "info",
                true,
                "satisfied",
                "Replacement restore mode is selected.",
                restoreMode);

            if (!target.StackFound)
            {
                const string message = "Replacement target runtime stack was not found.";

                AddCheck(
                    checks,
                    "production-restore.replacement.target-stack-required",
                    "replacement",
                    "blocker",
                    false,
                    "blocked",
                    message,
                    target.TargetStackSlug);

                blockers.Add(message);
            }
            else
            {
                AddCheck(
                    checks,
                    "production-restore.replacement.target-stack-required",
                    "replacement",
                    "info",
                    true,
                    "satisfied",
                    "Replacement target runtime stack was found.",
                    target.TargetStackSlug);
            }
        }

        if (recreateProductionModeRequested)
        {
            AddCheck(
                checks,
                "production-restore.recreate-production.mode",
                "replacement",
                "info",
                true,
                "satisfied",
                "Recreate-production restore mode is selected.",
                restoreMode);

            if (!target.StackFound)
            {
                AddCheck(
                    checks,
                    "production-restore.recreate-production.no-existing-target",
                    "replacement",
                    "info",
                    true,
                    "satisfied",
                    "No existing target runtime stack was found, which is valid for recreate-production mode.",
                    target.TargetStackSlug);
            }
            else
            {
                AddCheck(
                    checks,
                    "production-restore.recreate-production.existing-target-detected",
                    "replacement",
                    "warning",
                    false,
                    "requires_confirmation",
                    "An existing target runtime stack was found. Recreate-production may collide with an existing stack unless a new slug/runtime identity is chosen.",
                    target.TargetStackSlug);

                warnings.Add("An existing target runtime stack was found while using recreate-production mode. Confirm this is intentional or choose a new target slug.");
            }
        }

        AddCheck(
            checks,
            "production-restore.rollback.pre-cutover-backup-required",
            "rollback",
            recreateProductionModeRequested && !target.StackFound ? "warning" : "blocker",
            false,
            "requires_confirmation",
            recreateProductionModeRequested && !target.StackFound
                ? "No existing registered target stack was found, but operator should still confirm there is no old runtime or external state to preserve."
                : "A final pre-cutover backup is required before any future production execution.",
            null);

        var requiredBeforeExecution = new List<string>
    {
        "Confirm selected backup source.",
        "Confirm selected private staging evidence.",
        "Confirm Matrix server_name and signing key risk.",
        "Confirm DNS ownership and route ownership.",
        "Confirm selected certificate and NPM route attachment strategy.",
        "Confirm rollback material and retention policy."
    };

        if (replacementModeRequested)
        {
            requiredBeforeExecution.Add("Generate and verify a final pre-cutover backup of the current production target.");
            requiredBeforeExecution.Add("Confirm the old public homeserver stop window.");
            requiredBeforeExecution.Add("Confirm no duplicate public homeserver is online with the same server_name/signing key.");

            if (!target.StackFound)
            {
                requiredBeforeExecution.Add("Select a registered MEM runtime stack before replacement execution.");
            }
        }

        if (recreateProductionModeRequested)
        {
            requiredBeforeExecution.Add("Create a new restored production runtime candidate from the selected backup.");
            requiredBeforeExecution.Add("Verify the recreated production candidate privately before public route attach.");
            requiredBeforeExecution.Add("Confirm no old public homeserver is still online for the same server_name/signing key.");
        }

        if (matrixIdentity.OldPublicServerStopRequired)
        {
            requiredBeforeExecution.Add("Stop the old public homeserver before same-name public cutover, if one still exists outside MEM.");
        }

        return new RuntimeStackBackupProductionRestoreReplacementSummary(
            TargetStackExists: target.StackFound,
            ReplacementModeRequested: replacementModeRequested,
            PreCutoverBackupRequired: replacementModeRequested || target.StackFound,
            OldStackStopRequired: matrixIdentity.OldPublicServerStopRequired,
            RollbackPlanRequired: true,
            SafeToExecuteNow: false,
            RequiredBeforeExecution: requiredBeforeExecution,
            Notes: recreateProductionModeRequested
                ?
                [
                    "Recreate-production mode is valid when no registered target runtime stack exists.",
                "This mode means MEM will later create a new production runtime from the backup.",
                "Production execution is still not available in v1A.",
                "This plan is intentionally read-only."
                ]
                :
                [
                    "Replacement mode expects an existing registered runtime stack.",
                "Replacement execution is not available in v1A.",
                "This plan is intentionally read-only.",
                "A future executor must be implemented as a separate gated sprint."
                ]);
    }

    private static void AddProductionConfirmations(
        List<RuntimeStackBackupProductionRestoreConfirmation> confirmations)
    {
        confirmations.AddRange(
        [
            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.source.correct",
                Label: "Correct backup source selected",
                Description: "Confirm this validation/import source is the backup intended for production restore.",
                Severity: "blocker",
                RequiredForPlan: true,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.staging.evidence-selected",
                Label: "Staging evidence selected",
                Description: "Confirm the selected private staging run is the restore evidence to promote toward production.",
                Severity: "blocker",
                RequiredForPlan: true,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.matrix.signing-key-risk",
                Label: "Matrix signing key risk acknowledged",
                Description: "Confirm the operator understands Matrix signing keys are identity material.",
                Severity: "blocker",
                RequiredForPlan: true,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.matrix.old-server-stopped-before-cutover",
                Label: "Old public homeserver will be stopped",
                Description: "Confirm the old public homeserver for the same server_name will be stopped before public cutover.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.matrix.no-duplicate-public-server",
                Label: "No duplicate public server_name/signing key",
                Description: "Confirm no other public homeserver is online with the same Matrix server_name and signing key.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.runtime.replacement-intended",
                Label: "Replacement behavior intended",
                Description: "Confirm production replacement is intended and the target runtime ownership is understood.",
                Severity: "blocker",
                RequiredForPlan: true,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.runtime.pre-cutover-backup-required",
                Label: "Pre-cutover backup required",
                Description: "Confirm a final backup/snapshot of the existing production target is required before execution.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.runtime.rollback-understood",
                Label: "Rollback path understood",
                Description: "Confirm the rollback material and retention policy are understood before execution.",
                Severity: "warning",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.dns.ownership-confirmed",
                Label: "DNS ownership confirmed",
                Description: "Confirm DNS ownership and the intended public hostnames before any public route cutover.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.npm.route-ownership-confirmed",
                Label: "NPM route ownership confirmed",
                Description: "Confirm NPM route ownership and the intended upstream targets before route mutation.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.certificate.selected-or-issuance-understood",
                Label: "Certificate selected or issuance understood",
                Description: "Confirm the certificate selection/import/issuance path is understood before public cutover.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null),

            new RuntimeStackBackupProductionRestoreConfirmation(
                Code: "production.cutover.manual-final-approval",
                Label: "Manual final cutover approval",
                Description: "Confirm the operator gives final explicit approval before future production execution.",
                Severity: "blocker",
                RequiredForPlan: false,
                RequiredForExecution: true,
                AcknowledgedAtUtc: null)
        ]);
    }

    private static void AddDryRunSteps(
        List<RuntimeStackBackupProductionRestoreStep> steps)
    {
        steps.AddRange(
        [
            new RuntimeStackBackupProductionRestoreStep(1, "confirm-staging-evidence", "Confirm selected staging evidence", "Review selected private staging run evidence.", false, true, "planned"),
            new RuntimeStackBackupProductionRestoreStep(2, "final-pre-cutover-backup", "Create final pre-cutover backup", "Create and verify a final backup/snapshot of the current production target.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(3, "confirm-stop-window", "Confirm old homeserver stop window", "Confirm the old public homeserver can be stopped before public same-name cutover.", false, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(4, "stop-old-runtime", "Stop old runtime", "Stop old Matrix/Element runtime containers.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(5, "preserve-rollback-material", "Preserve rollback material", "Record old runtime metadata and rollback references.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(6, "restore-database-runtime", "Restore database and runtime files", "Create restored production database/runtime paths from the selected backup.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(7, "start-restored-private", "Start restored runtime privately", "Start restored Postgres/Synapse/Element privately before public route attach.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(8, "verify-private-health", "Verify restored private health", "Verify restored Synapse health before public route attach.", false, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(9, "attach-matrix-route", "Attach Matrix NPM route", "Attach or update the Matrix NPM route.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(10, "attach-element-route", "Attach Element NPM route", "Attach or update the Element NPM route.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(11, "attach-certificate", "Attach selected certificate", "Attach selected certificate to public routes.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(12, "verify-public-client-api", "Verify public Matrix client API", "Verify the public Matrix client API after route attach.", false, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(13, "verify-public-element", "Verify public Element route", "Verify the public Element web route after route attach.", false, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(14, "verify-federation-readiness", "Verify federation readiness", "Verify federation readiness before declaring restore complete.", false, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(15, "mark-complete", "Mark production restore complete", "Record final restore result.", true, false, "locked"),
            new RuntimeStackBackupProductionRestoreStep(16, "retain-rollback", "Retain rollback material", "Keep rollback material until retention policy is confirmed.", false, false, "locked")
        ]);
    }

    private RuntimeStackBackupProductionRestoreSourceSummary EmptySourceSummary(
        bool uploadedZipFound,
        string? uploadedZipPath = null,
        long? uploadedZipSizeBytes = null)
    {
        return new RuntimeStackBackupProductionRestoreSourceSummary(
            UploadedZipFound: uploadedZipFound,
            UploadedZipPath: uploadedZipPath,
            UploadedZipName: uploadedZipPath is null ? null : Path.GetFileName(uploadedZipPath),
            UploadedZipSizeBytes: uploadedZipSizeBytes,
            ManifestPresent: false,
            ManifestVersion: 0,
            ExportKind: null,
            CreatedAtUtc: null,
            CreatedBy: null,
            MemVersion: null,
            SourceStackSlug: null,
            SourceDisplayName: null,
            SourceMatrixServerName: null,
            MatrixPublicUrl: null,
            ElementPublicUrl: null,
            ManifestMatrixHost: null,
            ManifestElementHost: null,
            DatabaseDumpPresent: false,
            DatabaseDumpPath: null,
            HomeserverConfigPresent: false,
            HomeserverConfigPath: null,
            SigningKeyPresent: false,
            SigningKeyPath: null,
            MediaStorePresent: false,
            MediaFiles: 0,
            MediaBytes: 0,
            ElementConfigPresent: false,
            ElementConfigPath: null,
            ManifestWarnings: []);
    }

    private static RuntimeStackBackupProductionRestoreStagingEvidenceSummary EmptyStagingEvidence(
        bool stagingIdProvided,
        string? stagingId,
        string detail)
    {
        return new RuntimeStackBackupProductionRestoreStagingEvidenceSummary(
            StagingIdProvided: stagingIdProvided,
            StagingId: stagingId,
            RunFound: false,
            Status: null,
            Mode: null,
            StartedAtUtc: null,
            FinishedAtUtc: null,
            TargetStackSlug: null,
            MatrixServerName: null,
            WasReady: false,
            Destroyed: false,
            AcceptedAsEvidence: false,
            DatabaseImportSucceeded: false,
            SynapseHealthPassed: false,
            PrivateOnly: false,
            InternalDockerNetwork: false,
            PublicRoutesCreated: false,
            DnsChanged: false,
            CertificatesChanged: false,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            WarningCount: 0,
            ErrorCount: 0,
            Detail: detail);
    }

    private async Task<MemStackExportManifestFile?> ReadManifestAsync(
        ZipArchive archive,
        CancellationToken ct)
    {
        var entry = archive.GetEntry(ManifestPath);

        if (entry is null)
        {
            return null;
        }

        await using var stream = entry.Open();

        return await JsonSerializer.DeserializeAsync<MemStackExportManifestFile>(
            stream,
            JsonOptions,
            ct);
    }

    private static bool ArchiveEntryExists(
        ZipArchive archive,
        string path)
    {
        return archive.GetEntry(path) is not null;
    }

    private string? ResolveUploadedZipPathOrNull(
        string dataRoot,
        string validationId)
    {
        var uploadDirectory = Path.Combine(
            dataRoot,
            "imports",
            "uploads",
            validationId);

        if (!Directory.Exists(uploadDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(uploadDirectory, "*.zip")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static RuntimeStackBackupProductionRestoreServiceTarget ToServiceTarget(
        RuntimeServiceInstanceEntity entity)
    {
        return new RuntimeStackBackupProductionRestoreServiceTarget(
            InstanceId: entity.InstanceId,
            ServiceKey: entity.ServiceKey,
            Role: entity.Role,
            Status: entity.Status,
            Image: entity.Image,
            Version: entity.Version,
            ContainerName: entity.ContainerName,
            ContainerId: entity.ContainerId,
            NetworkName: entity.NetworkName,
            InternalHost: entity.InternalHost,
            InternalBaseUrl: entity.InternalBaseUrl,
            PublicHost: entity.PublicHost,
            PublicBaseUrl: entity.PublicBaseUrl,
            HostPort: entity.HostPort,
            ContainerPort: entity.ContainerPort,
            DataPath: entity.DataPath,
            ConfigPath: entity.ConfigPath,
            ServerName: entity.ServerName);
    }

    private static RuntimeStackBackupProductionRestoreDatabaseTarget ToDatabaseTarget(
        RuntimeStackDatabaseEntity entity)
    {
        return new RuntimeStackBackupProductionRestoreDatabaseTarget(
            DatabaseEngine: entity.DatabaseEngine,
            DatabaseHost: entity.DatabaseHost,
            DatabasePort: entity.DatabasePort,
            DatabaseName: entity.DatabaseName,
            DatabaseUsername: entity.DatabaseUsername,
            Status: entity.Status);
    }

    private static RuntimeStackBackupProductionRestoreRouteTarget ToRouteTarget(
        RuntimeRouteEntity entity)
    {
        return new RuntimeStackBackupProductionRestoreRouteTarget(
            ServiceKey: entity.ServiceKey,
            Provider: entity.Provider,
            RouteKind: entity.RouteKind,
            IsPublic: entity.IsPublic,
            PublicHost: entity.PublicHost,
            PublicBaseUrl: entity.PublicBaseUrl,
            ForwardScheme: entity.ForwardScheme,
            ForwardHost: entity.ForwardHost,
            ForwardPort: entity.ForwardPort,
            ProviderRouteId: entity.ProviderRouteId,
            CertificateId: entity.CertificateId,
            NpmCertificateId: entity.NpmCertificateId,
            SslExpected: entity.SslExpected,
            SslConfigured: entity.SslConfigured,
            ForceSsl: entity.ForceSsl,
            Http2: entity.Http2,
            Status: entity.Status,
            LastVerifiedAtUtc: ToUtcOffset(entity.LastVerifiedAtUtc),
            LastError: entity.LastError);
    }

    private static RuntimeStackBackupProductionRestoreDomainMatch? MatchDomain(
        string? host,
        IReadOnlyList<DomainEntity> domains)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalizedHost = NormalizeHost(host);

        var domain = domains.FirstOrDefault(domain =>
        {
            var baseDomain = NormalizeHost(domain.BaseDomain);

            return string.Equals(normalizedHost, baseDomain, StringComparison.OrdinalIgnoreCase) ||
                   normalizedHost.EndsWith("." + baseDomain, StringComparison.OrdinalIgnoreCase);
        });

        return domain is null
            ? null
            : new RuntimeStackBackupProductionRestoreDomainMatch(
                DomainId: domain.Id,
                BaseDomain: domain.BaseDomain,
                DisplayName: domain.DisplayName,
                Purpose: domain.Purpose,
                DnsProvider: domain.DnsProvider,
                DnsZone: domain.DnsZone,
                Status: domain.Status,
                ActiveCertificateId: domain.ActiveCertificateId);
    }

    private static RuntimeStackBackupProductionRestoreCertificateMatch? MatchCertificate(
        string? host,
        IReadOnlyList<CertificateEntity> certificates,
        RuntimeStackBackupProductionRestoreDomainMatch? domainMatch)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalizedHost = NormalizeHost(host);

        var certificate = certificates.FirstOrDefault(cert =>
                cert.IsActive &&
                CertificateMatchesHost(cert, normalizedHost)) ??
            certificates.FirstOrDefault(cert =>
                cert.IsActive &&
                domainMatch is not null &&
                cert.DomainId == domainMatch.DomainId &&
                cert.IsWildcard &&
                cert.ImportedToNpm);

        return certificate is null
            ? null
            : ToCertificateMatch(certificate);
    }

    private static bool CertificateMatchesHost(
        CertificateEntity cert,
        string normalizedHost)
    {
        var commonName = NormalizeHost(cert.CommonName);

        if (string.Equals(commonName, normalizedHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!cert.IsWildcard || !commonName.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var wildcardBase = commonName[2..];

        return normalizedHost.EndsWith("." + wildcardBase, StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeStackBackupProductionRestoreCertificateMatch ToCertificateMatch(
        CertificateEntity cert)
    {
        return new RuntimeStackBackupProductionRestoreCertificateMatch(
            CertificateEntityId: cert.Id,
            DomainId: cert.DomainId,
            CertificateId: cert.CertificateId,
            CommonName: cert.CommonName,
            Provider: cert.Provider,
            IsWildcard: cert.IsWildcard,
            IsStaging: cert.IsStaging,
            IsActive: cert.IsActive,
            Status: cert.Status,
            ExpiresAtUtc: ToUtcOffset(cert.ExpiresAtUtc),
            FullchainPath: cert.FullchainPath,
            PrivateKeyPath: cert.PrivateKeyPath,
            NpmCertificateId: cert.NpmCertificateId,
            ImportedToNpm: cert.ImportedToNpm,
            LastImportedToNpmAtUtc: ToUtcOffset(cert.LastImportedToNpmAtUtc),
            LastError: cert.LastError);
    }

    private static RuntimeStackBackupProductionRestoreNpmRouteSummary? MatchRuntimeRoute(
        string? host,
        IReadOnlyList<RuntimeStackBackupProductionRestoreRouteTarget> routes)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalizedHost = NormalizeHost(host);

        var route = routes.FirstOrDefault(route =>
            string.Equals(
                NormalizeHost(route.PublicHost),
                normalizedHost,
                StringComparison.OrdinalIgnoreCase));

        return route is null
            ? null
            : new RuntimeStackBackupProductionRestoreNpmRouteSummary(
                FoundInRuntimeRouteRegistry: true,
                Host: route.PublicHost,
                ServiceKey: route.ServiceKey,
                Provider: route.Provider,
                RouteKind: route.RouteKind,
                ForwardScheme: route.ForwardScheme,
                ForwardHost: route.ForwardHost,
                ForwardPort: route.ForwardPort,
                ProviderRouteId: route.ProviderRouteId,
                CertificateId: route.CertificateId,
                NpmCertificateId: route.NpmCertificateId,
                SslConfigured: route.SslConfigured,
                ForceSsl: route.ForceSsl,
                Http2: route.Http2,
                Status: route.Status,
                LastError: route.LastError);
    }

    private static void AddCheck(
        List<RuntimeStackBackupProductionRestoreCheck> checks,
        string code,
        string category,
        string severity,
        bool passed,
        string status,
        string message,
        string? detail)
    {
        checks.Add(new RuntimeStackBackupProductionRestoreCheck(
            Code: code,
            Category: category,
            Severity: severity,
            Status: status,
            Passed: passed,
            Message: message,
            Detail: detail));
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? ExtractHost(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.Host
            : NormalizeHost(value);
    }

    private static string NormalizeHost(
        string value)
    {
        var normalized = value.Trim().ToLowerInvariant();

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            normalized = uri.Host;
        }

        return normalized.TrimEnd('.');
    }

    private static string Slugify(
        string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug.Trim('-'))
            ? "production-restore"
            : slug.Trim('-');
    }

    private static DateTimeOffset? ToUtcOffset(
        DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private static bool IsSafePathSegment(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return value
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "." && segment != "..");
    }

    private static string CreatePlanId()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);

        return $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private sealed class MemStackExportManifestFile
    {
        public int ManifestVersion { get; set; }
        public string? ExportKind { get; set; }
        public DateTimeOffset? CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }
        public string? MemVersion { get; set; }
        public MemStackExportStackFile? Stack { get; set; }
        public MemStackExportRoutesFile? Routes { get; set; }
        public List<string>? IncludedFiles { get; set; }
        public List<string>? Warnings { get; set; }
    }

    private sealed class MemStackExportStackFile
    {
        public string? StackId { get; set; }
        public string? Slug { get; set; }
        public string? DisplayName { get; set; }
        public string? MatrixServerName { get; set; }
        public string? MatrixPublicUrl { get; set; }
        public string? ElementPublicUrl { get; set; }
    }

    private sealed class MemStackExportRoutesFile
    {
        public string? MatrixHost { get; set; }
        public string? ElementHost { get; set; }
        public bool RequiresDns { get; set; }
    }
}