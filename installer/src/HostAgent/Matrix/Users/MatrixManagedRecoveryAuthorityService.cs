using System.Collections.Concurrent;
using System.Security.Cryptography;
using HostAgent.Matrix.Provisioning;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Secrets;
using Microsoft.Extensions.Options;

namespace HostAgent.Matrix.Users;

public sealed class MatrixManagedRecoveryAuthorityService
{
    public const string UsernamePrefix = "mem_recovery_";
    public const string LegacyUsernamePrefix = "_mem_recovery_";
    public const string AuthoritySource = "managed-recovery-registration";

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    private readonly RuntimeStackSecretService _secretService;
    private readonly ISynapseSharedSecretRegistrationClient _registrationClient;
    private readonly ISynapseAdminUserClient _adminUserClient;
    private readonly MatrixAdminAuthorityService _authorityService;
    private readonly MatrixBootstrapOptions _bootstrapOptions;

    public MatrixManagedRecoveryAuthorityService(
        RuntimeStackSecretService secretService,
        ISynapseSharedSecretRegistrationClient registrationClient,
        ISynapseAdminUserClient adminUserClient,
        MatrixAdminAuthorityService authorityService,
        IOptions<MatrixBootstrapOptions> bootstrapOptions)
    {
        _secretService = secretService;
        _registrationClient = registrationClient;
        _adminUserClient = adminUserClient;
        _authorityService = authorityService;
        _bootstrapOptions = bootstrapOptions.Value;
    }

    public async Task<MatrixAdminAuthorityCredential> GetOrProvisionAsync(
        RuntimeStackManifest manifest,
        bool forceManagedReplacement,
        CancellationToken ct)
    {
        if (!forceManagedReplacement)
        {
            var existing = await TryGetExistingCredentialAsync(manifest.StackId, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var gate = Gates.GetOrAdd(manifest.StackId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            var existing = await TryGetExistingCredentialAsync(manifest.StackId, ct);
            if (existing is not null &&
                (!forceManagedReplacement || string.Equals(
                    existing.Source,
                    AuthoritySource,
                    StringComparison.Ordinal)))
            {
                return existing;
            }

            return await ProvisionManagedAuthorityAsync(manifest, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public static bool IsManagedRecoveryUser(string matrixUserId)
    {
        if (string.IsNullOrWhiteSpace(matrixUserId))
        {
            return false;
        }

        var value = matrixUserId.Trim();
        return value.StartsWith($"@{UsernamePrefix}", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith($"@{LegacyUsernamePrefix}", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MatrixAdminAuthorityCredential?> TryGetExistingCredentialAsync(
        Guid runtimeStackId,
        CancellationToken ct)
    {
        try
        {
            return await _authorityService.GetCredentialAsync(runtimeStackId, ct);
        }
        catch (MatrixAdminAuthorityException ex)
            when (ex.FailureKind is MatrixAdminAuthorityFailureKind.Required or
                MatrixAdminAuthorityFailureKind.Rejected)
        {
            return null;
        }
    }

    private async Task<MatrixAdminAuthorityCredential> ProvisionManagedAuthorityAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(manifest.Matrix.PublicBaseUrl) ||
            string.IsNullOrWhiteSpace(manifest.Matrix.ServerName))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_managed_recovery_route_incomplete",
                "The Matrix homeserver routing identity is incomplete.",
                MatrixAdminAuthorityFailureKind.Unavailable);
        }

        var perStackSharedSecret = await _secretService.GetMatrixRegistrationSharedSecretAsync(
            manifest.StackId,
            ct);
        var sharedSecret = (perStackSharedSecret ?? _bootstrapOptions.SharedSecret ?? string.Empty)
            .Trim();

        if (sharedSecret.Length == 0)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_managed_recovery_shared_secret_required",
                "MEM cannot establish password-reset authority because the Matrix registration shared secret is unavailable.",
                MatrixAdminAuthorityFailureKind.Unavailable);
        }

        var username = CreateUsername();
        var password = RuntimeStackSecretService.CreateSecretValue();

        try
        {
            var registration = await _registrationClient.RegisterAsync(
                manifest.Matrix.PublicBaseUrl,
                username,
                password,
                admin: true,
                sharedSecret,
                ct);

            if (string.IsNullOrWhiteSpace(registration.AccessToken))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_managed_recovery_token_missing",
                    "Synapse did not return password-reset authority for the MEM recovery administrator.",
                    MatrixAdminAuthorityFailureKind.Unavailable);
            }

            EnsureLocalMatrixUser(registration.UserId, manifest.Matrix.ServerName);

            var identity = await _adminUserClient.ValidateAuthorityAsync(
                manifest.Matrix.PublicBaseUrl,
                registration.AccessToken,
                ct);
            EnsureLocalMatrixUser(identity.MatrixUserId, manifest.Matrix.ServerName);

            if (!string.Equals(
                    identity.MatrixUserId,
                    registration.UserId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new MatrixAdminAuthorityException(
                    "matrix_managed_recovery_identity_mismatch",
                    "Synapse returned inconsistent identity for the MEM recovery administrator.",
                    MatrixAdminAuthorityFailureKind.Rejected);
            }

            await _authorityService.StoreAsync(
                manifest.StackId,
                registration.AccessToken,
                identity.MatrixUserId,
                AuthoritySource,
                DateTimeOffset.UtcNow,
                ct);

            return await _authorityService.GetCredentialAsync(manifest.StackId, ct);
        }
        catch (MatrixAdminAuthorityException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (IsSharedSecretRegistrationRejected(ex))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_managed_recovery_registration_rejected",
                "Synapse rejected creation of the MEM recovery administrator.",
                MatrixAdminAuthorityFailureKind.Unavailable,
                ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_managed_recovery_provision_failed",
                "MEM could not establish Matrix password-reset authority.",
                MatrixAdminAuthorityFailureKind.Unavailable,
                ex);
        }
    }

    private static bool IsSharedSecretRegistrationRejected(InvalidOperationException ex)
    {
        return ex.Message.StartsWith(
            "Synapse shared-secret registration failed with HTTP ",
            StringComparison.Ordinal);
    }

    private static string CreateUsername()
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        return $"{UsernamePrefix}{suffix}";
    }

    private static void EnsureLocalMatrixUser(string matrixUserId, string serverName)
    {
        if (!matrixUserId.EndsWith($":{serverName}", StringComparison.OrdinalIgnoreCase))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_managed_recovery_wrong_homeserver",
                "The MEM recovery administrator does not belong to this Matrix homeserver.",
                MatrixAdminAuthorityFailureKind.Rejected);
        }
    }
}
