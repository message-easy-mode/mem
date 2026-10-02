using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;

namespace Modules.Integrations.Seq.Services;

public static partial class SeqDiagnosticsOptionsValidator
{
    public static IReadOnlyList<string> Validate(SeqDiagnosticsOptions options) =>
        ValidateInternal(options, includeManagementRequirements: true);

    public static IReadOnlyList<string> ValidateForSetup(SeqDiagnosticsOptions options) =>
        ValidateInternal(options, includeManagementRequirements: false);

    public static void ThrowIfInvalidForManagement(SeqDiagnosticsOptions options)
    {
        var errors = Validate(options);
        if (errors.Count > 0)
        {
            throw new SeqOperationException(
                "seq_configuration_invalid",
                StatusCodes.Status409Conflict,
                string.Join(" ", errors));
        }
    }

    public static void ThrowIfInvalidForSetup(SeqDiagnosticsOptions options)
    {
        var errors = ValidateForSetup(options);
        if (errors.Count > 0)
        {
            throw new SeqOperationException(
                "seq_setup_configuration_invalid",
                StatusCodes.Status409Conflict,
                string.Join(" ", errors));
        }
    }

    public static bool TryNormalizeUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrWhiteSpace(parsed.UserInfo) ||
            !string.IsNullOrWhiteSpace(parsed.Query) ||
            !string.IsNullOrWhiteSpace(parsed.Fragment))
        {
            return false;
        }

        uri = new UriBuilder(
            parsed.Scheme,
            parsed.Host,
            parsed.IsDefaultPort ? -1 : parsed.Port,
            parsed.AbsolutePath.TrimEnd('/')).Uri;
        return true;
    }

    public static bool TryNormalizeAbsoluteFilePath(
        string? value,
        out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var normalized = Path.GetFullPath(value);
            if (string.IsNullOrWhiteSpace(Path.GetFileName(normalized)))
            {
                return false;
            }

            fullPath = normalized;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
                PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ValidateInternal(
        SeqDiagnosticsOptions options,
        bool includeManagementRequirements)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (options.SinkEnabled)
        {
            ValidateUrl(
                options.IngestionUrl,
                "Diagnostics:Seq:IngestionUrl",
                required: true,
                errors);

            if (string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariableName) &&
                string.IsNullOrWhiteSpace(options.ApiKeyFilePath))
            {
                errors.Add("Diagnostics:Seq requires an API-key environment variable name or secret-file path when the sink is enabled.");
            }
        }
        else
        {
            ValidateUrl(
                options.IngestionUrl,
                "Diagnostics:Seq:IngestionUrl",
                required: false,
                errors);
        }

        ValidateUrl(
            options.UiUrl,
            "Diagnostics:Seq:UiUrl",
            required: false,
            errors);
        ValidateUrl(
            options.HealthUrl,
            "Diagnostics:Seq:HealthUrl",
            required: options.SinkEnabled || options.ManagementEnabled,
            errors);

        if (options.ProbeTimeoutSeconds is < 1 or > 30)
        {
            errors.Add("Diagnostics:Seq:ProbeTimeoutSeconds must be between 1 and 30.");
        }

        if (options.ProbeIntervalSeconds is < 10 or > 3600)
        {
            errors.Add("Diagnostics:Seq:ProbeIntervalSeconds must be between 10 and 3600.");
        }

        if (options.PasswordHashTimeoutSeconds is < 5 or > 120)
        {
            errors.Add("Diagnostics:Seq:PasswordHashTimeoutSeconds must be between 5 and 120.");
        }

        if (options.BootstrapHealthAttemptCount is < 1 or > 120)
        {
            errors.Add("Diagnostics:Seq:BootstrapHealthAttemptCount must be between 1 and 120.");
        }

        if (options.BootstrapHealthPollIntervalSeconds is < 1 or > 30)
        {
            errors.Add("Diagnostics:Seq:BootstrapHealthPollIntervalSeconds must be between 1 and 30.");
        }

        if (options.PreferredHostPort is < 1 or > 65535)
        {
            errors.Add("Diagnostics:Seq:PreferredHostPort must be between 1 and 65535.");
        }

        if (options.AllowOperationalPull)
        {
            errors.Add("Diagnostics:Seq:AllowOperationalPull must remain false.");
        }

        ValidateStateFilePath(
            options.DeliveryStatePath,
            "Diagnostics:Seq:DeliveryStatePath",
            errors);
        ValidateStateFilePath(
            options.BootstrapStatePath,
            "Diagnostics:Seq:BootstrapStatePath",
            errors);
        ValidateHostDataPath(options.HostDataPath, errors);
        ValidateSecretPaths(options, errors);

        var image = options.ApprovedImageReference?.Trim() ?? string.Empty;
        var expectedVersion = options.ExpectedVersion?.Trim() ?? string.Empty;

        if (!ExactSeqImageRegex().IsMatch(image))
        {
            errors.Add("Diagnostics:Seq:ApprovedImageReference must be an exact datalust/seq patch tag.");
        }
        else if (!image.EndsWith($":{expectedVersion}", StringComparison.Ordinal))
        {
            errors.Add("Diagnostics:Seq:ApprovedImageReference must match Diagnostics:Seq:ExpectedVersion.");
        }

        if (includeManagementRequirements && options.ManagementEnabled)
        {
            if (!options.EulaAccepted)
            {
                errors.Add("Diagnostics:Seq:EulaAccepted must be true before MEM-managed Seq deployment is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.AdminPasswordHashEnvironmentVariableName) &&
                string.IsNullOrWhiteSpace(options.AdminPasswordHashFilePath))
            {
                errors.Add("Diagnostics:Seq requires an administrator-password-hash environment variable name or secret-file path when management is enabled.");
            }
        }

        return errors;
    }

    private static void ValidateUrl(
        string? value,
        string name,
        bool required,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{name} is required.");
            }

            return;
        }

        if (!TryNormalizeUrl(value, out _))
        {
            errors.Add($"{name} must be an absolute HTTP or HTTPS URL without embedded credentials, query, or fragment.");
        }
    }

    private static void ValidateStateFilePath(
        string? value,
        string name,
        ICollection<string> errors)
    {
        if (!TryNormalizeAbsoluteFilePath(value, out _))
        {
            errors.Add($"{name} must identify a file path.");
        }
    }

    private static void ValidateSecretPaths(
        SeqDiagnosticsOptions options,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.SecretRootPath))
        {
            errors.Add("Diagnostics:Seq:SecretRootPath is required.");
            return;
        }

        string root;
        try
        {
            root = Path.GetFullPath(options.SecretRootPath);
            var fileSystemRoot = Path.GetPathRoot(root);
            if (string.Equals(
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    fileSystemRoot?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                errors.Add("Diagnostics:Seq:SecretRootPath cannot be a filesystem root.");
                return;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
                PathTooLongException)
        {
            errors.Add("Diagnostics:Seq:SecretRootPath is invalid.");
            return;
        }

        ValidateSecretFilePath(
            options.ApiKeyFilePath,
            "Diagnostics:Seq:ApiKeyFilePath",
            root,
            errors);
        ValidateSecretFilePath(
            options.AdminPasswordHashFilePath,
            "Diagnostics:Seq:AdminPasswordHashFilePath",
            root,
            errors);
    }

    private static void ValidateSecretFilePath(
        string? value,
        string name,
        string root,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!TryNormalizeAbsoluteFilePath(value, out var fullPath) ||
            fullPath is null ||
            !IsPathWithinRoot(root, fullPath))
        {
            errors.Add($"{name} must identify a file beneath Diagnostics:Seq:SecretRootPath.");
        }
    }

    private static void ValidateHostDataPath(
        string? value,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("Diagnostics:Seq:HostDataPath is required.");
            return;
        }

        try
        {
            // Relative paths are supported for Development and are resolved
            // against the API content root by SeqFileSystemSafety at the point
            // of filesystem or Docker use. Validation here remains a
            // side-effect-free shape check.
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(
                    fullPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    root?.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.Ordinal))
            {
                errors.Add("Diagnostics:Seq:HostDataPath cannot be a filesystem root.");
            }
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add("Diagnostics:Seq:HostDataPath is invalid.");
        }
    }

    [GeneratedRegex(
        @"^datalust/seq:(?<version>[0-9]{4}\.[0-9]+\.[0-9]+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExactSeqImageRegex();
}

public sealed class SeqOperationException(
    string code,
    int statusCode,
    string message) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
