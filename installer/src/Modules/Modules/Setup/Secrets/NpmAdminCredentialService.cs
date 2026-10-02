using System.Globalization;
using System.Net.Mail;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Setup.InstallRuns;

namespace Modules.Setup.Secrets;

public sealed class NpmAdminCredentialService
{
    private readonly MemDbContext _db;
    private readonly IInstallationSecretStore _secretStore;

    public NpmAdminCredentialService(
        MemDbContext db,
        IInstallationSecretStore secretStore)
    {
        _db = db;
        _secretStore = secretStore;
    }

    public async Task<NpmAdminCredentialProjection> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var installation = await CurrentInstallationAsync(cancellationToken);
        return installation is null
            ? Unavailable("No active or completed MEM installation is available for NPM credential lookup.")
            : await GetForInstallationAsync(installation.Id, cancellationToken);
    }

    public async Task<NpmAdminCredentialProjection> GetForInstallationAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var installationExists = await _db.Installations
            .AsNoTracking()
            .AnyAsync(x => x.Id == installationId, cancellationToken);

        if (!installationExists)
        {
            return Unavailable(
                $"Installation '{installationId}' was not found.",
                "InstallationNotFound");
        }

        var emailStored = await _secretStore.ExistsAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminEmail,
            cancellationToken);
        var passwordStored = await _secretStore.ExistsAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminPassword,
            cancellationToken);

        if (!emailStored || !passwordStored)
        {
            return new NpmAdminCredentialProjection(
                InstallationId: installationId,
                Status: "CredentialRequired",
                Message: "Choose the Nginx Proxy Manager administrator email and password before installation continues.",
                ErrorCode: "NpmAdminCredentialRequired",
                AdministratorEmail: null,
                CredentialStored: false,
                VerifiedAtUtc: null);
        }

        // Safe browser projections never need to materialize the protected
        // password. Only resolve the non-secret display identity and status.
        var email = await _secretStore.ResolveProtectedAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminEmail,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new NpmAdminCredentialProjection(
                InstallationId: installationId,
                Status: "CredentialRequired",
                Message: "The protected Nginx Proxy Manager administrator credential is incomplete and must be replaced.",
                ErrorCode: "NpmAdminCredentialRequired",
                AdministratorEmail: null,
                CredentialStored: false,
                VerifiedAtUtc: null);
        }

        var verifiedAt = await ResolveVerifiedAtAsync(installationId, cancellationToken);
        return new NpmAdminCredentialProjection(
            InstallationId: installationId,
            Status: verifiedAt.HasValue ? "Verified" : "Stored",
            Message: verifiedAt.HasValue
                ? "The protected Nginx Proxy Manager administrator credential is stored and verified."
                : "The Nginx Proxy Manager administrator credential is stored protected. It will be verified against NPM during initialization.",
            ErrorCode: null,
            AdministratorEmail: email,
            CredentialStored: true,
            VerifiedAtUtc: verifiedAt);
    }

    public async Task<NpmAdminCredentialProjection> StoreCurrentAsync(
        NpmAdminCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var installation = await CurrentWritableInstallationAsync(cancellationToken);
        if (installation is null)
        {
            return Unavailable(
                "No editable or recoverable first-time Setup installation is available for NPM credential storage.",
                "SetupAuthorityMissing");
        }

        var validation = ValidateForStorage(request);
        if (!validation.IsValid)
        {
            return Invalid(
                installation.Id,
                validation.ErrorCode!,
                validation.Message!);
        }

        var email = validation.Email!;
        var password = validation.Password!;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _secretStore.SetProtectedAsync(
                installation.Id,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminEmail,
                email,
                "Nginx Proxy Manager administrator email retained for managed NPM authentication.",
                cancellationToken);

            await _secretStore.SetProtectedAsync(
                installation.Id,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminPassword,
                password,
                "Nginx Proxy Manager administrator password retained protected for managed NPM authentication.",
                cancellationToken);

            // Any replacement credential must prove itself again before MEM may
            // project it as verified. The prior verification timestamp is not
            // transferable to new credential material.
            await _secretStore.DeleteAsync(
                installation.Id,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminVerifiedAtUtc,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetForInstallationAsync(installation.Id, cancellationToken);
    }


    internal NpmAdminCredentialValidation ValidateForStorage(
        NpmAdminCredentialRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (!LooksLikeEmailAddress(email))
        {
            return new NpmAdminCredentialValidation(
                false,
                "NpmAdminEmailInvalid",
                "Enter a valid email address for the Nginx Proxy Manager administrator.",
                null,
                null);
        }

        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            return new NpmAdminCredentialValidation(
                false,
                "NpmAdminPasswordMissing",
                "Enter a password for the Nginx Proxy Manager administrator.",
                null,
                null);
        }

        if (password.Length > 512)
        {
            return new NpmAdminCredentialValidation(
                false,
                "NpmAdminPasswordTooLong",
                "The Nginx Proxy Manager administrator password exceeds the supported protected credential boundary.",
                null,
                null);
        }

        return new NpmAdminCredentialValidation(
            true,
            null,
            null,
            email,
            password);
    }

    internal async Task<NpmAdminCredentialProjection> StoreVerifiedForInstallationAsync(
        Guid installationId,
        NpmAdminCredentialRequest request,
        DateTime verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        var installationExists = await _db.Installations
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == installationId && x.Status == InstallationStatuses.Succeeded,
                cancellationToken);
        if (!installationExists)
        {
            return Unavailable(
                "No completed MEM installation is available for NPM credential replacement.",
                "NpmInstalledStateUnavailable");
        }

        var validation = ValidateForStorage(request);
        if (!validation.IsValid)
        {
            return Invalid(
                installationId,
                validation.ErrorCode!,
                validation.Message!);
        }

        var normalizedVerifiedAtUtc = DateTime.SpecifyKind(verifiedAtUtc, DateTimeKind.Utc);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _secretStore.SetProtectedAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminEmail,
                validation.Email!,
                "Nginx Proxy Manager administrator email retained for managed NPM authentication.",
                cancellationToken);

            await _secretStore.SetProtectedAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminPassword,
                validation.Password!,
                "Nginx Proxy Manager administrator password retained protected for managed NPM authentication.",
                cancellationToken);

            await _secretStore.SetProtectedAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminVerifiedAtUtc,
                normalizedVerifiedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                "Last successful Nginx Proxy Manager administrator credential verification timestamp.",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetForInstallationAsync(installationId, cancellationToken);
    }

    public async Task<NpmAdminCredential?> ResolveAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var email = await _secretStore.ResolveProtectedAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminEmail,
            cancellationToken);

        var password = await _secretStore.ResolveProtectedAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminPassword,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var verifiedAt = await ResolveVerifiedAtAsync(installationId, cancellationToken);
        return new NpmAdminCredential(installationId, email, password, verifiedAt);
    }

    public async Task<NpmAdminCredential?> ResolveCurrentAsync(
        CancellationToken cancellationToken)
    {
        var installation = await CurrentInstallationAsync(cancellationToken);
        return installation is null
            ? null
            : await ResolveAsync(installation.Id, cancellationToken);
    }

    public async Task MarkVerifiedAsync(
        Guid installationId,
        DateTime verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalized = DateTime.SpecifyKind(verifiedAtUtc, DateTimeKind.Utc);
        await _secretStore.SetProtectedAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminVerifiedAtUtc,
            normalized.ToString("O", CultureInfo.InvariantCulture),
            "Last successful Nginx Proxy Manager administrator credential verification timestamp.",
            cancellationToken);
    }

    private async Task<DateTime?> ResolveVerifiedAtAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var value = await _secretStore.ResolveProtectedAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminVerifiedAtUtc,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(value) ||
            !DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    private async Task<InstallationEntity?> CurrentInstallationAsync(
        CancellationToken cancellationToken) =>
        await _db.Installations
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                x => InstallationStatuses.ActiveFirstTimeSetup.Contains(x.Status) ||
                     x.Status == InstallationStatuses.Succeeded,
                cancellationToken);

    private async Task<InstallationEntity?> CurrentWritableInstallationAsync(
        CancellationToken cancellationToken) =>
        await _db.Installations
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                x => x.Status == InstallationStatuses.Draft ||
                     x.Status == InstallationStatuses.Ready ||
                     x.Status == InstallationStatuses.WaitingForUser ||
                     x.Status == InstallationStatuses.Failed,
                cancellationToken);

    private static string NormalizeEmail(string? value) =>
        (value ?? string.Empty).Trim();

    private static bool LooksLikeEmailAddress(string value)
    {
        if (value.Length is < 3 or > 320)
        {
            return false;
        }

        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static NpmAdminCredentialProjection Invalid(
        Guid installationId,
        string errorCode,
        string message) =>
        new(
            InstallationId: installationId,
            Status: "ValidationFailed",
            Message: message,
            ErrorCode: errorCode,
            AdministratorEmail: null,
            CredentialStored: false,
            VerifiedAtUtc: null);

    private static NpmAdminCredentialProjection Unavailable(
        string message,
        string errorCode = "NpmAdminCredentialUnavailable") =>
        new(
            InstallationId: null,
            Status: "Unavailable",
            Message: message,
            ErrorCode: errorCode,
            AdministratorEmail: null,
            CredentialStored: false,
            VerifiedAtUtc: null);
}
