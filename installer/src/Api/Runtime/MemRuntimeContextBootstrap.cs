using System.Reflection;
using Shared.ControlPlane;
using Shared.ControlPlane.Runtime;

namespace Api.Runtime;

public static class MemRuntimeContextBootstrap
{
    public static MemControlPlaneRuntimeContext Create(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new MemRuntimeContextOptions();
        configuration
            .GetSection(MemRuntimeContextOptions.SectionName)
            .Bind(options);

        options.Mode = FirstNonEmpty(
            configuration[MemControlPlaneIdentity.EnvironmentVariables.RuntimeMode],
            options.Mode) ?? string.Empty;
        options.StateRoot = FirstNonEmpty(
            configuration[MemControlPlaneIdentity.EnvironmentVariables.StateRoot],
            options.StateRoot) ?? string.Empty;
        options.ExpectedInstanceId = FirstNonEmpty(
            configuration[MemControlPlaneIdentity.EnvironmentVariables.InstanceId],
            options.ExpectedInstanceId);
        options.HostAccessIpv4 = FirstNonEmpty(
            configuration[MemControlPlaneIdentity.EnvironmentVariables.HostIpv4],
            options.HostAccessIpv4);
        options.AllowSharedDockerHost = IsTrue(FirstNonEmpty(
            configuration[MemControlPlaneIdentity.EnvironmentVariables.AllowSharedDockerHost],
            configuration[$"{MemRuntimeContextOptions.SectionName}:AllowSharedDockerHost"]));

        var snapshot = new MemRuntimeEnvironmentSnapshot(
            EnvironmentName: environment.EnvironmentName,
            ApplicationName: MemControlPlaneIdentity.CanonicalContainerName,
            ContentRootPath: environment.ContentRootPath,
            WebRootPath: environment.WebRootPath,
            RunningInContainer: IsTrue(
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")),
            DevelopmentSetupTokenConfigured: !string.IsNullOrWhiteSpace(
                configuration["InstallerAuth:DevelopmentSetupToken"]),
            Version: ResolveVersion(configuration),
            Commit: FirstNonEmpty(
                Environment.GetEnvironmentVariable(
                    MemControlPlaneIdentity.EnvironmentVariables.CommitSha),
                Environment.GetEnvironmentVariable("SOURCE_COMMIT"),
                configuration["Product:Commit"]));

        return MemControlPlaneRuntimeContextFactory.Create(options, snapshot);
    }

    private static string ResolveVersion(IConfiguration configuration) =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable(
                MemControlPlaneIdentity.EnvironmentVariables.ProductVersion),
            configuration["Product:Version"],
            configuration["Mem:TargetVersion"],
            configuration["Mem:ProductVersion"],
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString())
        ?? "unknown";

    private static bool IsTrue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    private static string? FirstNonEmpty(params string?[] values) =>
        values
            .Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
