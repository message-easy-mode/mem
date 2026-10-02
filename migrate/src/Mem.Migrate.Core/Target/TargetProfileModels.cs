using System.Diagnostics.CodeAnalysis;

namespace Mem.Migrate.Core.Target;

/// <summary>
/// Resolved non-secret profile identity plus the opaque device credential read
/// from the same OS Secret Service item used by the normal MEM CLI.
/// </summary>
public sealed record TargetProfileCredential(
    string ProfileName,
    string TargetBaseUrl,
    string DeviceCredential);

public interface ITargetProfileCredentialResolver
{
    Task<TargetProfileCredential> ResolveAsync(
        string profileName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Shared validation aligned with the current MEM CLI profile contract.
/// </summary>
public static class TargetProfileValidator
{
    public const int MaximumProfileNameLength = 32;

    public static bool TryNormalizeName(
        string? value,
        out string normalizedName)
    {
        normalizedName = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().ToLowerInvariant();

        if (candidate.Length > MaximumProfileNameLength ||
            !IsAlphaNumeric(candidate[0]) ||
            !IsAlphaNumeric(candidate[^1]))
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!IsAlphaNumeric(character) && character != '-')
            {
                return false;
            }
        }

        normalizedName = candidate;
        return true;
    }

    public static bool TryNormalizeServerUrl(
        string? value,
        out string normalizedServerUrl)
    {
        normalizedServerUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
        {
            return false;
        }

        var isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

        var isLoopbackHttp = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback;

        if (!isHttps && !isLoopbackHttp)
        {
            return false;
        }

        normalizedServerUrl = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static bool TryCreateCredentialKey(
        string? profileName,
        string? serverUrl,
        [NotNullWhen(true)] out TargetProfileCredentialKey? key)
    {
        key = null;

        if (!TryNormalizeName(profileName, out var normalizedProfileName) ||
            !TryNormalizeServerUrl(serverUrl, out var normalizedServerUrl))
        {
            return false;
        }

        key = new TargetProfileCredentialKey(
            normalizedProfileName,
            normalizedServerUrl);
        return true;
    }

    private static bool IsAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' ||
        value is >= '0' and <= '9';
}

public sealed record TargetProfileCredentialKey(
    string ProfileName,
    string ServerUrl);

/// <summary>
/// MEM CLI device credentials are 32 random bytes encoded as unpadded base64url.
/// </summary>
public static class TargetDeviceCredentialValidator
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

            return Convert.FromBase64String(base64).Length ==
                OpaqueCredentialByteLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
