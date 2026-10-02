using HostAgent.Runtime.Coturn;
using Modules.Shared.RuntimeImages;
using Modules.Setup.Platform.Coturn;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnRuntimeInstallationBoundaryTests
{
    private static readonly ApprovedCoturnRuntimeDescriptor Descriptor = new(
        ApprovedReference: "coturn/coturn@sha256:0c0e8fc0c263b85a134e9e4242b5e46e1f4c077c5029633511191c05b5c2c814",
        ResolvedImageId: "sha256:4cecab6dc16fc24ceff2742648282dbfaf02a1549b0d9de2d7f3cfa2a4a42924",
        RepositoryDigests: [],
        ExpectedVersion: "4.14.0",
        VerifiedAtUtc: new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Setup_installation_uses_the_installation_image_boundary()
    {
        var provider = new RecordingProvider();

        var result = await CoturnRuntimeService.ResolveApprovedImageAsync(
            provider,
            CoturnRuntimeImageBoundary.Installation,
            CancellationToken.None);

        Assert.Same(Descriptor, result);
        Assert.Equal(1, provider.InstallationCalls);
        Assert.Equal(0, provider.OperationCalls);
    }

    [Fact]
    public void Setup_installation_always_publishes_the_full_relay_range()
    {
        var request = CoturnRuntimeService.CreateSetupEnsureRequest(
            new PlatformCoturnSetupRequest(
                ExternalIp: "203.0.113.10",
                ForceRecreate: true));

        Assert.Equal("203.0.113.10", request.ExternalIp);
        Assert.True(request.ForceRecreate);
        Assert.True(request.PublishRelayPorts);
    }

    [Fact]
    public async Task Normal_runtime_work_keeps_the_no_pull_operational_boundary()
    {
        var provider = new RecordingProvider();

        var result = await CoturnRuntimeService.ResolveApprovedImageAsync(
            provider,
            CoturnRuntimeImageBoundary.Operation,
            CancellationToken.None);

        Assert.Same(Descriptor, result);
        Assert.Equal(0, provider.InstallationCalls);
        Assert.Equal(1, provider.OperationCalls);
    }

    [Theory]
    [InlineData(null, "auto-detect", "--external-ip=$(detect-external-ip)")]
    [InlineData("8.8.8.8", "explicit", "--external-ip=8.8.8.8")]
    public void Created_runtime_persists_mode_and_address_without_changing_image_secret_mount_or_ports(
        string? externalIp, string mode, string argument)
    {
        var spec = CoturnRuntimeService.BuildContainerSpec(
            Descriptor, "example.test", "turn.example.test", externalIp,
            true, "existing-protected-config-hash", "/protected/turnserver.conf", "mem-gateway");

        Assert.Equal(Descriptor.ResolvedImageId, spec.Image);
        Assert.True(CoturnRuntimePolicy.IsOwned(spec.Labels));
        Assert.Equal(mode, spec.Labels!["mem.coturn.externalIpMode"]);
        if (externalIp is null) Assert.False(spec.Labels.ContainsKey("mem.coturn.externalIp"));
        else Assert.Equal(externalIp, spec.Labels["mem.coturn.externalIp"]);
        Assert.Equal("existing-protected-config-hash", spec.Labels["mem.coturn.configSha256"]);
        Assert.Contains(argument, spec.Cmd!);
        Assert.Equal(4, spec.Cmd!.Count);
        Assert.Null(spec.Env); // No unsupported environment-only detection contract.
        Assert.Equal(CoturnRuntimePolicy.StartupUser, spec.User);
        Assert.Equal("mem-gateway", spec.NetworkName);
        var mount = Assert.Single(spec.BindMounts!);
        Assert.Equal("/protected/turnserver.conf", mount.HostPath);
        Assert.Equal(CoturnRuntimePolicy.ContainerConfigPath, mount.ContainerPath);
        Assert.True(mount.ReadOnly);
        Assert.Equal("3478", spec.PortBindings!["3478/udp"]);
        Assert.Equal("3478", spec.PortBindings["3478/tcp"]);
        for (var port = 49160; port <= 49200; port++)
            Assert.Equal(port.ToString(), spec.PortBindings[$"{port}/udp"]);
    }

    [Fact]
    public void Clearing_an_explicit_setting_builds_a_fresh_automatic_contract_without_old_ip_labels()
    {
        var explicitSpec = CoturnRuntimeService.BuildContainerSpec(
            Descriptor, "example.test", "turn.example.test", "8.8.8.8",
            true, "same-hash", "/protected/turnserver.conf", "mem-gateway");
        var automaticSpec = CoturnRuntimeService.BuildContainerSpec(
            Descriptor, "example.test", "turn.example.test", null,
            true, "same-hash", "/protected/turnserver.conf", "mem-gateway");

        Assert.Equal("8.8.8.8", explicitSpec.Labels!["mem.coturn.externalIp"]);
        Assert.False(automaticSpec.Labels!.ContainsKey("mem.coturn.externalIp"));
        Assert.Contains("--external-ip=$(detect-external-ip)", automaticSpec.Cmd!);
        Assert.Equal(explicitSpec.Labels["mem.coturn.configSha256"], automaticSpec.Labels["mem.coturn.configSha256"]);
        Assert.Equal(explicitSpec.Image, automaticSpec.Image);
    }

    private sealed class RecordingProvider : IApprovedCoturnRuntimeProvider
    {
        public int InstallationCalls { get; private set; }
        public int OperationCalls { get; private set; }

        public ApprovedCoturnRuntimePolicy GetPolicy() => new(
            Descriptor.ApprovedReference,
            Descriptor.ExpectedVersion,
            AllowInstallPull: true,
            AllowOperationalPull: false);

        public Task<ApprovedCoturnRuntimeDescriptor> ResolveForInstallationAsync(
            CancellationToken cancellationToken)
        {
            InstallationCalls++;
            return Task.FromResult(Descriptor);
        }

        public Task<ApprovedCoturnRuntimeDescriptor> ResolveForOperationAsync(
            CancellationToken cancellationToken)
        {
            OperationCalls++;
            return Task.FromResult(Descriptor);
        }
    }
}
