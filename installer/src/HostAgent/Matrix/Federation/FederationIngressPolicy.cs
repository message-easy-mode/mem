using System.Security.Cryptography;
using System.Text;
using HostAgent.Runtime.Ingress;

namespace HostAgent.Matrix.Federation;

public static class FederationIngressPolicy
{
    public static string Classify(string? advancedConfig)
    {
        var candidate = Normalize(advancedConfig);
        if (string.Equals(candidate, Normalize(IngressAdvancedConfigTemplates.MatrixWellKnown()), StringComparison.Ordinal))
        {
            return FederationIngressModes.Normal;
        }

        if (string.Equals(candidate, Normalize(IngressAdvancedConfigTemplates.MatrixLocalOnly()), StringComparison.Ordinal))
        {
            return FederationIngressModes.LocalOnly;
        }

        return FederationIngressModes.CustomUnsupported;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();

        return string.Join("\n", lines).Trim();
    }

    public static string ComputeSnapshotHash(params string?[] values)
    {
        var canonical = string.Join("\n", values.Select(value => value?.Trim() ?? string.Empty));
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
