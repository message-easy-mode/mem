using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Secrets;

public sealed class RuntimeStackSecretService
{
    public const string MatrixRegistrationSharedSecretKind = "matrix_registration_shared_secret";

    private readonly MemDbContext _db;

    public RuntimeStackSecretService(MemDbContext db)
    {
        _db = db;
    }

    public async Task<bool> RuntimeStackExistsAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        return await _db.RuntimeStacks
            .AsNoTracking()
            .AnyAsync(x => x.Id == runtimeStackId, ct);
    }

    public async Task<string?> GetMatrixRegistrationSharedSecretAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        var entity = await _db.Set<RuntimeStackSecretEntity>()
            .AsNoTracking()
            .Where(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.SecretKind == MatrixRegistrationSharedSecretKind &&
                x.Status == "active")
            .FirstOrDefaultAsync(ct);

        return entity?.SecretValue;
    }

    public async Task UpsertMatrixRegistrationSharedSecretAsync(
        Guid runtimeStackId,
        string secretValue,
        string source,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secretValue))
        {
            throw new InvalidOperationException("Matrix registration shared secret value is required.");
        }

        var stackExists = await _db.RuntimeStacks
            .AnyAsync(x => x.Id == runtimeStackId, ct);

        if (!stackExists)
        {
            throw new InvalidOperationException(
                $"RuntimeStack '{runtimeStackId}' was not found. Save the RuntimeStack before attaching secrets.");
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime;

        var entity = await _db.Set<RuntimeStackSecretEntity>()
            .FirstOrDefaultAsync(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.SecretKind == MatrixRegistrationSharedSecretKind,
                ct);

        if (entity is null)
        {
            entity = new RuntimeStackSecretEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = runtimeStackId,
                SecretKind = MatrixRegistrationSharedSecretKind,
                CreatedAtUtc = now
            };

            _db.Set<RuntimeStackSecretEntity>().Add(entity);
        }
        else if (!string.Equals(entity.SecretValue, secretValue, StringComparison.Ordinal))
        {
            entity.RotatedAtUtc = now;
        }

        entity.SecretValue = secretValue;
        entity.Status = "active";
        entity.UpdatedAtUtc = now;
        entity.MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["source"] = source,
            ["note"] = "Stored in plain SQLite for current dev slice. Protect/encrypt before production."
        }, JsonOptions());

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetSecretValueAsync(
    Guid runtimeStackId,
    string secretKind,
    CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secretKind))
        {
            throw new ArgumentException("Secret kind is required.", nameof(secretKind));
        }

        var entity = await _db.Set<RuntimeStackSecretEntity>()
            .AsNoTracking()
            .Where(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.SecretKind == secretKind &&
                x.Status == "active")
            .FirstOrDefaultAsync(ct);

        return entity?.SecretValue;
    }

    public async Task UpsertSecretValueAsync(
        Guid runtimeStackId,
        string secretKind,
        string secretValue,
        string? metadataJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secretKind))
        {
            throw new ArgumentException("Secret kind is required.", nameof(secretKind));
        }

        if (string.IsNullOrWhiteSpace(secretValue))
        {
            throw new InvalidOperationException("Secret value is required.");
        }

        var stackExists = await _db.RuntimeStacks
            .AnyAsync(x => x.Id == runtimeStackId, ct);

        if (!stackExists)
        {
            throw new InvalidOperationException(
                $"RuntimeStack '{runtimeStackId}' was not found. Save the RuntimeStack before attaching secrets.");
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime;

        var entity = await _db.Set<RuntimeStackSecretEntity>()
            .FirstOrDefaultAsync(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.SecretKind == secretKind,
                ct);

        if (entity is null)
        {
            entity = new RuntimeStackSecretEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = runtimeStackId,
                SecretKind = secretKind,
                CreatedAtUtc = now
            };

            _db.Set<RuntimeStackSecretEntity>().Add(entity);
        }
        else if (!string.Equals(entity.SecretValue, secretValue, StringComparison.Ordinal))
        {
            entity.RotatedAtUtc = now;
        }

        entity.SecretValue = secretValue;
        entity.Status = "active";
        entity.UpdatedAtUtc = now;
        entity.MetadataJson = metadataJson;

        await _db.SaveChangesAsync(ct);
    }

    public static string CreateSecretValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
