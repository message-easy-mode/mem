using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Contracts;

namespace Modules.Auth.Services.Identity;

public interface IMemOperatorDirectoryService
{
    Task<IReadOnlyList<MemOperatorDirectoryEntry>> ListAsync(
        Guid currentOperatorId,
        CancellationToken ct = default);
}

/// <summary>
/// Provides the Platform Owner-only operator inventory used by SEC-AUTH-04B.
/// The directory is intentionally read-only in this first governance slice;
/// user creation, enrolment, role mutation, account enablement and session
/// revocation are separate follow-on changes with their own safety gates.
/// </summary>
public sealed class MemOperatorDirectoryService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    TimeProvider timeProvider) : IMemOperatorDirectoryService
{
    public async Task<IReadOnlyList<MemOperatorDirectoryEntry>> ListAsync(
        Guid currentOperatorId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var users = await db.Users
            .AsNoTracking()
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .ToArrayAsync(ct);

        // SQLite cannot translate DateTimeOffset comparisons. Enrollment grants
        // are short-lived and rare, so fetch active status rows and apply expiry
        // projection in memory. The raw code/hash is intentionally never read by
        // this directory projection.
        var now = timeProvider.GetUtcNow();
        var activeGrantExpiryByOperator = (await db.MemOperatorEnrollmentGrants
            .AsNoTracking()
            .Where(grant => grant.Status == MemOperatorEnrollmentGrantStatuses.Active)
            .ToArrayAsync(ct))
            .Where(grant => grant.ExpiresAtUtc > now)
            .GroupBy(grant => grant.OperatorId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(grant => grant.CreatedAtUtc)
                    .First()
                    .ExpiresAtUtc);

        var entries = new List<MemOperatorDirectoryEntry>(users.Length);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var enrollmentGrantExpiresAtUtc =
                activeGrantExpiryByOperator.TryGetValue(user.Id, out var activeGrantExpiry)
                    ? activeGrantExpiry
                    : (DateTimeOffset?)null;

            entries.Add(new MemOperatorDirectoryEntry(
                OperatorId: user.Id,
                Username: user.UserName ?? user.Id.ToString("D"),
                Email: user.Email,
                IsEnabled: user.IsEnabled,
                IsBootstrapProvisioning: user.IsBootstrapProvisioning,
                HasPassword: !string.IsNullOrWhiteSpace(user.PasswordHash),
                HasTotp: user.TwoFactorEnabled,
                Roles: roles.OrderBy(role => role, StringComparer.Ordinal).ToArray(),
                CreatedAtUtc: user.CreatedAtUtc,
                LastLoginAtUtc: user.LastLoginAtUtc,
                EnrollmentGrantExpiresAtUtc: enrollmentGrantExpiresAtUtc,
                IsCurrentOperator: user.Id == currentOperatorId));
        }

        return entries;
    }
}
