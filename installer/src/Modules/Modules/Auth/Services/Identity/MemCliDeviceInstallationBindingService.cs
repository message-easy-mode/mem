using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Auth.Services.Identity;

public interface IMemCliDeviceInstallationBindingService
{
    /// <summary>
    /// Returns the latest completed installation known to this control plane.
    /// The value is server-derived; no CLI request can choose it.
    /// </summary>
    Task<Guid?> GetCurrentInstallationIdAsync(CancellationToken ct = default);
}

/// <summary>
/// Resolves the installation binding used by browser-approved CLI device
/// sessions. A fresh successful installation becomes the current identity,
/// which deliberately makes earlier installation-bound device sessions
/// inapplicable to the new deployment state.
/// </summary>
public sealed class MemCliDeviceInstallationBindingService(
    MemDbContext db) : IMemCliDeviceInstallationBindingService
{
    public Task<Guid?> GetCurrentInstallationIdAsync(CancellationToken ct = default) =>
        db.Installations
            .AsNoTracking()
            .Where(installation =>
                installation.Status == "Succeeded" &&
                installation.CompletedAtUtc != null)
            .OrderByDescending(installation => installation.CompletedAtUtc)
            .ThenByDescending(installation => installation.UpdatedAtUtc)
            .ThenByDescending(installation => installation.Id)
            .Select(installation => (Guid?)installation.Id)
            .FirstOrDefaultAsync(ct);
}
