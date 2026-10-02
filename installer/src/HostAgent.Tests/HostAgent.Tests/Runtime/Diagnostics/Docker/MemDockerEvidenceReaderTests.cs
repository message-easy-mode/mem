using System.Buffers.Binary;
using System.Text;
using HostAgent.Runtime.Diagnostics;
using HostAgent.Runtime.Diagnostics.Docker;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics.Docker;

public sealed class MemDockerEvidenceReaderTests
{
    [Fact]
    public async Task Reads_only_the_server_resolved_container_and_returns_safe_evidence()
    {
        var resource = new MemDiagnosticResource(
            "stack",
            Guid.NewGuid().ToString("D"),
            "Family chat",
            Service: "matrix");
        var resolver = new FakeResolver(new MemResolvedDockerResource(
            resource,
            "persisted-container-id",
            "Family chat Synapse",
            ["exact-secret-value"]));
        var source = new FakeSource
        {
            Container = new MemDockerSourceContainer(
                "persisted-container-id",
                "unexpected-runtime-name",
                "exited",
                1,
                "unhealthy",
                DateTimeOffset.Parse("2026-08-02T01:00:00Z"),
                DateTimeOffset.Parse("2026-08-02T01:01:00Z"),
                2,
                "sha256:image",
                Tty: false),
            LogBytes = Frame(1, "password=hunter2\nexact-secret-value\n")
        };
        var reader = CreateReader(resolver, source);
        var incidentId = "inc_11111111111111111111111111111111";

        var result = await reader.ReadForIncidentAsync(
            incidentId,
            [Event(incidentId, resource)],
            CancellationToken.None);

        Assert.True(result.Available);
        Assert.NotNull(result.Evidence);
        Assert.Equal("persisted-container-id", source.InspectedReference);
        Assert.Equal("persisted-container-id", source.LoggedReference);
        Assert.Equal("Family chat Synapse", result.Evidence!.Container.LogicalName);
        Assert.Equal("exited", result.Evidence.Container.ObservedState);
        Assert.Equal(1L, result.Evidence.Container.ExitCode);
        Assert.DoesNotContain("hunter2", result.Evidence.LogTail.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("exact-secret-value", result.Evidence.LogTail.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("persisted-container-id", result.Evidence.LogTail.Content, StringComparison.Ordinal);
        Assert.True(result.Evidence.LogTail.RedactionsApplied);
        Assert.Equal(100, result.Evidence.LogTail.RequestedLines);
        Assert.Contains(
            MemDiagnosticCodes.DockerEvidenceSensitiveOperationalMetadata,
            result.Evidence.Warnings);
    }

    [Fact]
    public async Task Returns_partial_unavailable_evidence_when_no_owned_resource_resolves()
    {
        var source = new FakeSource();
        var reader = CreateReader(new FakeResolver(null), source);
        var incidentId = "inc_22222222222222222222222222222222";

        var result = await reader.ReadForIncidentAsync(
            incidentId,
            [Event(incidentId, null)],
            CancellationToken.None);

        Assert.False(result.Available);
        Assert.Null(result.Evidence);
        Assert.Equal(MemDiagnosticCodes.DockerEvidenceResourceNotResolved, result.WarningCode);
        Assert.Null(source.InspectedReference);
    }

    [Fact]
    public async Task Evidence_failure_does_not_throw_a_second_incident_failure()
    {
        var resource = new MemDiagnosticResource("platform-service", "postgres", Service: "postgres");
        var resolver = new FakeResolver(new MemResolvedDockerResource(
            resource,
            "container-id",
            "MEM platform service postgres",
            Array.Empty<string>()));
        var source = new FakeSource
        {
            Container = new MemDockerSourceContainer(
                "container-id",
                "mem-postgres",
                "running",
                null,
                null,
                null,
                null,
                0,
                "postgres:16",
                Tty: false),
            ThrowCancellationFromLogs = true
        };
        var reader = CreateReader(resolver, source);
        var incidentId = "inc_33333333333333333333333333333333";

        var result = await reader.ReadForIncidentAsync(
            incidentId,
            [Event(incidentId, resource)],
            CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal(MemDiagnosticCodes.DockerEvidenceTimedOut, result.WarningCode);
    }

    private static MemDockerEvidenceReader CreateReader(
        IMemDiagnosticResourceResolver resolver,
        IMemDockerEvidenceSource source)
    {
        var safeEventOptions = new MemDiagnosticsOptions
        {
            MaximumDetailCharacters = 30000
        };
        var evidenceOptions = new MemDockerEvidenceOptions
        {
            DefaultTailLines = 100,
            MaximumTailLines = 500,
            MaximumCharacters = 30000,
            MaximumRawBytes = 1024 * 1024,
            MaximumFrameBytes = 1024 * 1024,
            TimeoutSeconds = 1
        };
        return new MemDockerEvidenceReader(
            evidenceOptions,
            resolver,
            source,
            new MemDockerLogDecoder(),
            new MemDockerLogSanitizer(new MemDiagnosticRedactor(safeEventOptions)),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 1, 2, 0, TimeSpan.Zero)),
            NullLogger<MemDockerEvidenceReader>.Instance);
    }

    private static MemDiagnosticEvent Event(
        string incidentId,
        MemDiagnosticResource? resource) =>
        new(
            1,
            $"evt_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            MemDiagnosticSeverities.Error,
            "api.docker.operation_failed",
            "control-plane-api",
            "stacks",
            "request",
            "Docker operation failed.",
            incidentId,
            "trace",
            "span",
            "request",
            "correlation",
            null,
            resource,
            null,
            null,
            null,
            null,
            "Review Docker evidence.",
            true,
            true,
            false);

    private static byte[] Frame(byte streamType, string content)
    {
        var payload = Encoding.UTF8.GetBytes(content);
        var result = new byte[8 + payload.Length];
        result[0] = streamType;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), (uint)payload.Length);
        payload.AsSpan().CopyTo(result.AsSpan(8));
        return result;
    }

    private sealed class FakeResolver(MemResolvedDockerResource? result)
        : IMemDiagnosticResourceResolver
    {
        public Task<MemResolvedDockerResource?> ResolveAsync(
            string incidentId,
            IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public Task<MemResolvedDockerResource?> ResolveResourceAsync(
            MemDiagnosticResource resource,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FakeSource : IMemDockerEvidenceSource
    {
        public MemDockerSourceContainer? Container { get; init; }

        public byte[] LogBytes { get; init; } = [];

        public bool ThrowCancellationFromLogs { get; init; }

        public string? InspectedReference { get; private set; }

        public string? LoggedReference { get; private set; }

        public Task<MemDockerSourceContainer?> InspectAsync(
            string containerReference,
            CancellationToken cancellationToken)
        {
            InspectedReference = containerReference;
            return Task.FromResult(Container);
        }

        public Task<Stream> OpenLogsAsync(
            string containerReference,
            int tailLines,
            CancellationToken cancellationToken)
        {
            LoggedReference = containerReference;
            if (ThrowCancellationFromLogs)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return Task.FromResult<Stream>(new MemoryStream(LogBytes));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
