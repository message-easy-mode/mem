using HostAgent.Runtime.Diagnostics.Docker;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics.Docker;

public sealed class MemDockerResourceLocatorTests
{
    [Fact]
    public async Task Resolves_the_logical_resource_then_returns_the_current_full_container_identity()
    {
        var resource = new MemDiagnosticResource(
            "stack",
            "family-chat",
            StackSlug: "family-chat",
            Service: "matrix");
        var resolved = new MemResolvedDockerResource(
            resource,
            "persisted-or-named-reference",
            "Family chat Synapse",
            []);
        var source = new FakeSource(new MemDockerSourceContainer(
            new string('a', 64),
            "mem-matrix-family-chat",
            "running",
            null,
            "healthy",
            null,
            null,
            0,
            "sha256:image",
            Tty: false));
        var locator = CreateLocator(
            new FakeResolver(resolved),
            source,
            enabled: true);

        var location = await locator.LocateAsync(
            resource,
            CancellationToken.None);

        Assert.NotNull(location);
        Assert.Equal(new string('a', 64), location!.ContainerId);
        Assert.Equal("persisted-or-named-reference", source.InspectedReference);
        Assert.Equal("Family chat Synapse", location.LogicalName);
    }

    [Fact]
    public async Task Disabled_Docker_evidence_does_not_resolve_or_inspect_a_resource()
    {
        var resolver = new FakeResolver(null);
        var source = new FakeSource(null);
        var locator = CreateLocator(resolver, source, enabled: false);

        var location = await locator.LocateAsync(
            new MemDiagnosticResource("platform-service", "seq", Service: "seq"),
            CancellationToken.None);

        Assert.Null(location);
        Assert.Equal(0, resolver.ResolveCalls);
        Assert.Null(source.InspectedReference);
    }

    private static MemDockerResourceLocator CreateLocator(
        IMemDiagnosticResourceResolver resolver,
        IMemDockerEvidenceSource source,
        bool enabled) =>
        new(
            new MemDockerEvidenceOptions
            {
                Enabled = enabled,
                TimeoutSeconds = 1
            },
            resolver,
            source,
            NullLogger<MemDockerResourceLocator>.Instance);

    private sealed class FakeResolver(MemResolvedDockerResource? result)
        : IMemDiagnosticResourceResolver
    {
        public int ResolveCalls { get; private set; }

        public Task<MemResolvedDockerResource?> ResolveAsync(
            string incidentId,
            IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
            CancellationToken cancellationToken)
        {
            ResolveCalls += 1;
            return Task.FromResult(result);
        }

        public Task<MemResolvedDockerResource?> ResolveResourceAsync(
            MemDiagnosticResource resource,
            CancellationToken cancellationToken)
        {
            ResolveCalls += 1;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeSource(MemDockerSourceContainer? container)
        : IMemDockerEvidenceSource
    {
        public string? InspectedReference { get; private set; }

        public Task<MemDockerSourceContainer?> InspectAsync(
            string containerReference,
            CancellationToken cancellationToken)
        {
            InspectedReference = containerReference;
            return Task.FromResult(container);
        }

        public Task<Stream> OpenLogsAsync(
            string containerReference,
            int tailLines,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The locator must not read logs.");
    }
}
