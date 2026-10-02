using HostAgent.Matrix.Federation;
using HostAgent.Runtime.Manifests;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class RuntimeStackFederationStateClassifierTests
{
    [Fact]
    public void Classifies_supported_public_truth_as_healthy()
    {
        var result = RuntimeStackFederationStateClassifier.Classify(
            Manifest(),
            Config(FederationModes.Public),
            Container(),
            Ingress(FederationIngressModes.Normal));

        Assert.Equal(FederationModes.Public, result.Mode);
        Assert.Equal(FederationConfigurationStates.Healthy, result.ConfigurationState);
        Assert.True(result.ServerWellKnownPublished);
        Assert.False(result.MatrixDirectHostPortExposed);
        Assert.NotNull(result.StateFingerprint);
    }

    [Fact]
    public void Empty_synapse_allowlist_without_local_only_ingress_is_incomplete()
    {
        var result = RuntimeStackFederationStateClassifier.Classify(
            Manifest(),
            Config(FederationModes.LocalOnly),
            Container(),
            Ingress(FederationIngressModes.Normal));

        Assert.Equal(FederationModes.LocalOnly, result.Mode);
        Assert.Equal(FederationConfigurationStates.Incomplete, result.ConfigurationState);
        Assert.Contains(result.Problems, x => x.Code == "federation_local_only_incomplete");
    }

    [Fact]
    public void Local_only_ingress_with_public_synapse_config_is_reported_as_a_mismatch()
    {
        var result = RuntimeStackFederationStateClassifier.Classify(
            Manifest(),
            Config(FederationModes.Public),
            Container(),
            Ingress(FederationIngressModes.LocalOnly));

        Assert.Equal(FederationModes.Unknown, result.Mode);
        Assert.Equal(FederationConfigurationStates.Incomplete, result.ConfigurationState);
        Assert.Contains(result.Problems, x => x.Code == "federation_ingress_policy_mismatch");
    }

    [Fact]
    public void Direct_host_port_is_visible_as_a_warning_for_public_federation()
    {
        var result = RuntimeStackFederationStateClassifier.Classify(
            Manifest(),
            Config(FederationModes.Public),
            Container(directPort: true),
            Ingress(FederationIngressModes.Normal));

        Assert.Equal(FederationConfigurationStates.Healthy, result.ConfigurationState);
        Assert.Contains(result.Warnings, x => x.Code == "federation_direct_host_port_exposed");
    }

    [Fact]
    public void Direct_host_port_prevents_a_healthy_local_only_claim()
    {
        var result = RuntimeStackFederationStateClassifier.Classify(
            Manifest(),
            Config(FederationModes.LocalOnly),
            Container(directPort: true),
            Ingress(FederationIngressModes.LocalOnly));

        Assert.Equal(FederationConfigurationStates.Incomplete, result.ConfigurationState);
        Assert.Contains(result.Problems, x => x.Code == "federation_direct_host_port_exposed");
    }

    [Fact]
    public void Unsupported_synapse_representation_is_read_only_custom_state()
    {
        var config = new SynapseFederationConfigReadResult(
            SynapseFederationConfigKinds.CustomUnsupported,
            FederationModes.Unknown,
            [],
            "sha256:config",
            "federation_config_custom_unsupported",
            "Unsupported custom representation.");

        var result = RuntimeStackFederationStateClassifier.Classify(
            Manifest(),
            config,
            Container(),
            Ingress(FederationIngressModes.Normal));

        Assert.Equal(FederationModes.Unknown, result.Mode);
        Assert.Equal(FederationConfigurationStates.CustomUnsupported, result.ConfigurationState);
    }

    private static RuntimeStackManifest Manifest() =>
        new(
            Source: "control-plane",
            StackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
            Slug: "demo-stack",
            LastVerifiedStatus: "ready",
            LastVerifiedAtUtc: DateTimeOffset.Parse("2026-07-23T01:00:00Z"),
            Matrix: new RuntimeStackServiceManifest(
                InstanceId: Guid.NewGuid(),
                ServiceKey: "matrix",
                ContainerId: "container-id",
                ContainerName: "matrix-demo-stack",
                HostPort: 0,
                DataPath: "/srv/mem/demo-stack/matrix",
                ServerName: "matrix.example.test",
                PublicHost: "matrix.example.test",
                PublicBaseUrl: "https://matrix.example.test",
                InternalHost: "matrix-demo-stack",
                InternalBaseUrl: "http://matrix-demo-stack:8008",
                PublicRouteId: "12",
                InternalRouteId: null,
                NpmCertificateId: 2,
                RuntimeMetadata: new Dictionary<string, string?>
                {
                    ["runtimeNetworkName"] = "mem-gateway",
                    ["matrixImage"] = "matrixdotorg/synapse:latest"
                }),
            Element: null,
            Warnings: [],
            Metadata: new Dictionary<string, string?>());

    private static SynapseFederationConfigReadResult Config(string mode) => mode switch
    {
        FederationModes.Public => new(
            SynapseFederationConfigKinds.KeyAbsent,
            FederationModes.Public,
            [],
            "sha256:config",
            null,
            null),
        FederationModes.Restricted => new(
            SynapseFederationConfigKinds.ExactDomainList,
            FederationModes.Restricted,
            ["partner.example"],
            "sha256:config",
            null,
            null),
        _ => new(
            SynapseFederationConfigKinds.EmptyList,
            FederationModes.LocalOnly,
            [],
            "sha256:config",
            null,
            null)
    };

    private static FederationContainerObservation Container(bool directPort = false) =>
        new(
            Exists: true,
            Running: true,
            IdentityMatches: true,
            ExpectedNetworkAttached: true,
            DirectHostPortExposed: directPort,
            ContainerId: "container-id",
            ContainerName: "matrix-demo-stack",
            Image: "matrixdotorg/synapse:latest",
            ProblemCode: null,
            Detail: null);

    private static FederationIngressObservation Ingress(string mode) =>
        new(
            Available: true,
            CanonicalRouteFound: true,
            IngressMode: mode,
            RouteEnabled: true,
            RouteTargetsExpectedMatrix: true,
            CertificatePresent: true,
            ServerWellKnownPublished: mode == FederationIngressModes.Normal,
            FederationPathsPubliclyForwarded: mode == FederationIngressModes.Normal,
            SigningKeyPathsPubliclyForwarded: mode == FederationIngressModes.Normal,
            AlternateMatrixRouteDetected: false,
            RouteSnapshotSha256: "sha256:route",
            ProblemCode: null,
            Detail: null);
}
