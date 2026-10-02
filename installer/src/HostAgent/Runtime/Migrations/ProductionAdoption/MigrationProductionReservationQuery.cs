using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Central authority for migration adoption plans that still reserve normal-runtime identities.
/// Historical adoption evidence is retained, but a plan linked to a runtime that has reached the
/// canonical destroyed state no longer blocks reuse of its stack slug, hosts, database, containers,
/// paths, or runtime identity.
/// </summary>
public static class MigrationProductionReservationQuery
{
    public static IQueryable<MigrationProductionAdoptionEntity> CurrentReservations(
        MemDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.MigrationProductionAdoptions
            .AsNoTracking()
            .Where(plan => plan.Status != "superseded")
            .Where(plan => !db.RuntimeStacks.Any(runtime =>
                runtime.Id == plan.RuntimeStackId &&
                runtime.Status == "destroyed"));
    }
}
