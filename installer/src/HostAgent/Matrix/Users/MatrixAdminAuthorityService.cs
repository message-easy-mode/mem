using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Matrix.Users;

public interface IMatrixAdminAuthorityProtector
{
    string Protect(string accessToken);
    string Unprotect(string protectedAccessToken);
}

public sealed class MatrixAdminAuthorityDataProtector : IMatrixAdminAuthorityProtector
{
    private const string Purpose = "MatrixEasyMode.RuntimeStack.MatrixAdminAuthority.v1";
    private readonly IDataProtector _protector;

    public MatrixAdminAuthorityDataProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(string protectedAccessToken) =>
        _protector.Unprotect(protectedAccessToken);
}

public sealed class MatrixAdminAuthorityService
{
    public const string SecretKind = "matrix_admin_access_token_v1";

    private readonly MemDbContext _db;
    private readonly IMatrixAdminAuthorityProtector _protector;

    public MatrixAdminAuthorityService(
        MemDbContext db,
        IMatrixAdminAuthorityProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<MatrixAdminAuthorityStatus> GetStatusAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        var entity = await FindAsync(runtimeStackId, tracking: false, ct);

        if (entity is null)
        {
            return MatrixAdminAuthorityStatus.Required();
        }

        var metadata = ReadMetadata(entity.MetadataJson);
        var available = string.Equals(entity.Status, "active", StringComparison.Ordinal);

        return new MatrixAdminAuthorityStatus(
            Status: available
                ? MatrixAdminAuthorityStates.Available
                : MatrixAdminAuthorityStates.Invalid,
            CanResetPasswords: available,
            Source: metadata.Source,
            AdminUserId: metadata.AdminUserId,
            StoredAtUtc: entity.UpdatedAtUtc,
            LastValidatedAtUtc: metadata.LastValidatedAtUtc,
            ErrorCode: available ? null : metadata.ErrorCode);
    }

    public async Task<MatrixAdminAuthorityCredential> GetCredentialAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        var entity = await FindAsync(runtimeStackId, tracking: false, ct);

        if (entity is null)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_required",
                "Matrix administrator authority is required before resetting passwords.",
                MatrixAdminAuthorityFailureKind.Required);
        }

        var metadata = ReadMetadata(entity.MetadataJson);

        if (!string.Equals(entity.Status, "active", StringComparison.Ordinal))
        {
            throw new MatrixAdminAuthorityException(
                metadata.ErrorCode ?? "matrix_admin_authority_invalid",
                "The stored Matrix administrator authority is not currently usable.",
                MatrixAdminAuthorityFailureKind.Rejected);
        }

        try
        {
            var accessToken = _protector.Unprotect(entity.SecretValue);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new CryptographicException(
                    "The protected Matrix administrator authority was empty.");
            }

            return new MatrixAdminAuthorityCredential(
                AccessToken: accessToken,
                AdminUserId: metadata.AdminUserId,
                Source: metadata.Source);
        }
        catch (CryptographicException ex)
        {
            try
            {
                await MarkInvalidAsync(
                    runtimeStackId,
                    "matrix_admin_authority_unprotect_failed",
                    ct);
            }
            catch
            {
                // Preserve the decryption failure as the primary result.
            }

            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_unprotect_failed",
                "The stored Matrix administrator authority could not be decrypted.",
                MatrixAdminAuthorityFailureKind.Rejected,
                ex);
        }
    }

    public async Task<MatrixAdminAuthorityStatus> StoreAsync(
        Guid runtimeStackId,
        string accessToken,
        string adminUserId,
        string source,
        DateTimeOffset validatedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_token_required",
                "A Matrix administrator access token is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_identity_required",
                "The Matrix administrator identity is required.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (!await _db.RuntimeStacks.AnyAsync(x => x.Id == runtimeStackId, ct))
        {
            throw new InvalidOperationException(
                $"RuntimeStack '{runtimeStackId}' was not found before storing Matrix administrator authority.");
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var entity = await FindAsync(runtimeStackId, tracking: true, ct);
        var protectedValue = _protector.Protect(accessToken.Trim());

        if (entity is null)
        {
            entity = new RuntimeStackSecretEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = runtimeStackId,
                SecretKind = SecretKind,
                CreatedAtUtc = now
            };

            _db.RuntimeStackSecrets.Add(entity);
        }
        else if (!string.Equals(entity.SecretValue, protectedValue, StringComparison.Ordinal))
        {
            entity.RotatedAtUtc = now;
        }

        entity.SecretValue = protectedValue;
        entity.Status = "active";
        entity.UpdatedAtUtc = now;
        entity.MetadataJson = JsonSerializer.Serialize(
            new MatrixAdminAuthorityMetadata(
                Source: source,
                AdminUserId: adminUserId,
                LastValidatedAtUtc: validatedAtUtc.UtcDateTime,
                ErrorCode: null,
                Protection: "aspnet-data-protection-v1"),
            JsonOptions());

        await _db.SaveChangesAsync(ct);

        return await GetStatusAsync(runtimeStackId, ct);
    }

    public async Task MarkInvalidAsync(
        Guid runtimeStackId,
        string errorCode,
        CancellationToken ct)
    {
        var entity = await FindAsync(runtimeStackId, tracking: true, ct);

        if (entity is null)
        {
            return;
        }

        var metadata = ReadMetadata(entity.MetadataJson);
        entity.Status = "invalid";
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
        entity.MetadataJson = JsonSerializer.Serialize(
            metadata with { ErrorCode = errorCode },
            JsonOptions());

        await _db.SaveChangesAsync(ct);
    }

    private Task<RuntimeStackSecretEntity?> FindAsync(
        Guid runtimeStackId,
        bool tracking,
        CancellationToken ct)
    {
        var query = _db.RuntimeStackSecrets
            .Where(x =>
                x.RuntimeStackId == runtimeStackId &&
                x.SecretKind == SecretKind);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(ct);
    }

    private static MatrixAdminAuthorityMetadata ReadMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new MatrixAdminAuthorityMetadata(
                Source: null,
                AdminUserId: null,
                LastValidatedAtUtc: null,
                ErrorCode: null,
                Protection: null);
        }

        try
        {
            var root = JsonNode.Parse(metadataJson) as JsonObject;

            return new MatrixAdminAuthorityMetadata(
                Source: root?["source"]?.GetValue<string>(),
                AdminUserId: root?["adminUserId"]?.GetValue<string>(),
                LastValidatedAtUtc: ReadUtc(root?["lastValidatedAtUtc"]),
                ErrorCode: root?["errorCode"]?.GetValue<string>(),
                Protection: root?["protection"]?.GetValue<string>());
        }
        catch (JsonException)
        {
            return new MatrixAdminAuthorityMetadata(
                Source: null,
                AdminUserId: null,
                LastValidatedAtUtc: null,
                ErrorCode: "matrix_admin_authority_metadata_invalid",
                Protection: null);
        }
    }

    private static DateTime? ReadUtc(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<DateTime>(out var dateTime))
        {
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return DateTime.TryParse(node?.ToString(), out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record MatrixAdminAuthorityMetadata(
        string? Source,
        string? AdminUserId,
        DateTime? LastValidatedAtUtc,
        string? ErrorCode,
        string? Protection);
}
