using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Identity;

namespace Modules.Setup.Start;

internal static class EstablishedOperatorStateProbe
{
    public static async Task<EstablishedOperatorStateObservation> ObserveAsync(
        MemDbContext db,
        CancellationToken cancellationToken)
    {
        var hasCompletedPlatformOwner = await (
            from user in db.Users.AsNoTracking()
            join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.IsEnabled &&
                !user.IsBootstrapProvisioning &&
                role.Name == MemOperatorRoles.PlatformOwner
            select user.Id)
            .AnyAsync(cancellationToken);

        if (!hasCompletedPlatformOwner)
        {
            return EstablishedOperatorStateObservation.None;
        }

        var evidence = new List<string>();

        if (await db.RuntimeServices.AsNoTracking().AnyAsync(cancellationToken))
        {
            evidence.Add("runtime-service");
        }

        if (await db.RuntimeStacks.AsNoTracking().AnyAsync(cancellationToken))
        {
            evidence.Add("runtime-stack");
        }

        if (await db.Domains.AsNoTracking().AnyAsync(cancellationToken))
        {
            evidence.Add("domain");
        }

        if (await db.BackupCatalogEntries.AsNoTracking().AnyAsync(cancellationToken))
        {
            evidence.Add("backup-catalog");
        }

        if (await db.RestoreAttempts.AsNoTracking().AnyAsync(cancellationToken))
        {
            evidence.Add("restore");
        }

        if (await db.MigrationIntakes.AsNoTracking().AnyAsync(cancellationToken))
        {
            evidence.Add("migration");
        }

        return evidence.Count == 0
            ? EstablishedOperatorStateObservation.None
            : new EstablishedOperatorStateObservation(true, evidence);
    }
}

internal sealed record EstablishedOperatorStateObservation(
    bool Established,
    IReadOnlyList<string> Evidence)
{
    public static EstablishedOperatorStateObservation None { get; } =
        new(false, []);
}
