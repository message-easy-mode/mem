using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Setup.Start;
using Modules.Setup.InstallRuns;

namespace Modules.Setup.Lifecycle;

public sealed record FirstTimeSetupLockState(
    bool Locked,
    string ReasonCode,
    Guid? ActiveInstallationId = null,
    IReadOnlyList<string>? Evidence = null)
{
    public static FirstTimeSetupLockState Available(Guid? activeInstallationId = null) =>
        new(false, "available", activeInstallationId);
}

public sealed class FirstTimeSetupLockService(MemDbContext db)
{
    public async Task<FirstTimeSetupLockState> GetStateAsync(
        CancellationToken cancellationToken)
    {
        var completedInstallationId = await db.Installations
            .AsNoTracking()
            .Where(installation => installation.Status == InstallationStatuses.Succeeded)
            .OrderByDescending(installation => installation.UpdatedAtUtc)
            .Select(installation => (Guid?)installation.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (completedInstallationId.HasValue)
        {
            return new FirstTimeSetupLockState(
                Locked: true,
                ReasonCode: "installation-completed");
        }

        var activeInstallationId = await db.Installations
            .AsNoTracking()
            .Where(installation => InstallationStatuses.ActiveFirstTimeSetup.Contains(installation.Status))
            .OrderByDescending(installation => installation.UpdatedAtUtc)
            .Select(installation => (Guid?)installation.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeInstallationId.HasValue)
        {
            return FirstTimeSetupLockState.Available(activeInstallationId);
        }

        var establishedState = await EstablishedOperatorStateProbe.ObserveAsync(
            db,
            cancellationToken);

        return establishedState.Established
            ? new FirstTimeSetupLockState(
                Locked: true,
                ReasonCode: "established-operator-state",
                Evidence: establishedState.Evidence)
            : FirstTimeSetupLockState.Available();
    }
}
