using System.Security.Cryptography;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Modules.Shared.Domains.Renewal;

public sealed class ProtectedDomainSecretStore : IDomainSecretStore
{
    private const string RootPurpose = "MEM.DomainSecret.v1";
    private const int MaximumProtectedValueLength = 4000;

    private readonly MemDbContext _db;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ProtectedDomainSecretStore(
        MemDbContext db,
        IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _dataProtectionProvider = dataProtectionProvider;
    }

    public async Task SetProtectedAsync(
        Guid domainId,
        string category,
        string key,
        string secret,
        string? description,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(domainId, category, key);

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Domain secret value is required.", nameof(secret));
        }

        if (!await _db.Domains.AsNoTracking().AnyAsync(x => x.Id == domainId, cancellationToken))
        {
            throw new InvalidOperationException($"Domain '{domainId}' was not found.");
        }

        var protectedValue = Protector(domainId, category, key).Protect(secret);
        if (protectedValue.Length > MaximumProtectedValueLength)
        {
            throw new InvalidOperationException("Protected domain secret exceeds the supported storage boundary.");
        }

        var now = DateTime.UtcNow;
        var entity = await _db.DomainSecrets
            .FirstOrDefaultAsync(
                x => x.DomainId == domainId && x.Key == key,
                cancellationToken);

        if (entity is null)
        {
            entity = new DomainSecretEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domainId,
                Category = category,
                Key = key,
                ProtectedValue = protectedValue,
                Description = NormalizeDescription(description),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.DomainSecrets.Add(entity);
        }
        else
        {
            entity.Category = category;
            entity.ProtectedValue = protectedValue;
            entity.Description = NormalizeDescription(description);
            entity.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> ResolveProtectedAsync(
        Guid domainId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(domainId, category, key);

        var protectedValue = await _db.DomainSecrets
            .AsNoTracking()
            .Where(x => x.DomainId == domainId && x.Category == category && x.Key == key)
            .Select(x => x.ProtectedValue)
            .FirstOrDefaultAsync(cancellationToken);

        if (protectedValue is null)
        {
            return null;
        }

        try
        {
            return Protector(domainId, category, key).Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Protected domain secret could not be resolved from the current Control Plane key ring.",
                ex);
        }
    }

    public Task<bool> ExistsAsync(
        Guid domainId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(domainId, category, key);

        return _db.DomainSecrets
            .AsNoTracking()
            .AnyAsync(
                x => x.DomainId == domainId && x.Category == category && x.Key == key,
                cancellationToken);
    }

    public async Task DeleteAsync(
        Guid domainId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(domainId, category, key);

        var entity = await _db.DomainSecrets
            .FirstOrDefaultAsync(
                x => x.DomainId == domainId && x.Category == category && x.Key == key,
                cancellationToken);

        if (entity is null)
        {
            return;
        }

        _db.DomainSecrets.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private IDataProtector Protector(Guid domainId, string category, string key) =>
        _dataProtectionProvider.CreateProtector(
            RootPurpose,
            category,
            key,
            domainId.ToString("D"));

    private static void ValidateIdentity(Guid domainId, string category, string key)
    {
        if (domainId == Guid.Empty)
        {
            throw new ArgumentException("Domain id is required.", nameof(domainId));
        }

        if (string.IsNullOrWhiteSpace(category) || category.Length > 100)
        {
            throw new ArgumentException("Domain secret category is invalid.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            throw new ArgumentException("Domain secret key is invalid.", nameof(key));
        }
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= 500
                ? value.Trim()
                : value.Trim()[..500];
}
