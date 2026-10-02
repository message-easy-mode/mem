using Microsoft.Extensions.Hosting;
namespace Modules.Integrations.Seq.Services;

public sealed record SeqSecretResolution(
    bool Available,
    string? Value,
    string? WarningCode);

public sealed class SeqSecretResolver(IHostEnvironment environment)
{
    public SeqSecretResolution ResolveApiKey(SeqDiagnosticsOptions options) =>
        Resolve(
            options.ApiKeyEnvironmentVariableName,
            options.ApiKeyFilePath,
            "diagnostics.seq_api_key_unavailable");

    public SeqSecretResolution ResolveAdminPasswordHash(
        SeqDiagnosticsOptions options) =>
        Resolve(
            options.AdminPasswordHashEnvironmentVariableName,
            options.AdminPasswordHashFilePath,
            "diagnostics.seq_admin_password_hash_unavailable");

    public static SeqSecretResolution ResolveApiKey(
        SeqDiagnosticsOptions options,
        string contentRootPath) =>
        Resolve(
            options.ApiKeyEnvironmentVariableName,
            options.ApiKeyFilePath,
            contentRootPath,
            "diagnostics.seq_api_key_unavailable");

    private SeqSecretResolution Resolve(
        string? environmentVariableName,
        string? configuredPath,
        string warningCode) =>
        Resolve(
            environmentVariableName,
            configuredPath,
            environment.ContentRootPath,
            warningCode);

    private static SeqSecretResolution Resolve(
        string? environmentVariableName,
        string? configuredPath,
        string contentRootPath,
        string warningCode)
    {
        if (!string.IsNullOrWhiteSpace(environmentVariableName))
        {
            var value = Environment.GetEnvironmentVariable(
                environmentVariableName.Trim());
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new SeqSecretResolution(
                    Available: true,
                    Value: value.Trim(),
                    WarningCode: null);
            }
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var fullPath = Path.IsPathRooted(configuredPath)
                    ? Path.GetFullPath(configuredPath)
                    : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
                SeqFileSystemSafety.EnsurePathContainsNoLinks(
                    fullPath,
                    warningCode);
                SeqFileSystemSafety.EnsureTargetIsNotLink(
                    fullPath,
                    warningCode);

                if (File.Exists(fullPath))
                {
                    var value = File.ReadAllText(fullPath).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return new SeqSecretResolution(
                            Available: true,
                            Value: value,
                            WarningCode: null);
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or
                    ArgumentException or NotSupportedException or
                    SeqOperationException)
            {
            }
        }

        return new SeqSecretResolution(
            Available: false,
            Value: null,
            WarningCode: warningCode);
    }
}
