using HostAgent.Planning;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Migrations.Cutover;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Builds a non-mutating target identity review from the verified private test.
/// This service never creates production authority, runtime records, databases,
/// containers, manifests, secrets, or public routes.
/// </summary>
public sealed class MigrationPrivateServerTargetReviewService(
    MemDbContext db,
    MigrationCutoverContextResolver contextResolver,
    ChatStackRuntimePlanner runtimePlanner,
    RuntimeStackDatabaseService databaseService,
    RuntimeStackManifestStore manifestStore,
    IConfiguration configuration)
{
    public async Task<MigrationPrivateServerTargetReviewResponse> ReviewAsync(
        string migrationId,
        ReviewMigrationPrivateServerTargetRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await contextResolver.ResolvePlanningAsync(migrationId, ct);
        var sourceStackSlug = MigrationProductionAdoptionService.NormalizeSlug(
            context.Capture.SourceStackSlug);
        var matrixServerName =
            MigrationProductionAdoptionService.NormalizeTargetHost(
                context.Capture.MatrixServerName);
        var sourceElementPublicHost =
            MigrationProductionAdoptionService.ResolveElementPublicHost(
                null,
                context.Capture.ElementPublicUrl,
                matrixServerName);

        var targetStackSlug = MigrationProductionAdoptionService.NormalizeSlug(
            string.IsNullOrWhiteSpace(request.TargetStackSlug)
                ? sourceStackSlug
                : request.TargetStackSlug);
        var elementPublicHost =
            MigrationProductionAdoptionService.ResolveElementPublicHost(
                request.ElementPublicHost,
                context.Capture.ElementPublicUrl,
                matrixServerName);

        var existingPlan = await db.MigrationProductionAdoptions
            .AsNoTracking()
            .Include(x => x.MigrationIntake)
            .Where(x => x.MigrationIntake.IntakeId == migrationId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var runtimeStackId = existingPlan?.RuntimeStackId ?? Guid.NewGuid();
        var matrixInstanceId = existingPlan?.MatrixInstanceId ?? Guid.NewGuid();
        var elementInstanceId = existingPlan?.ElementInstanceId ?? Guid.NewGuid();
        var runtime = runtimePlanner.Plan(
            runtimeStackId,
            matrixInstanceId,
            elementInstanceId,
            targetStackSlug);
        var database = databaseService.GetExpectedMatrixDatabaseIdentity(
            runtimeStackId,
            targetStackSlug);

        var collisions = await FindCollisionsAsync(
            existingPlan?.Id,
            context.Intake.Id,
            runtimeStackId,
            targetStackSlug,
            matrixInstanceId,
            elementInstanceId,
            runtime,
            database,
            matrixServerName,
            elementPublicHost,
            ct);

        var stackCollisions = collisions
            .Where(x => x.Field == "stack-name")
            .ToArray();
        var matrixCollisions = collisions
            .Where(x => x.Field == "matrix-address")
            .ToArray();
        var elementCollisions = collisions
            .Where(x => x.Field == "element-address")
            .ToArray();

        var suggestedTargetStackSlug = stackCollisions.Length == 0
            ? null
            : await FindSuggestedStackSlugAsync(
                context.Intake.Id,
                existingPlan?.Id,
                runtimeStackId,
                matrixInstanceId,
                elementInstanceId,
                sourceStackSlug,
                matrixServerName,
                elementPublicHost,
                ct);

        return new MigrationPrivateServerTargetReviewResponse(
            MigrationId: migrationId,
            SourceStackSlug: sourceStackSlug,
            SourceElementPublicHost: sourceElementPublicHost,
            TargetStackSlug: targetStackSlug,
            MatrixServerName: matrixServerName,
            ElementPublicHost: elementPublicHost,
            StackNameStatus: stackCollisions.Length == 0 ? "available" : "conflict",
            MatrixAddressStatus: matrixCollisions.Length == 0 ? "locked-available" : "locked-conflict",
            ElementAddressStatus: elementCollisions.Length == 0 ? "available" : "conflict",
            CollisionFree: collisions.Count == 0,
            SuggestedTargetStackSlug: suggestedTargetStackSlug,
            Collisions: collisions,
            Detail: collisions.Count == 0
                ? "The target stack name, preserved Matrix address, Element address, database, containers, paths, and runtime ownership are available."
                : "Resolve every listed target ownership conflict before creating the private server.");
    }

    private async Task<List<MigrationPrivateServerTargetCollision>> FindCollisionsAsync(
        Guid? currentPlanEntityId,
        Guid migrationIntakeEntityId,
        Guid runtimeStackId,
        string targetStackSlug,
        Guid matrixInstanceId,
        Guid elementInstanceId,
        ChatStackRuntimePlan runtime,
        RuntimeStackDatabaseIdentity database,
        string matrixPublicHost,
        string elementPublicHost,
        CancellationToken ct)
    {
        var collisions = new List<MigrationPrivateServerTargetCollision>();

        var stackOwner = await db.RuntimeStacks
            .AsNoTracking()
            .Where(x => x.Id == runtimeStackId || x.Slug == targetStackSlug)
            .Select(x => new
            {
                x.Slug,
                x.DisplayName,
            })
            .FirstOrDefaultAsync(ct);
        if (stackOwner is not null)
        {
            var owner = DisplayOwner(stackOwner.DisplayName, stackOwner.Slug);
            collisions.Add(new(
                Field: "stack-name",
                Code: "runtime_stack_collision",
                ResourceType: "runtime-stack",
                ResourceValue: targetStackSlug,
                Owner: owner,
                Detail: $"Server name '{targetStackSlug}' is already owned by normal MEM stack '{owner}'."));
        }

        var serviceOwner = await db.RuntimeServiceInstances
            .AsNoTracking()
            .Where(x =>
                x.InstanceId == matrixInstanceId ||
                x.InstanceId == elementInstanceId ||
                x.ContainerName == runtime.MatrixContainerName ||
                x.ContainerName == runtime.ElementContainerName)
            .Select(x => new
            {
                x.ContainerName,
                StackSlug = x.RuntimeStack.Slug,
                StackDisplayName = x.RuntimeStack.DisplayName,
            })
            .FirstOrDefaultAsync(ct);
        if (serviceOwner is not null)
        {
            var owner = DisplayOwner(
                serviceOwner.StackDisplayName,
                serviceOwner.StackSlug);
            collisions.Add(new(
                Field: "stack-name",
                Code: "runtime_service_collision",
                ResourceType: "runtime-service",
                ResourceValue: serviceOwner.ContainerName ??
                    $"{runtime.MatrixContainerName}, {runtime.ElementContainerName}",
                Owner: owner,
                Detail: $"A container identity generated from server name '{targetStackSlug}' is already owned by normal MEM stack '{owner}'."));
        }

        var databaseOwner = await db.RuntimeStackDatabases
            .AsNoTracking()
            .Where(x =>
                x.RuntimeStackId == runtimeStackId ||
                x.DatabaseName == database.DatabaseName ||
                x.DatabaseUsername == database.DatabaseUsername)
            .Select(x => new
            {
                x.DatabaseName,
                StackSlug = x.RuntimeStack.Slug,
                StackDisplayName = x.RuntimeStack.DisplayName,
            })
            .FirstOrDefaultAsync(ct);
        if (databaseOwner is not null)
        {
            var owner = DisplayOwner(
                databaseOwner.StackDisplayName,
                databaseOwner.StackSlug);
            collisions.Add(new(
                Field: "stack-name",
                Code: "runtime_database_collision",
                ResourceType: "runtime-database",
                ResourceValue: databaseOwner.DatabaseName,
                Owner: owner,
                Detail: $"A PostgreSQL identity generated from server name '{targetStackSlug}' is already owned by normal MEM stack '{owner}'."));
        }

        var routeOwners = await db.RuntimeRoutes
            .AsNoTracking()
            .Where(x =>
                x.PublicHost == matrixPublicHost ||
                x.PublicHost == elementPublicHost)
            .Select(x => new
            {
                x.PublicHost,
                x.ServiceKey,
                StackSlug = x.RuntimeStack.Slug,
                StackDisplayName = x.RuntimeStack.DisplayName,
            })
            .ToArrayAsync(ct);
        foreach (var routeOwner in routeOwners)
        {
            var owner = DisplayOwner(
                routeOwner.StackDisplayName,
                routeOwner.StackSlug);
            var matrixRoute = string.Equals(
                routeOwner.PublicHost,
                matrixPublicHost,
                StringComparison.OrdinalIgnoreCase);
            var label = matrixRoute ? "Matrix address" : "Element address";
            collisions.Add(new(
                Field: matrixRoute ? "matrix-address" : "element-address",
                Code: "runtime_route_collision",
                ResourceType: "runtime-route",
                ResourceValue: routeOwner.PublicHost,
                Owner: owner,
                Detail: $"{label} '{routeOwner.PublicHost}' is already owned by normal MEM stack '{owner}'."));
        }

        var otherPlans = await MigrationProductionReservationQuery
            .CurrentReservations(db)
            .Where(x =>
                (!currentPlanEntityId.HasValue || x.Id != currentPlanEntityId.Value) &&
                x.MigrationIntakeEntityId != migrationIntakeEntityId)
            .Where(x =>
                x.RuntimeStackId == runtimeStackId ||
                x.TargetStackSlug == targetStackSlug ||
                x.MatrixInstanceId == matrixInstanceId ||
                x.ElementInstanceId == elementInstanceId ||
                x.DatabaseName == database.DatabaseName ||
                x.DatabaseUsername == database.DatabaseUsername ||
                x.MatrixPublicHost == matrixPublicHost ||
                x.ElementPublicHost == elementPublicHost)
            .Select(x => new
            {
                x.AdoptionPlanId,
                x.TargetStackSlug,
                x.MatrixPublicHost,
                x.ElementPublicHost,
            })
            .ToArrayAsync(ct);
        foreach (var otherPlan in otherPlans)
        {
            if (string.Equals(
                otherPlan.TargetStackSlug,
                targetStackSlug,
                StringComparison.OrdinalIgnoreCase))
            {
                collisions.Add(new(
                    Field: "stack-name",
                    Code: "migration_adoption_plan_collision",
                    ResourceType: "migration-adoption-plan",
                    ResourceValue: targetStackSlug,
                    Owner: otherPlan.AdoptionPlanId,
                    Detail: $"Another Migration Session currently reserves server name '{targetStackSlug}' in plan '{otherPlan.AdoptionPlanId}'."));
            }

            if (string.Equals(
                otherPlan.MatrixPublicHost,
                matrixPublicHost,
                StringComparison.OrdinalIgnoreCase))
            {
                collisions.Add(new(
                    Field: "matrix-address",
                    Code: "migration_adoption_plan_collision",
                    ResourceType: "migration-adoption-plan",
                    ResourceValue: matrixPublicHost,
                    Owner: otherPlan.AdoptionPlanId,
                    Detail: $"Another Migration Session currently reserves Matrix address '{matrixPublicHost}' in plan '{otherPlan.AdoptionPlanId}'."));
            }

            if (string.Equals(
                otherPlan.ElementPublicHost,
                elementPublicHost,
                StringComparison.OrdinalIgnoreCase))
            {
                collisions.Add(new(
                    Field: "element-address",
                    Code: "migration_adoption_plan_collision",
                    ResourceType: "migration-adoption-plan",
                    ResourceValue: elementPublicHost,
                    Owner: otherPlan.AdoptionPlanId,
                    Detail: $"Another Migration Session currently reserves Element address '{elementPublicHost}' in plan '{otherPlan.AdoptionPlanId}'."));
            }
        }

        if (await manifestStore.FindAsync(targetStackSlug, ct) is not null)
        {
            collisions.Add(new(
                Field: "stack-name",
                Code: "runtime_manifest_collision",
                ResourceType: "runtime-manifest",
                ResourceValue: targetStackSlug,
                Owner: targetStackSlug,
                Detail: $"A normal MEM runtime manifest already exists for server name '{targetStackSlug}'."));
        }

        if (Directory.Exists(runtime.MatrixDataPath) ||
            (!string.IsNullOrWhiteSpace(runtime.ElementDataPath) &&
             Directory.Exists(runtime.ElementDataPath)))
        {
            collisions.Add(new(
                Field: "stack-name",
                Code: "runtime_data_path_collision",
                ResourceType: "runtime-data-root",
                ResourceValue: runtimeStackId.ToString(),
                Owner: targetStackSlug,
                Detail: $"One or more normal runtime data directories already exist for server name '{targetStackSlug}'."));
        }

        return collisions
            .GroupBy(x => new
            {
                x.Field,
                x.Code,
                x.ResourceValue,
                x.Owner,
            })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<string?> FindSuggestedStackSlugAsync(
        Guid migrationIntakeEntityId,
        Guid? currentPlanEntityId,
        Guid runtimeStackId,
        Guid matrixInstanceId,
        Guid elementInstanceId,
        string sourceStackSlug,
        string matrixPublicHost,
        string elementPublicHost,
        CancellationToken ct)
    {
        for (var index = 1; index <= 20; index++)
        {
            var suffix = index == 1 ? "migrated" : $"migrated-{index}";
            var candidate = MigrationProductionAdoptionService.NormalizeSlug(
                $"{sourceStackSlug}-{suffix}");
            var runtime = runtimePlanner.Plan(
                runtimeStackId,
                matrixInstanceId,
                elementInstanceId,
                candidate);
            var database = databaseService.GetExpectedMatrixDatabaseIdentity(
                runtimeStackId,
                candidate);
            var collisions = await FindCollisionsAsync(
                currentPlanEntityId,
                migrationIntakeEntityId,
                runtimeStackId,
                candidate,
                matrixInstanceId,
                elementInstanceId,
                runtime,
                database,
                matrixPublicHost,
                elementPublicHost,
                ct);

            if (collisions.All(x => x.Field != "stack-name"))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string DisplayOwner(
        string? displayName,
        string slug) =>
        string.IsNullOrWhiteSpace(displayName)
            ? slug
            : $"{displayName} ({slug})";
}
