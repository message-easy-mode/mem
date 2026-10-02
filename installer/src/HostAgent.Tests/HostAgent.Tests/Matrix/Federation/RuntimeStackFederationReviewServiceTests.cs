using HostAgent.Matrix.Federation;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class RuntimeStackFederationReviewServiceTests
{
    [Fact]
    public async Task Canonicalises_restricted_domains_and_reports_exact_changes()
    {
        var service = Service(State(
            mode: FederationModes.Restricted,
            allowlist: ["old.example", "shared.example"]));

        var review = await service.ReviewAsync(
            "demo-stack",
            new RuntimeStackFederationPolicyRequest(
                " Restricted ",
                ["Shared.Example", "Partner.Example."]),
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal("ready", review!.Status);
        Assert.Equal(FederationModes.Restricted, review.ProposedMode);
        Assert.Equal(["partner.example", "shared.example"], review.CanonicalAllowlist);
        Assert.Equal(["partner.example"], review.AddedDomains);
        Assert.Equal(["old.example"], review.RemovedDomains);
        Assert.True(review.RestartRequired);
        Assert.False(review.IngressChangeRequired);
        Assert.False(review.NoChange);
        Assert.StartsWith("sha256:", review.ReviewHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_no_change_for_the_exact_current_public_policy()
    {
        var service = Service(State());

        var review = await service.ReviewAsync(
            "demo-stack",
            new RuntimeStackFederationPolicyRequest("public", []),
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal("no_change", review!.Status);
        Assert.True(review.NoChange);
        Assert.False(review.RestartRequired);
        Assert.False(review.IngressChangeRequired);
        Assert.Empty(review.Warnings);
    }

    [Fact]
    public async Task Review_hash_is_bound_to_current_state_and_canonical_request()
    {
        var request = new RuntimeStackFederationPolicyRequest(
            "restricted",
            ["Partner.Example."]);

        var first = await Service(State(fingerprint: "sha256:first"))
            .ReviewAsync("demo-stack", request, CancellationToken.None);
        var canonicalReplay = await Service(State(fingerprint: "sha256:first"))
            .ReviewAsync(
                "demo-stack",
                new RuntimeStackFederationPolicyRequest("restricted", ["partner.example"]),
                CancellationToken.None);
        var changedState = await Service(State(fingerprint: "sha256:second"))
            .ReviewAsync("demo-stack", request, CancellationToken.None);

        Assert.Equal(first!.ReviewHash, canonicalReplay!.ReviewHash);
        Assert.NotEqual(first.ReviewHash, changedState!.ReviewHash);
    }

    [Fact]
    public async Task Public_rejects_a_submitted_allowlist()
    {
        var service = Service(State());

        var exception = await Assert.ThrowsAsync<FederationPolicyValidationException>(() =>
            service.ReviewAsync(
                "demo-stack",
                new RuntimeStackFederationPolicyRequest("public", ["partner.example"]),
                CancellationToken.None));

        Assert.Contains("empty allowlist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restricted_requires_at_least_one_exact_domain()
    {
        var service = Service(State());

        await Assert.ThrowsAsync<FederationPolicyValidationException>(() =>
            service.ReviewAsync(
                "demo-stack",
                new RuntimeStackFederationPolicyRequest("restricted", []),
                CancellationToken.None));
    }

    [Fact]
    public async Task Local_only_review_requires_ingress_change_and_exposes_safety_caveats()
    {
        var service = Service(State());

        var review = await service.ReviewAsync(
            "demo-stack",
            new RuntimeStackFederationPolicyRequest("local_only", []),
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(FederationModes.LocalOnly, review!.ProposedMode);
        Assert.True(review.RestartRequired);
        Assert.True(review.IngressChangeRequired);
        Assert.Empty(review.CanonicalAllowlist);
        Assert.Contains(review.Warnings, x => x.Code == "federation_local_only_client_access_remains_public");
        Assert.Contains(review.Warnings, x => x.Code == "federation_historical_state_retained");
    }

    [Fact]
    public async Task Local_only_rejects_a_submitted_allowlist()
    {
        var service = Service(State());

        await Assert.ThrowsAsync<FederationPolicyValidationException>(() =>
            service.ReviewAsync(
                "demo-stack",
                new RuntimeStackFederationPolicyRequest("local_only", ["partner.example"]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Leaving_observed_local_only_truth_marks_ingress_change_required()
    {
        var service = Service(State(
            mode: FederationModes.LocalOnly,
            ingressMode: FederationIngressModes.LocalOnly));

        var review = await service.ReviewAsync(
            "demo-stack",
            new RuntimeStackFederationPolicyRequest("public", []),
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.True(review!.RestartRequired);
        Assert.True(review.IngressChangeRequired);
        Assert.False(review.NoChange);
    }

    [Fact]
    public async Task Unsupported_current_truth_is_not_reviewed()
    {
        var service = Service(State(
            mode: FederationModes.Unknown,
            configurationState: FederationConfigurationStates.CustomUnsupported));

        var exception = await Assert.ThrowsAsync<FederationReviewBlockedException>(() =>
            service.ReviewAsync(
                "demo-stack",
                new RuntimeStackFederationPolicyRequest("public", []),
                CancellationToken.None));

        Assert.Equal("federation_config_custom_unsupported", exception.Code);
    }

    private static RuntimeStackFederationReviewService Service(
        RuntimeStackFederationStateResponse state) =>
        new(
            new FakeStateService(state),
            new FederationPolicyRequestValidator(new FederationDomainValidator()));

    private static RuntimeStackFederationStateResponse State(
        string mode = FederationModes.Public,
        string configurationState = FederationConfigurationStates.Healthy,
        IReadOnlyList<string>? allowlist = null,
        string ingressMode = FederationIngressModes.Normal,
        string fingerprint = "sha256:current") =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
            Slug: "demo-stack",
            Mode: mode,
            ConfigurationState: configurationState,
            Allowlist: allowlist ?? [],
            EnforcementKind: "test",
            MatrixContainerRunning: true,
            MatrixDirectHostPortExposed: false,
            IngressMode: ingressMode,
            ServerWellKnownPublished: ingressMode == FederationIngressModes.Normal,
            FederationPathsPubliclyForwarded: ingressMode == FederationIngressModes.Normal,
            SigningKeyPathsPubliclyForwarded: ingressMode == FederationIngressModes.Normal,
            CanonicalRouteEnabled: true,
            CanonicalRouteTargetsMatrix: true,
            CanonicalCertificatePresent: true,
            AlternateMatrixRouteDetected: false,
            StateFingerprint: fingerprint,
            LatestOperation: null,
            Checks: [],
            Warnings: [],
            Problems: []);

    private sealed class FakeStateService(RuntimeStackFederationStateResponse? state)
        : IRuntimeStackFederationStateService
    {
        public Task<RuntimeStackFederationStateResponse?> GetAsync(
            string slugOrId,
            CancellationToken ct) => Task.FromResult(state);
    }
}
