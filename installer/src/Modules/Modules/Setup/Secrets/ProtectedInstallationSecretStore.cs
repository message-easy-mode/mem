using System.Security.Cryptography;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Modules.Setup.Secrets;

public sealed class ProtectedInstallationSecretStore : IInstallationSecretStore
{
    private const string RootPurpose = "MEM.InstallationSecret.v1";
    private const int MaximumProtectedValueLength = 4000;

    private readonly MemDbContext _db;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ProtectedInstallationSecretStore(
        MemDbContext db,
        IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _dataProtectionProvider = dataProtectionProvider;
    }

    public async Task SetProtectedAsync(
        Guid installationId,
        string category,
        string key,
        string secret,
        string? description,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(installationId, category, key);

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Installation secret value is required.", nameof(secret));
        }

        var protectedValue = Protector(installationId, category, key).Protect(secret);
        if (protectedValue.Length > MaximumProtectedValueLength)
        {
            throw new InvalidOperationException("Protected installation secret exceeds the supported storage boundary.");
        }

        var now = DateTime.UtcNow;
        var entity = await _db.InstallationSecrets
            .FirstOrDefaultAsync(
                x => x.InstallationId == installationId && x.Key == key,
                cancellationToken);

        if (entity is null)
        {
            entity = new InstallationSecretEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installationId,
                Category = category,
                Key = key,
                Value = protectedValue,
                Description = NormalizeDescription(description),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.InstallationSecrets.Add(entity);
        }
        else
        {
            entity.Category = category;
            entity.Value = protectedValue;
            entity.Description = NormalizeDescription(description);
            entity.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> ResolveProtectedAsync(
        Guid installationId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(installationId, category, key);

        var protectedValue = await _db.InstallationSecrets
            .AsNoTracking()
            .Where(x =>
                x.InstallationId == installationId &&
                x.Category == category &&
                x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (protectedValue is null)
        {
            return null;
        }

        try
        {
            return Protector(installationId, category, key).Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Protected installation secret could not be resolved from the current Control Plane key ring.",
                ex);
        }
    }

    public Task<bool> ExistsAsync(
        Guid installationId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(installationId, category, key);

        return _db.InstallationSecrets
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.InstallationId == installationId &&
                    x.Category == category &&
                    x.Key == key,
                cancellationToken);
    }

    public async Task DeleteAsync(
        Guid installationId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(installationId, category, key);

        var entity = await _db.InstallationSecrets
            .FirstOrDefaultAsync(
                x =>
                    x.InstallationId == installationId &&
                    x.Category == category &&
                    x.Key == key,
                cancellationToken);

        if (entity is null)
        {
            return;
        }

        _db.InstallationSecrets.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private IDataProtector Protector(Guid installationId, string category, string key) =>
        _dataProtectionProvider.CreateProtector(
            RootPurpose,
            category,
            key,
            installationId.ToString("D"));

    private static void ValidateIdentity(Guid installationId, string category, string key)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("Installation id is required.", nameof(installationId));
        }

        if (string.IsNullOrWhiteSpace(category) || category.Length > 100)
        {
            throw new ArgumentException("Installation secret category is invalid.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            throw new ArgumentException("Installation secret key is invalid.", nameof(key));
        }
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= 500
                ? value.Trim()
                : value.Trim()[..500];
}
