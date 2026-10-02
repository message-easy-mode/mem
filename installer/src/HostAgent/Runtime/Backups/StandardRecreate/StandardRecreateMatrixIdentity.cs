using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Resolves the immutable Matrix server identity stored in a backup manifest.
/// A Standard Recreate restores the same Synapse database, so it must preserve
/// this identity instead of treating the Matrix hostname as a new target.
/// </summary>
public static class StandardRecreateMatrixIdentity
{
    public static string? Resolve(MemStackExportManifestSummary? manifest) =>
        manifest is null
            ? null
            : FirstKnownHost(
                manifest.Stack.MatrixServerName,
                manifest.Routes.MatrixHost,
                manifest.Stack.MatrixPublicUrl);

    public static string? Resolve(MemStackExportManifest? manifest) =>
        manifest is null
            ? null
            : FirstKnownHost(
                manifest.Stack.MatrixServerName,
                manifest.Routes.MatrixHost,
                manifest.Stack.MatrixPublicUrl);

    public static bool Matches(string? suppliedHost, string sourceMatrixIdentity)
    {
        var normalizedSuppliedHost = NormalizeHost(suppliedHost);
        return string.IsNullOrWhiteSpace(normalizedSuppliedHost) ||
               string.Equals(
                   normalizedSuppliedHost,
                   sourceMatrixIdentity,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.TrimEnd('.').ToLowerInvariant();
        }

        return trimmed
            .Trim('/')
            .TrimEnd('.')
            .ToLowerInvariant();
    }

    public static string CreateMismatchMessage(string sourceMatrixIdentity) =>
        $"This backup belongs to Matrix server '{sourceMatrixIdentity}'. A standard restore preserves that Matrix server identity and cannot use a different Matrix public host.";

    private static string? FirstKnownHost(params string?[] candidates) =>
        candidates
            .Select(NormalizeHost)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
