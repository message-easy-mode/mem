using Microsoft.Extensions.Configuration;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.RuntimeImages;

public sealed class ApprovedOperationalRuntimeImageProviderTests
{
    private const string SynapseReference =
        "matrixdotorg/synapse@sha256:6882d26594b87171e0fe807ac6bd7f0000665cd70e73fb88c58ec9bff14c19ce";
    private const string ElementReference =
        "vectorim/element-web@sha256:2a65f32acc6fd7163523d1c4b5174de354b5ceb085898b15898f4d8ea01a8e3d";
    private const string SynapseImageId =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ElementImageId =
        "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";


    [Fact]
    public void Policy_projects_release_managed_repository_versions_without_touching_Docker()
    {
        var inspector = new FakeInspector();
        var provider = CreateProvider(inspector);

        var policy = provider.GetPolicy();

        Assert.Equal(1, policy.SchemaVersion);
        Assert.Equal("matrixdotorg/synapse", policy.Synapse.Repository);
        Assert.Equal("1.156.0", policy.Synapse.Version);
        Assert.Equal(SynapseReference, policy.Synapse.ApprovedReference);
        Assert.Equal("vectorim/element-web", policy.Element.Repository);
        Assert.Equal("1.12.23", policy.Element.Version);
        Assert.Equal(ElementReference, policy.Element.ApprovedReference);
        Assert.Empty(inspector.PulledReferences);
    }

    [Fact]
    public void Policy_fails_closed_when_release_version_metadata_is_missing()
    {
        var settings = DefaultSettings();
        settings["RuntimeImages:Element:ExpectedVersion"] = "";
        var provider = CreateProvider(new FakeInspector(), settings);

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetPolicy());

        Assert.Contains("ExpectedVersion is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparation_pulls_exact_missing_digest_then_resolves_local_image()
    {
        var inspector = new FakeInspector(
            null,
            new RuntimeImageInspection(SynapseImageId, [SynapseReference], []));
        var provider = CreateProvider(inspector);

        var synapse = await provider.PrepareSynapseAsync(CancellationToken.None);

        Assert.Equal("Synapse", synapse.Component);
        Assert.Equal(SynapseReference, synapse.ApprovedReference);
        Assert.Equal(SynapseImageId, synapse.ResolvedImageId);
        Assert.Equal(new[] { SynapseReference }, inspector.PulledReferences);
    }

    [Fact]
    public async Task Preparation_does_not_pull_when_exact_digest_is_already_local()
    {
        var inspector = new FakeInspector(
            new RuntimeImageInspection(ElementImageId, [ElementReference], []));
        var provider = CreateProvider(inspector);

        var element = await provider.PrepareElementAsync(CancellationToken.None);

        Assert.Equal(ElementReference, element.ApprovedReference);
        Assert.Equal(ElementImageId, element.ResolvedImageId);
        Assert.Empty(inspector.PulledReferences);
    }

    [Fact]
    public async Task Operation_resolves_prepared_images_without_pull()
    {
        var inspector = new FakeInspector(
            new RuntimeImageInspection(SynapseImageId, [SynapseReference], []),
            new RuntimeImageInspection(ElementImageId, [ElementReference], []));
        var provider = CreateProvider(inspector);

        var synapse = await provider.ResolveSynapseForOperationAsync(CancellationToken.None);
        var element = await provider.ResolveElementForOperationAsync(CancellationToken.None);

        Assert.Equal(SynapseImageId, synapse.ResolvedImageId);
        Assert.Equal(ElementImageId, element.ResolvedImageId);
        Assert.Empty(inspector.PulledReferences);
    }

    [Fact]
    public async Task Operation_fails_closed_when_exact_approved_image_is_missing()
    {
        var inspector = new FakeInspector(null);
        var provider = CreateProvider(inspector);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveElementForOperationAsync(CancellationToken.None));

        Assert.Contains("not available locally", exception.Message, StringComparison.Ordinal);
        Assert.Empty(inspector.PulledReferences);
    }

    [Fact]
    public async Task Preparation_fails_closed_when_pull_policy_is_disabled()
    {
        var settings = DefaultSettings();
        settings["RuntimeImages:Synapse:AllowPreparationPull"] = "false";
        var inspector = new FakeInspector(null);
        var provider = CreateProvider(inspector, settings);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.PrepareSynapseAsync(CancellationToken.None));

        Assert.Contains("Preparation pulls are disabled", exception.Message, StringComparison.Ordinal);
        Assert.Empty(inspector.PulledReferences);
    }

    [Theory]
    [InlineData("matrixdotorg/synapse:latest")]
    [InlineData("vectorim/element-web:latest")]
    [InlineData("docker.io/vectorim/element-web@sha256:bad")]
    [InlineData("sha256:2a65f32acc6fd7163523d1c4b5174de354b5ceb085898b15898f4d8ea01a8e3d")]
    public async Task Rejects_mutable_malformed_or_local_only_release_authority(string reference)
    {
        var settings = DefaultSettings();
        settings["RuntimeImages:Element:ApprovedReference"] = reference;
        var provider = CreateProvider(new FakeInspector(null), settings);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.PrepareElementAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_operational_pull_configuration_even_when_the_image_exists()
    {
        var settings = DefaultSettings();
        settings["RuntimeImages:Synapse:AllowOperationalPull"] = "true";
        var provider = CreateProvider(
            new FakeInspector(new RuntimeImageInspection(SynapseImageId, [SynapseReference], [])),
            settings);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveSynapseForOperationAsync(CancellationToken.None));

        Assert.Contains("must remain false", exception.Message, StringComparison.Ordinal);
    }

    private static ApprovedOperationalRuntimeImageProvider CreateProvider(
        IRuntimeImageInspector inspector,
        Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? DefaultSettings())
            .Build();

        return new ApprovedOperationalRuntimeImageProvider(
            configuration,
            inspector,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 18, 6, 0, 0, TimeSpan.Zero)));
    }

    private static Dictionary<string, string?> DefaultSettings() => new()
    {
        ["RuntimeImages:Synapse:ApprovedReference"] = SynapseReference,
        ["RuntimeImages:Synapse:ExpectedVersion"] = "1.156.0",
        ["RuntimeImages:Synapse:AllowPreparationPull"] = "true",
        ["RuntimeImages:Synapse:AllowOperationalPull"] = "false",
        ["RuntimeImages:Element:ApprovedReference"] = ElementReference,
        ["RuntimeImages:Element:ExpectedVersion"] = "1.12.23",
        ["RuntimeImages:Element:AllowPreparationPull"] = "true",
        ["RuntimeImages:Element:AllowOperationalPull"] = "false",
    };

    private sealed class FakeInspector(params RuntimeImageInspection?[] inspections)
        : IRuntimeImageInspector
    {
        private readonly Queue<RuntimeImageInspection?> _inspections = new(inspections ?? []);

        public List<string> PulledReferences { get; } = [];

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            var result = _inspections.Count == 0 ? null : _inspections.Dequeue();
            return Task.FromResult(result);
        }

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PulledReferences.Add(immutableReference);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
