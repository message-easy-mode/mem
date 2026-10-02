using HostAgent.Runtime.Migrations.Assurance;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Coordinates the simplified operator journey for creating the normal MEM runtime privately.
/// Existing authority, planning, validation, and materialisation services remain authoritative.
/// </summary>
public sealed class MigrationPrivateServerCreationService(
    MemDbContext db,
    MigrationProductionAuthorityService authorityService,
    MigrationProductionAdoptionService adoptionService,
    MigrationProductionRuntimeMaterializationService materializationService)
{
    private static readonly TimeSpan MaterializationTimeout = TimeSpan.FromMinutes(45);

    public async Task<MigrationProductionAdoptionStateResponse> CreateAsync(
        string migrationId,
        Guid operatorId,
        CreateMigrationPrivateServerRequest request,
        CancellationToken requestCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateConfirmations(request);

        var current = await adoptionService.GetStateAsync(migrationId, requestCancellationToken);
        if (HasCreatedPrivateRuntime(current.Plan))
        {
            return current;
        }

        if (string.Equals(current.Status, "materializing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Private server creation is already running. Refresh the Migration workspace for durable state.");
        }

        if (string.Equals(current.Status, "materialization-failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Private server creation previously failed after mutation began. Review the retained failure evidence and complete ownership-bound recovery before trying again.");
        }

        var authority = await authorityService.GetStateAsync(
            migrationId,
            requestCancellationToken);
        if (!authority.AuthorizesProduction &&
            !await HasActiveFinalPackageAsync(migrationId, requestCancellationToken))
        {
            await authorityService.CreateOperatorAttestedSnapshotAsync(
                migrationId,
                operatorId,
                new CreateOperatorAttestedSnapshotAuthorityRequest(
                    AcknowledgeUsersWereInstructedNotToUseSource: true,
                    AcknowledgePostCaptureWritesWillNotMigrate: true,
                    AcknowledgeSelectedSnapshotBecomesAuthoritative: true,
                    AcknowledgeSourceWillBeRetainedUntilVerification: true,
                    AcknowledgeNoFormalSourceFreezeEvidence: true,
                    AcknowledgeReducedRollbackAssurance: true),
                requestCancellationToken);
        }

        var prepared = await adoptionService.PrepareAsync(
            migrationId,
            new PrepareMigrationProductionAdoptionRequest(
                request.TargetStackSlug,
                request.ElementPublicHost),
            requestCancellationToken);

        if (prepared.Plan is null || !prepared.Plan.CollisionFree ||
            !string.Equals(prepared.Status, "prepared", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                prepared.Plan?.BlockerSummary ??
                "The proposed private server identity is not available. Review the stack name and Element address and try again.");
        }

        using var operationTimeout = new CancellationTokenSource(MaterializationTimeout);
        return await materializationService.MaterializeAsync(
            migrationId,
            new MaterializeMigrationProductionRuntimeRequest(
                Operator: null,
                Note: null,
                ExecutePrivateProductionMaterialization: true,
                AcknowledgeCreatesNormalRuntimeRecords: true,
                AcknowledgeMutatesProductionPostgres: true,
                AcknowledgeStartsProductionContainers: true,
                AcknowledgeNoPublicRoutes: true,
                AcknowledgeNoAutomaticRollback: true),
            operationTimeout.Token);
    }

    private async Task<bool> HasActiveFinalPackageAsync(
        string migrationId,
        CancellationToken ct)
    {
        return await db.MigrationPackageRevisions
            .AsNoTracking()
            .AnyAsync(x =>
                x.MigrationIntake.IntakeId == migrationId &&
                x.Purpose == "final" &&
                x.ActivePurposeKey != null,
                ct);
    }

    internal static IReadOnlyList<string> GetMissingConfirmations(CreateMigrationPrivateServerRequest request)
    {
        var missing = new List<string>();
        if (!request.ConfirmVerifiedSnapshotIsAuthoritative) missing.Add("confirmVerifiedSnapshotIsAuthoritative");
        if (!request.ConfirmLaterSourceWritesAreNotIncluded) missing.Add("confirmLaterSourceWritesAreNotIncluded");
        if (!request.ConfirmCreatePrivateServer) missing.Add("confirmCreatePrivateServer");
        return missing;
    }

    private static void ValidateConfirmations(CreateMigrationPrivateServerRequest request)
    {
        var missing = GetMissingConfirmations(request);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required confirmations: {string.Join(", ", missing)}");
        }
    }

    private static bool HasCreatedPrivateRuntime(MigrationProductionAdoptionPlanDto? plan) =>
        plan is not null &&
        plan.Materialization.ProductionDatabaseImported &&
        plan.Materialization.MatrixContainerStarted &&
        plan.Materialization.MatrixHealthPassed &&
        plan.Materialization.ElementContainerStarted &&
        plan.Materialization.ElementHealthPassed &&
        plan.Materialization.RuntimeManifestSaved &&
        plan.Materialization.DatabaseOwnershipSaved &&
        plan.Materialization.RuntimeRecordsCreated;
}
