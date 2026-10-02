using System.Text.RegularExpressions;

namespace Mem.Migrate.Core.Security;

public static partial class AssessmentRedactor
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "password",
        "passwd",
        "token",
        "secret",
        "privatekey",
        "private_key",
        "authorization",
        "cookie",
        "credential",
        "machinename",
        "currentuser"
    ];

    public static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveKeyFragments.Any(fragment =>
            normalized.Contains(
                fragment.Replace("_", string.Empty, StringComparison.Ordinal),
                StringComparison.Ordinal));
    }

    public static string RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = BearerTokenRegex().Replace(value, "$1<redacted>");
        redacted = AssignmentSecretRegex().Replace(redacted, "$1<redacted>");
        redacted = UriCredentialRegex().Replace(redacted, "$1<redacted>@");
        return redacted;
    }

    public static string DisplayPath(string? path, bool includeSensitivePaths)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        if (includeSensitivePaths)
        {
            return RedactText(path);
        }

        var trimmed = path.Trim();
        var fileName = Path.GetFileName(trimmed.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "root";
        }

        return $"<path:{fileName}>";
    }

    [GeneratedRegex(@"(?i)\b(Bearer\s+)[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        @"(?i)\b(password|passwd|token|secret|authorization|cookie|credential)\s*([=:]\s*)[^\s,;]+")]
    private static partial Regex AssignmentSecretRegex();

    [GeneratedRegex(@"(?i)(https?://[^:/@\s]+:)[^@\s]+@")]
    private static partial Regex UriCredentialRegex();
}
