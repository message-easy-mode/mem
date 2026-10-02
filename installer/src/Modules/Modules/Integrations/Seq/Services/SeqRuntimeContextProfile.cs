using Core.RuntimeDefinition;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Services;

/// <summary>
/// Server-owned Seq identity for the active MEM Control Plane runtime context.
/// Production retains the established Seq identity. Local-source and
/// containerized development use distinct Docker names, network aliases,
/// host ports, data paths, secret roots, and state files so both environments
/// can retain their own Seq runtime without sharing lifecycle or event history.
/// </summary>
public sealed record SeqRuntimeContextProfile(
    string RuntimeMode,
    string ContainerName,
    string DockerNetworkAlias,
    int PreferredHostPort,
    string HostDataPath,
    string SecretRootPath,
    string? ApiKeyFilePath,
    string? AdminPasswordHashFilePath,
    string BootstrapStatePath,
    string DeliveryStatePath,
    bool ContextScoped)
{
    public const int LocalDevelopmentPreferredHostPort = 16341;
    public const int ContainerizedDevelopmentPreferredHostPort = 17341;

    public static SeqRuntimeContextProfile Create(
        MemControlPlaneRuntimeContext runtimeContext,
        SeqDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        ArgumentNullException.ThrowIfNull(options);

        var (containerName, networkAlias, scope, defaultPort) =
            runtimeContext.RuntimeMode switch
            {
                MemRuntimeModes.LocalDevelopment => (
                    "mem-seq-local",
                    "seq-local",
                    "local",
                    LocalDevelopmentPreferredHostPort),
                MemRuntimeModes.ContainerizedDevelopment => (
                    "mem-seq-dev",
                    "seq-dev",
                    "dev",
                    ContainerizedDevelopmentPreferredHostPort),
                _ => (
                    ManagedContainerNames.Seq,
                    ManagedNetworkAliases.Seq,
                    (string?)null,
                    options.PreferredHostPort)
            };

        if (scope is null)
        {
            return new SeqRuntimeContextProfile(
                runtimeContext.RuntimeMode,
                containerName,
                networkAlias,
                options.PreferredHostPort,
                options.HostDataPath,
                options.SecretRootPath,
                options.ApiKeyFilePath,
                options.AdminPasswordHashFilePath,
                options.BootstrapStatePath,
                options.DeliveryStatePath,
                ContextScoped: false);
        }

        var preferredHostPort = options.PreferredHostPort ==
                                SeqDiagnosticsOptions.DefaultPreferredHostPort
            ? defaultPort
            : options.PreferredHostPort;
        var scopedSecretRoot = ScopeDirectorySibling(options.SecretRootPath, scope);

        return new SeqRuntimeContextProfile(
            runtimeContext.RuntimeMode,
            containerName,
            networkAlias,
            preferredHostPort,
            ScopeDirectorySibling(options.HostDataPath, scope),
            scopedSecretRoot,
            ScopeSecretFile(
                options.SecretRootPath,
                scopedSecretRoot,
                options.ApiKeyFilePath),
            ScopeSecretFile(
                options.SecretRootPath,
                scopedSecretRoot,
                options.AdminPasswordHashFilePath),
            ScopeFileSibling(options.BootstrapStatePath, scope),
            ScopeFileSibling(options.DeliveryStatePath, scope),
            ContextScoped: true);
    }

    public static SeqRuntimeContextProfile CreateLegacy(
        SeqDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SeqRuntimeContextProfile(
            RuntimeMode: "legacy",
            ContainerName: ManagedContainerNames.Seq,
            DockerNetworkAlias: ManagedNetworkAliases.Seq,
            PreferredHostPort: options.PreferredHostPort,
            HostDataPath: options.HostDataPath,
            SecretRootPath: options.SecretRootPath,
            ApiKeyFilePath: options.ApiKeyFilePath,
            AdminPasswordHashFilePath: options.AdminPasswordHashFilePath,
            BootstrapStatePath: options.BootstrapStatePath,
            DeliveryStatePath: options.DeliveryStatePath,
            ContextScoped: false);
    }

    /// <summary>
    /// Applies the active runtime-context defaults to configuration before
    /// Serilog and Seq state stores are created. Explicit non-default ports
    /// remain operator-controlled; production and automated-test configuration
    /// are intentionally unchanged in this slice.
    /// </summary>
    public static SeqRuntimeContextProfile ApplyConfigurationOverrides(
        ConfigurationManager configuration,
        MemControlPlaneRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(runtimeContext);

        var options = new SeqDiagnosticsOptions();
        configuration
            .GetSection(SeqDiagnosticsOptions.SectionName)
            .Bind(options);
        var profile = Create(runtimeContext, options);
        if (!profile.ContextScoped)
        {
            return profile;
        }

        var authoritySource =
            SeqManagedServiceAuthoritySourceFactory.CreateForBootstrap(
                options,
                profile.PreferredHostPort,
                profile);
        var ingestionUrl = ResolveContextInternalAuthority(
            options.IngestionUrl,
            expectedDefaultPort: 5341,
            runtimeContext,
            authoritySource,
            MemManagedServicePurposes.Ingestion);
        var healthUrl = ResolveContextInternalAuthority(
            options.HealthUrl,
            expectedDefaultPort: 80,
            runtimeContext,
            authoritySource,
            MemManagedServicePurposes.Health);

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:PreferredHostPort"] =
                profile.PreferredHostPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"] = ingestionUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"] = healthUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HostDataPath"] =
                profile.HostDataPath,
            [$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"] =
                profile.SecretRootPath,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyFilePath"] =
                profile.ApiKeyFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashFilePath"] =
                profile.AdminPasswordHashFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                profile.BootstrapStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                profile.DeliveryStatePath
        });

        return profile;
    }

    private static string? ResolveContextInternalAuthority(
        string? configuredValue,
        int expectedDefaultPort,
        MemControlPlaneRuntimeContext runtimeContext,
        MemManagedServiceAuthoritySource authoritySource,
        string purpose)
    {
        if (!IsCanonicalDefaultAuthority(configuredValue, expectedDefaultPort))
        {
            return configuredValue;
        }

        return MemManagedServiceAuthorityResolver.Resolve(
            runtimeContext,
            purpose,
            authoritySource).Authority.ToString();
    }

    private static bool IsCanonicalDefaultAuthority(
        string? configuredValue,
        int expectedPort)
    {
        if (!Uri.TryCreate(configuredValue, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, ManagedNetworkAliases.Seq, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == expectedPort &&
               string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal);
    }

    private static string ScopeDirectorySibling(string path, string scope)
    {
        if (string.IsNullOrWhiteSpace(path) || IsFileSystemRoot(path))
        {
            return path;
        }

        try
        {
            var trimmed = path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(name) ||
                name.EndsWith($"-{scope}", StringComparison.Ordinal))
            {
                return path;
            }

            var parent = Path.GetDirectoryName(trimmed);
            var scopedName = $"{name}-{scope}";
            return string.IsNullOrWhiteSpace(parent)
                ? scopedName
                : Path.Combine(parent, scopedName);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            // Preserve invalid input so the existing Seq options validator can
            // reject it rather than accidentally turning it into a valid path.
            return path;
        }
    }

    private static string ScopeFileSibling(string path, string scope)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path);
            var extension = Path.GetExtension(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith($"-{scope}", StringComparison.Ordinal))
            {
                return path;
            }

            var scopedName = $"{stem}-{scope}{extension}";
            return string.IsNullOrWhiteSpace(directory)
                ? scopedName
                : Path.Combine(directory, scopedName);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return path;
        }
    }

    private static bool IsFileSystemRoot(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(
                fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                root?.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return false;
        }
    }

    private static bool IsPathException(Exception exception) =>
        exception is ArgumentException or NotSupportedException or PathTooLongException;

    private static string? ScopeSecretFile(
        string originalRoot,
        string scopedRoot,
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        try
        {
            var root = Path.GetFullPath(originalRoot);
            var file = Path.GetFullPath(filePath);
            var relative = Path.GetRelativePath(root, file);
            if (relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                return filePath;
            }

            return Path.Combine(scopedRoot, relative);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            // Preserve the configured value. The existing Seq options validator
            // remains authoritative for rejecting an invalid secret path.
            return filePath;
        }
    }
}
