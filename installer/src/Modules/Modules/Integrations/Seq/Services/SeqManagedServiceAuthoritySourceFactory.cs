using Core.RuntimeDefinition;
using Modules.Integrations.Seq.Contracts;
using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Services;

public static class SeqManagedServiceAuthoritySourceFactory
{
    public static MemManagedServiceAuthoritySource Create(
        SeqDiagnosticsOptions options,
        SeqStatusResponse? runtime = null,
        SeqRuntimeContextProfile? runtimeProfile = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new MemManagedServiceAuthoritySource(
            ServiceName: ManagedServiceNames.Seq,
            PublishedHostPort: runtime?.UiHostPort,
            DockerNetworkAlias:
                runtimeProfile?.DockerNetworkAlias ?? ManagedNetworkAliases.Seq,
            HealthPort: 80,
            AdministrationPort: 80,
            IngestionPort: 5341,
            BrowserAuthority: Normalize(options.UiUrl),
            ConfiguredHealthAuthority: Normalize(options.HealthUrl),
            ConfiguredAdministrationAuthority: Normalize(options.HealthUrl),
            ConfiguredIngestionAuthority: Normalize(options.IngestionUrl));
    }

    public static MemManagedServiceAuthoritySource CreateForBootstrap(
        SeqDiagnosticsOptions options,
        int? selectedHostPort,
        SeqRuntimeContextProfile? runtimeProfile = null) =>
        Create(
            options,
            new SeqStatusResponse(
                ServiceName: ManagedServiceNames.Seq,
                ContainerName:
                    runtimeProfile?.ContainerName ?? ManagedContainerNames.Seq,
                ExpectedVersion: options.ExpectedVersion,
                HostDataPath: runtimeProfile?.HostDataPath ?? options.HostDataPath,
                UiHostPort: selectedHostPort,
                Exists: selectedHostPort is > 0,
                Running: selectedHostPort is > 0,
                State: selectedHostPort is > 0 ? "running" : null,
                Image: null,
                UsesApprovedRuntime: true,
                Warnings: [],
                Managed: true,
                OwnershipState: "managed",
                WarningCode: null),
            runtimeProfile);

    private static Uri? Normalize(string? value) =>
        SeqDiagnosticsOptionsValidator.TryNormalizeUrl(value, out var uri)
            ? uri
            : null;
}
