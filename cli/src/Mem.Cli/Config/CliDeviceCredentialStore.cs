using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Mem.Cli.Config;

/// <summary>
/// Secure local storage boundary for opaque MEM CLI device-session credentials.
/// Profile configuration remains deliberately non-secret and must never contain
/// a device credential, installer token, cookie, password, TOTP value, recovery
/// code, or raw control-plane response.
/// </summary>
public interface ICliDeviceCredentialStore
{
    /// <summary>
    /// Verifies that the selected OS secret store can safely write, read, and
    /// delete a disposable local probe. This must succeed before a future login
    /// flow starts a browser approval attempt; MEM CLI must never fall back to a
    /// plaintext file.
    /// </summary>
    Task<CliDeviceCredentialStoreAvailability> CheckAvailableAsync(
        CancellationToken ct = default);

    Task<CliDeviceCredentialReadResult> ReadAsync(
        CliDeviceCredentialKey key,
        CancellationToken ct = default);

    Task<CliDeviceCredentialWriteResult> StoreAsync(
        CliDeviceCredentialKey key,
        string credential,
        CancellationToken ct = default);

    Task<CliDeviceCredentialDeleteResult> DeleteAsync(
        CliDeviceCredentialKey key,
        CancellationToken ct = default);
}

/// <summary>
/// Safe lookup identity for one local CLI device session. The opaque credential
/// is intentionally not part of this value and is never serialized into the
/// normal MEM CLI profile/configuration store.
/// </summary>
public sealed record CliDeviceCredentialKey(
    string ProfileName,
    string ServerUrl)
{
    public static bool TryCreate(
        string? profileName,
        string? serverUrl,
        [NotNullWhen(true)] out CliDeviceCredentialKey? key)
    {
        key = null;

        if (!CliProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedProfileName) ||
            !CliProfileValidator.TryNormalizeServerUrl(
                serverUrl,
                out var normalizedServerUrl))
        {
            return false;
        }

        key = new CliDeviceCredentialKey(
            normalizedProfileName,
            normalizedServerUrl);

        return true;
    }
}

public enum CliDeviceCredentialStoreStatus
{
    Success,
    NotFound,
    Unavailable,
    InvalidCredential
}

public sealed record CliDeviceCredentialStoreAvailability(
    CliDeviceCredentialStoreStatus Status)
{
    public bool Available => Status == CliDeviceCredentialStoreStatus.Success;

    public static CliDeviceCredentialStoreAvailability AvailableResult() =>
        new(CliDeviceCredentialStoreStatus.Success);

    public static CliDeviceCredentialStoreAvailability Unavailable() =>
        new(CliDeviceCredentialStoreStatus.Unavailable);
}

public sealed record CliDeviceCredentialReadResult(
    CliDeviceCredentialStoreStatus Status,
    string? Credential = null)
{
    public bool Succeeded =>
        Status == CliDeviceCredentialStoreStatus.Success &&
        Credential is not null;

    public static CliDeviceCredentialReadResult Found(
        string credential) =>
        new(CliDeviceCredentialStoreStatus.Success, credential);

    public static CliDeviceCredentialReadResult NotFound() =>
        new(CliDeviceCredentialStoreStatus.NotFound);

    public static CliDeviceCredentialReadResult Unavailable() =>
        new(CliDeviceCredentialStoreStatus.Unavailable);

    public static CliDeviceCredentialReadResult InvalidCredential() =>
        new(CliDeviceCredentialStoreStatus.InvalidCredential);
}

public sealed record CliDeviceCredentialWriteResult(
    CliDeviceCredentialStoreStatus Status)
{
    public bool Succeeded =>
        Status == CliDeviceCredentialStoreStatus.Success;

    public static CliDeviceCredentialWriteResult Success() =>
        new(CliDeviceCredentialStoreStatus.Success);

    public static CliDeviceCredentialWriteResult Unavailable() =>
        new(CliDeviceCredentialStoreStatus.Unavailable);

    public static CliDeviceCredentialWriteResult InvalidCredential() =>
        new(CliDeviceCredentialStoreStatus.InvalidCredential);
}

public sealed record CliDeviceCredentialDeleteResult(
    CliDeviceCredentialStoreStatus Status)
{
    public bool Succeeded =>
        Status == CliDeviceCredentialStoreStatus.Success;

    public static CliDeviceCredentialDeleteResult Success() =>
        new(CliDeviceCredentialStoreStatus.Success);

    public static CliDeviceCredentialDeleteResult Unavailable() =>
        new(CliDeviceCredentialStoreStatus.Unavailable);
}

/// <summary>
/// Creates the supported credential-store implementation for the current OS.
/// Linux uses a Secret Service-compatible keyring through the documented
/// <c>secret-tool</c> client. Unsupported or unavailable environments fail
/// closed; no plaintext config-file or environment-variable fallback exists.
/// </summary>
public static class CliDeviceCredentialStore
{
    public static ICliDeviceCredentialStore CreateDefault() =>
        OperatingSystem.IsLinux()
            ? new SecretToolCliDeviceCredentialStore(
                new SystemSecretToolProcessRunner())
            : new UnavailableCliDeviceCredentialStore();
}

public sealed class UnavailableCliDeviceCredentialStore :
    ICliDeviceCredentialStore
{
    public Task<CliDeviceCredentialStoreAvailability> CheckAvailableAsync(
        CancellationToken ct = default) =>
        Task.FromResult(
            CliDeviceCredentialStoreAvailability.Unavailable());

    public Task<CliDeviceCredentialReadResult> ReadAsync(
        CliDeviceCredentialKey key,
        CancellationToken ct = default) =>
        Task.FromResult(CliDeviceCredentialReadResult.Unavailable());

    public Task<CliDeviceCredentialWriteResult> StoreAsync(
        CliDeviceCredentialKey key,
        string credential,
        CancellationToken ct = default) =>
        Task.FromResult(CliDeviceCredentialWriteResult.Unavailable());

    public Task<CliDeviceCredentialDeleteResult> DeleteAsync(
        CliDeviceCredentialKey key,
        CancellationToken ct = default) =>
        Task.FromResult(CliDeviceCredentialDeleteResult.Unavailable());
}

/// <summary>
/// Shared validation for opaque device credentials. Credentials are generated
/// server-side as 32 random bytes encoded with unpadded base64url.
/// </summary>
public static class CliDeviceCredentialValidator
{
    public const int OpaqueCredentialByteLength = 32;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();

        if (normalized.Any(char.IsWhiteSpace) ||
            normalized.Length is < 42 or > 44)
        {
            return false;
        }

        try
        {
            var base64 = normalized
                .Replace('-', '+')
                .Replace('_', '/');

            base64 = base64.PadRight(
                base64.Length + ((4 - (base64.Length % 4)) % 4),
                '=');

            var bytes = Convert.FromBase64String(base64);

            return bytes.Length == OpaqueCredentialByteLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string CreateProbeCredential() =>
        Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(
                OpaqueCredentialByteLength))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
