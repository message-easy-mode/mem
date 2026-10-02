using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Migrations.Qualification;

/// <summary>
/// Closes the migration production qualification programme after the accepted normal Runtime
/// Stack, first native MEM baseline backup, and distinct-host qualification have all become
/// durable. The closure report is a self-contained, hash-bound release handoff; it does not
/// delete retained source or migration evidence and it does not claim that a development
/// coexistence fixture is production qualification.
/// </summary>
public sealed class MigrationTwoServerQualificationClosureService(
    MemDbContext db,
    IMigrationTargetHostIdentityProbe targetHostIdentityProbe,
    TimeProvider timeProvider,
    ILogger<MigrationTwoServerQualificationClosureService> logger)
{
    internal const string ClosureSchemaVersion =
        "mem.migration.two-server-qualification-closure.v2";
    internal const string MemVersion = "0.2.0";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<MigrationTwoServerQualificationClosureStateResponse> GetStateAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var context = await LoadAsync(migrationId, tracking: false, cancellationToken);
        var assurance = MigrationTwoServerQualificationService.ResolveAssuranceOrNull(context.Intake);
        if (HasClosure(context.Qualification))
        {
            return BuildClosedState(context, assurance: assurance);
        }

        if (assurance is not null && MigrationAssuranceProjection.IsSimplified(assurance))
        {
            return new MigrationTwoServerQualificationClosureStateResponse(
                Source: "control-plane",
                Status: "not-applicable-simplified-assurance",
                MigrationId: migrationId,
                ClosureEligible: false,
                Closed: false,
                Assurance: assurance,
                Closure: null,
                Blockers: Array.Empty<string>(),
                Detail: "Production qualification closure is not applicable to simplified assurance. The accepted-baseline completion report is the release handoff and records the reduced source-freeze and rollback evidence boundary.");
        }

        var blockers = BuildClosureBlockers(context);
        if (blockers.Count == 0)
        {
            _ = ReadAndValidateSourceEnvelope(context.Qualification!);
            _ = ReadAndValidateQualificationEvidence(context.Qualification!);
        }
        return new MigrationTwoServerQualificationClosureStateResponse(
            Source: "control-plane",
            Status: blockers.Count == 0 ? "ready-to-close" : "blocked",
            MigrationId: migrationId,
            ClosureEligible: blockers.Count == 0,
            Closed: false,
            Assurance: assurance,
            Closure: null,
            Blockers: blockers,
            Detail: blockers.Count == 0
                ? "Review the accepted migration, distinct-host evidence, retained source boundary, normal Runtime Stack, and first native baseline backup before producing the final MEM 0.2.0 qualification handoff."
                : "Production qualification closure remains blocked until the complete accepted two-server evidence chain is durable and internally consistent.");
    }

    public async Task<MigrationTwoServerQualificationClosureStateResponse> CloseAsync(
        string migrationId,
        MigrationTwoServerQualificationClosureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAcknowledgements(request);

        var context = await LoadAsync(migrationId, tracking: true, cancellationToken);
        var assurance = MigrationTwoServerQualificationService.ResolveAssuranceOrNull(context.Intake);
        if (assurance is not null && MigrationAssuranceProjection.IsSimplified(assurance))
        {
            throw new InvalidOperationException(
                "Production qualification closure is not applicable to simplified assurance. Download the accepted-baseline completion report instead.");
        }

        if (HasClosure(context.Qualification))
        {
            return BuildClosedState(context, assurance: assurance);
        }

        var blockers = BuildClosureBlockers(context);
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Production qualification closure is blocked: {string.Join("; ", blockers)}");
        }

        var qualification = context.Qualification!;
        var sourceEnvelope = ReadAndValidateSourceEnvelope(qualification);
        var qualificationEvidence = ReadAndValidateQualificationEvidence(qualification);
        var targetHost = await targetHostIdentityProbe.ObserveAsync(cancellationToken);
        ValidateTargetHostStillMatches(qualificationEvidence.TargetHost, targetHost);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var closureId = CreateId("mtqc", nowUtc);
        var plan = context.Intake.ProductionAdoption!;
        var acceptance = context.Intake.Acceptance!;
        var baseline = acceptance.BaselineBackupHandoff ?? context.Intake.BaselineBackupHandoff!;
        var stack = context.RuntimeStack!;
        var catalog = context.BaselineCatalogEntry!;
        var note = NormalizeNote(request.Note);
        var payload = new MigrationTwoServerQualificationClosureEvidencePayload(
            ClosureId: closureId,
            ClosedAtUtc: nowUtc,
            MemVersion: MemVersion,
            MigrationId: context.Intake.IntakeId,
            Assurance: assurance ?? throw new InvalidOperationException(
                "Production qualification closure is missing its production assurance evidence."),
            SourceMigrationId: qualification.SourceMigrationId,
            PackageRevisionId: qualification.PackageRevisionId,
            EncryptedPackageSha256: qualification.EncryptedPackageSha256,
            SourceFingerprint: qualification.SourceFingerprint,
            SourceStackSlug: qualification.SourceStackSlug,
            MatrixServerName: qualification.MatrixServerName,
            QualificationId: qualification.QualificationId,
            QualificationEvidenceSha256: qualification.QualificationEvidenceSha256,
            SourceEvidenceAttemptId: qualification.SourceEvidenceAttemptId,
            SourceEvidenceSha256: qualification.SourceEvidenceSha256,
            AdoptionPlanId: qualification.AdoptionPlanId,
            RuntimeStackId: qualification.RuntimeStackId,
            RuntimeStackSlug: stack.Slug,
            ProductionVerificationId: qualification.ProductionVerificationId,
            ProductionVerificationEvidenceSha256: plan.ProductionVerificationEvidenceSha256!,
            ProductionVerificationCompletedAtUtc: plan.ProductionVerificationCompletedAtUtc!.Value,
            AcceptanceId: qualification.AcceptanceId,
            AcceptanceEvidenceSha256: acceptance.PublicVerificationEvidenceSha256,
            AcceptedAtUtc: acceptance.AcceptedAtUtc,
            BaselineBackupHandoffId: qualification.BaselineBackupHandoffId,
            BaselineBackupId: baseline.BackupId!,
            BaselineCatalogEntryId: qualification.BaselineCatalogEntryId,
            BaselineCatalogPayloadState: catalog.PayloadState,
            BaselineCatalogIntegrityStatus: catalog.IntegrityStatus,
            BaselineCompletedAtUtc: baseline.CompletedAtUtc!.Value,
            BaselineBackupBytes: baseline.BackupTotalBytes,
            BaselineBackupFiles: baseline.BackupTotalFiles,
            BaselineBackupWarnings: baseline.BackupWarningCount,
            SourceHost: sourceEnvelope.Payload.SourceHost,
            TargetHost: targetHost,
            DistinctMachineIdentity: qualification.DistinctMachineIdentity,
            DistinctDockerEngineIdentity: qualification.DistinctDockerEngineIdentity,
            SourceContainers: sourceEnvelope.Payload.Containers,
            PublicRoutes: BuildPublicRoutes(stack.Routes),
            QualificationEvidenceReviewed: request.QualificationEvidenceReviewed,
            DistinctHostEvidenceReviewed: request.DistinctHostEvidenceReviewed,
            NormalLifecycleReviewed: request.NormalLifecycleReviewed,
            SourceRetentionReviewed: request.SourceRetentionReviewed,
            ReleaseHandoffAcknowledged: request.ReleaseHandoffAcknowledged,
            Note: note);
        var envelope = new MigrationTwoServerQualificationClosureEnvelope(
            ClosureSchemaVersion,
            ComputePayloadSha256(payload),
            payload);

        qualification.ClosureId = closureId;
        qualification.ClosureStatus = "closed";
        qualification.ClosedAtUtc = nowUtc;
        qualification.ClosureEvidenceSha256 = envelope.PayloadSha256;
        qualification.ClosureEvidenceJson = JsonSerializer.Serialize(envelope, JsonOptions);
        qualification.ClosureNote = note;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Migration production qualification closed. MigrationId={MigrationId} QualificationId={QualificationId} ClosureId={ClosureId} RuntimeStackId={RuntimeStackId}",
            migrationId,
            qualification.QualificationId,
            closureId,
            qualification.RuntimeStackId);
        return BuildClosedState(context, envelope);
    }

    public async Task<MigrationTwoServerQualificationClosureEnvelope> GetReportAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var context = await LoadAsync(migrationId, tracking: false, cancellationToken);
        if (!HasClosure(context.Qualification))
        {
            throw new InvalidOperationException(
                "Production qualification closure has not been completed.");
        }

        return ReadAndValidateClosure(context);
    }

    internal static string ComputePayloadSha256(
        MigrationTwoServerQualificationClosureEvidencePayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Sha256(json);
    }

    internal static void ValidateAcknowledgements(
        MigrationTwoServerQualificationClosureRequest request)
    {
        if (!request.QualificationEvidenceReviewed ||
            !request.DistinctHostEvidenceReviewed ||
            !request.NormalLifecycleReviewed ||
            !request.SourceRetentionReviewed ||
            !request.ReleaseHandoffAcknowledged)
        {
            throw new InvalidOperationException(
                "All production qualification closure acknowledgements are required.");
        }
    }

    internal static void ValidateTargetHostStillMatches(
        MigrationTwoServerHostIdentity qualifiedTarget,
        MigrationTwoServerHostIdentity currentTarget)
    {
        if (!IsSha256(currentTarget.MachineIdSha256) ||
            !IsSha256(currentTarget.DockerEngineIdSha256) ||
            !string.Equals(
                qualifiedTarget.MachineIdSha256,
                currentTarget.MachineIdSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                qualifiedTarget.DockerEngineIdSha256,
                currentTarget.DockerEngineIdSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The current MEM 0.2.0 target host no longer matches the host and Docker Engine identities recorded by two-server qualification.");
        }
    }

    internal static IReadOnlyList<string> BuildClosureBlockers(
        MigrationTwoServerQualificationClosureContext context)
    {
        var blockers = new List<string>();
        var intake = context.Intake;
        var qualification = context.Qualification;
        var plan = intake.ProductionAdoption;
        var acceptance = intake.Acceptance;
        var baseline = acceptance?.BaselineBackupHandoff ?? intake.BaselineBackupHandoff;
        var stack = context.RuntimeStack;
        var catalog = context.BaselineCatalogEntry;

        if (qualification is null ||
            !string.Equals(qualification.Status, "qualified", StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(qualification.SourceEvidenceSha256) ||
            !IsSha256(qualification.QualificationEvidenceSha256) ||
            !qualification.DistinctMachineIdentity ||
            !qualification.DistinctDockerEngineIdentity ||
            qualification.DevelopmentExternalControlPlane)
        {
            blockers.Add("A durable production-only two-server qualification is required.");
        }

        if (plan is null ||
            !string.Equals(plan.Status, "accepted-baseline-backup-created", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ProductionVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(plan.ProductionVerificationId) ||
            plan.ProductionVerificationCompletedAtUtc is null ||
            !IsSha256(plan.ProductionVerificationEvidenceSha256))
        {
            blockers.Add("The accepted production adoption must retain its passed durable production-verification authority.");
        }

        if (acceptance is null ||
            string.IsNullOrWhiteSpace(acceptance.AcceptanceId) ||
            !IsSha256(acceptance.PublicVerificationEvidenceSha256) ||
            plan is null ||
            !string.Equals(
                acceptance.PublicVerificationEvidenceSha256,
                plan.ProductionVerificationEvidenceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Migration acceptance must remain bound to the exact production-verification evidence.");
        }

        if (baseline is null ||
            !string.Equals(baseline.Status, "created", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(baseline.HandoffId) ||
            string.IsNullOrWhiteSpace(baseline.BackupId) ||
            string.IsNullOrWhiteSpace(baseline.CatalogEntryId) ||
            baseline.CompletedAtUtc is null)
        {
            blockers.Add("The first native MEM baseline backup handoff must be complete.");
        }

        if (qualification is not null &&
            (plan is null ||
             acceptance is null ||
             baseline is null ||
             !string.Equals(qualification.AdoptionPlanId, plan.AdoptionPlanId, StringComparison.Ordinal) ||
             qualification.RuntimeStackId != plan.RuntimeStackId ||
             !string.Equals(qualification.ProductionVerificationId, plan.ProductionVerificationId, StringComparison.Ordinal) ||
             !string.Equals(qualification.AcceptanceId, acceptance.AcceptanceId, StringComparison.Ordinal) ||
             !string.Equals(qualification.BaselineBackupHandoffId, baseline.HandoffId, StringComparison.Ordinal) ||
             !string.Equals(qualification.BaselineCatalogEntryId, baseline.CatalogEntryId, StringComparison.Ordinal)))
        {
            blockers.Add("The two-server qualification must remain bound to the exact adoption, verification, acceptance, Runtime Stack, and baseline backup identities.");
        }

        if (catalog is null ||
            !string.Equals(catalog.PayloadState, "available", StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(catalog.IntegrityStatus, "valid", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(catalog.IntegrityStatus, "warning", StringComparison.OrdinalIgnoreCase)) ||
            baseline is null ||
            !string.Equals(catalog.CatalogEntryId, baseline.CatalogEntryId, StringComparison.Ordinal))
        {
            blockers.Add("The first native baseline backup must remain available in the Backup Catalog with usable integrity evidence.");
        }

        if (stack is null ||
            plan is null ||
            stack.Id != plan.RuntimeStackId ||
            !string.Equals(stack.Slug, plan.TargetStackSlug, StringComparison.Ordinal) ||
            !string.Equals(stack.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(stack.LastError))
        {
            blockers.Add("The accepted migrated Runtime Stack must be in the normal ready lifecycle without a recorded error.");
        }
        else
        {
            var matrixServices = stack.ServiceInstances.Where(x =>
                string.Equals(x.ServiceKey, ServiceKeys.Matrix, StringComparison.OrdinalIgnoreCase)).ToArray();
            var elementServices = stack.ServiceInstances.Where(x =>
                string.Equals(x.ServiceKey, ServiceKeys.ElementWeb, StringComparison.OrdinalIgnoreCase)).ToArray();
            var matrixService = matrixServices.Length == 1 ? matrixServices[0] : null;
            var elementService = elementServices.Length == 1 ? elementServices[0] : null;
            if (matrixService is null ||
                elementService is null ||
                matrixService.InstanceId != plan.MatrixInstanceId ||
                elementService.InstanceId != plan.ElementInstanceId ||
                !string.Equals(matrixService.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(elementService.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add("The normal Matrix and Element service ownership records must both be ready.");
            }

            var matrixRoute = FindRoute(stack.Routes, ServiceKeys.Matrix);
            var elementRoute = FindRoute(stack.Routes, ServiceKeys.ElementWeb);
            if (!IsExpectedRoute(
                    matrixRoute,
                    plan.MatrixNpmRouteId,
                    plan.MatrixPublicHost) ||
                !IsExpectedRoute(
                    elementRoute,
                    plan.ElementNpmRouteId,
                    plan.ElementPublicHost))
            {
                blockers.Add("The normal Runtime Stack must retain verified public Matrix and Element route ownership.");
            }
        }

        return blockers.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task<MigrationTwoServerQualificationClosureContext> LoadAsync(
        string migrationId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new FileNotFoundException("Migration Session was not found.");
        }

        IQueryable<MigrationIntakeEntity> intakeQuery = db.MigrationIntakes
            .Include(x => x.Sources)
            .Include(x => x.ProductionAuthorities)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.ProductionAdoption)!
                .ThenInclude(x => x!.PackageRevision)
            .Include(x => x.Acceptance)!
                .ThenInclude(x => x!.BaselineBackupHandoff)
            .Include(x => x.BaselineBackupHandoff)
            .Include(x => x.TwoServerQualification);
        IQueryable<RuntimeStackEntity> stackQuery = db.RuntimeStacks
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes);
        IQueryable<BackupCatalogEntryEntity> catalogQuery = db.BackupCatalogEntries;
        if (!tracking)
        {
            intakeQuery = intakeQuery.AsNoTracking();
            stackQuery = stackQuery.AsNoTracking();
            catalogQuery = catalogQuery.AsNoTracking();
        }

        var intake = await intakeQuery.SingleOrDefaultAsync(
                         item => item.IntakeId == migrationId,
                         cancellationToken)
                     ?? throw new FileNotFoundException("Migration Session was not found.");
        var qualification = intake.TwoServerQualification;
        RuntimeStackEntity? stack = null;
        BackupCatalogEntryEntity? catalog = null;
        if (qualification is not null)
        {
            stack = await stackQuery.SingleOrDefaultAsync(
                item => item.Id == qualification.RuntimeStackId,
                cancellationToken);
            catalog = await catalogQuery.SingleOrDefaultAsync(
                item => item.CatalogEntryId == qualification.BaselineCatalogEntryId,
                cancellationToken);
        }

        return new MigrationTwoServerQualificationClosureContext(
            intake,
            qualification,
            stack,
            catalog);
    }

    private static MigrationTwoServerQualificationClosureStateResponse BuildClosedState(
        MigrationTwoServerQualificationClosureContext context,
        MigrationTwoServerQualificationClosureEnvelope? envelope = null,
        MigrationAssuranceSummary? assurance = null)
    {
        envelope ??= ReadAndValidateClosure(context);
        assurance ??= MigrationTwoServerQualificationService.ResolveAssuranceOrNull(context.Intake);
        return new MigrationTwoServerQualificationClosureStateResponse(
            Source: "control-plane",
            Status: "closed",
            MigrationId: context.Intake.IntakeId,
            ClosureEligible: false,
            Closed: true,
            Assurance: assurance,
            Closure: envelope,
            Blockers: Array.Empty<string>(),
            Detail: "The MEM 0.1.0 to MEM 0.2.0 migration is production-qualified across distinct hosts, accepted into the normal runtime lifecycle, protected by its first native baseline backup, and closed with a hash-bound release handoff.");
    }

    private static MigrationTwoServerSourceEvidenceEnvelope ReadAndValidateSourceEnvelope(
        MigrationTwoServerQualificationEntity qualification)
    {
        var envelope = MigrationTwoServerQualificationService.ReadSourceEnvelope(
            qualification.SourceEvidenceJson);
        MigrationTwoServerQualificationService.ValidateSourceEvidenceEnvelope(envelope);
        MigrationTwoServerQualificationService.ValidateSourceEvidenceProof(envelope.Payload);
        if (!string.Equals(
                envelope.PayloadSha256,
                qualification.SourceEvidenceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stored source qualification evidence no longer matches its durable identity.");
        }
        return envelope;
    }

    private static MigrationTwoServerQualificationEvidencePayload ReadAndValidateQualificationEvidence(
        MigrationTwoServerQualificationEntity qualification)
    {
        var evidence = MigrationTwoServerQualificationService.ReadQualificationEvidence(
            qualification.QualificationEvidenceJson);
        if (!string.Equals(
                MigrationTwoServerQualificationService.ComputeQualificationPayloadSha256(evidence),
                qualification.QualificationEvidenceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stored two-server qualification evidence failed SHA-256 validation.");
        }
        MigrationTwoServerQualificationService.ValidateStoredQualificationEvidence(
            qualification,
            evidence);
        return evidence;
    }

    private static MigrationTwoServerQualificationClosureEnvelope ReadAndValidateClosure(
        MigrationTwoServerQualificationClosureContext context)
    {
        var qualification = context.Qualification
            ?? throw new InvalidOperationException("Two-server qualification was not found.");
        if (!HasClosure(qualification))
        {
            throw new InvalidOperationException(
                "Production qualification closure has not been completed.");
        }

        var envelope = JsonSerializer.Deserialize<MigrationTwoServerQualificationClosureEnvelope>(
                           qualification.ClosureEvidenceJson!,
                           JsonOptions)
                       ?? throw new InvalidDataException(
                           "Stored production qualification closure evidence could not be read.");
        if (!string.Equals(
                envelope.SchemaVersion,
                ClosureSchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                ComputePayloadSha256(envelope.Payload),
                envelope.PayloadSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                envelope.PayloadSha256,
                qualification.ClosureEvidenceSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                envelope.Payload.ClosureId,
                qualification.ClosureId,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Payload.QualificationId,
                qualification.QualificationId,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Payload.MigrationId,
                context.Intake.IntakeId,
                StringComparison.Ordinal) ||
            envelope.Payload.RuntimeStackId != qualification.RuntimeStackId ||
            !string.Equals(
                envelope.Payload.Assurance.AuthorityType,
                MigrationProductionAuthorityTypes.FinalFrozen,
                StringComparison.Ordinal) ||
            !envelope.Payload.Assurance.FormalSourceFreezeEvidenceCollected ||
            !envelope.Payload.Assurance.FinalRecapturePerformed ||
            !envelope.Payload.Assurance.PostCaptureWritesIndependentlyExcluded ||
            !envelope.Payload.Assurance.TwoServerQualificationRequired ||
            !envelope.Payload.QualificationEvidenceReviewed ||
            !envelope.Payload.DistinctHostEvidenceReviewed ||
            !envelope.Payload.NormalLifecycleReviewed ||
            !envelope.Payload.SourceRetentionReviewed ||
            !envelope.Payload.ReleaseHandoffAcknowledged)
        {
            throw new InvalidDataException(
                "Stored production qualification closure evidence no longer matches its durable record.");
        }

        return envelope;
    }

    private static IReadOnlyList<MigrationTwoServerQualificationClosureRoute> BuildPublicRoutes(
        IEnumerable<RuntimeRouteEntity> routes) =>
        routes
            .Where(route => route.IsPublic &&
                (string.Equals(route.ServiceKey, ServiceKeys.Matrix, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(route.ServiceKey, ServiceKeys.ElementWeb, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(route => route.ServiceKey, StringComparer.Ordinal)
            .Select(route => new MigrationTwoServerQualificationClosureRoute(
                route.ServiceKey,
                route.PublicHost,
                route.PublicBaseUrl,
                route.Provider,
                route.ProviderRouteId!,
                route.ForwardScheme,
                route.ForwardHost,
                route.ForwardPort,
                route.IsPublic,
                route.SslExpected,
                route.SslConfigured,
                route.ForceSsl,
                route.Status,
                route.LastVerifiedAtUtc))
            .ToArray();

    private static RuntimeRouteEntity? FindRoute(
        IEnumerable<RuntimeRouteEntity> routes,
        string serviceKey)
    {
        var matches = routes.Where(route =>
            string.Equals(route.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsExpectedRoute(
        RuntimeRouteEntity? route,
        string? expectedProviderRouteId,
        string expectedHost) =>
        route is not null &&
        route.IsPublic &&
        route.SslExpected &&
        route.SslConfigured &&
        route.ForceSsl &&
        string.Equals(route.Status, "verified", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(route.ProviderRouteId) &&
        string.Equals(route.ProviderRouteId, expectedProviderRouteId, StringComparison.Ordinal) &&
        string.Equals(route.PublicHost, expectedHost, StringComparison.OrdinalIgnoreCase);

    private static bool HasClosure(MigrationTwoServerQualificationEntity? qualification) =>
        qualification is not null &&
        string.Equals(qualification.ClosureStatus, "closed", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(qualification.ClosureId) &&
        qualification.ClosedAtUtc is not null &&
        IsSha256(qualification.ClosureEvidenceSha256) &&
        !string.IsNullOrWhiteSpace(qualification.ClosureEvidenceJson);

    private static string? NormalizeNote(string? note)
    {
        var value = note?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.Length <= 1000 ? value : value[..1000];
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string CreateId(string prefix, DateTime utc) =>
        $"{prefix}_{utc:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}";
}

internal sealed record MigrationTwoServerQualificationClosureContext(
    MigrationIntakeEntity Intake,
    MigrationTwoServerQualificationEntity? Qualification,
    RuntimeStackEntity? RuntimeStack,
    BackupCatalogEntryEntity? BaselineCatalogEntry);
