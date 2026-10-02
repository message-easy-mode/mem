using System.Text.Json;
using Shared.ControlPlane.Runtime;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSupportReportSizeLimiterTests
{
    [Fact]
    public void Support_report_is_bounded_by_serialized_bytes_and_docker_log_characters()
    {
        var options = new DiagnosticsApiOptions
        {
            SupportReportMaximumBytes = 64 * 1024,
            SupportReportMaximumDockerLogCharacters = 2000
        };
        var limiter = new DiagnosticsSupportReportSizeLimiter(options);
        var report = Report(
            eventCount: 100,
            messageCharacters: 4000,
            dockerLogCharacters: 100000);

        var bounded = limiter.Apply(report);
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(
            bounded,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(serializedBytes.Length <= options.SupportReportMaximumBytes);
        Assert.True(bounded.Truncated);
        Assert.Contains(
            MemDiagnosticCodes.SupportReportSizeTruncated,
            bounded.Warnings);
        Assert.Contains(
            MemDiagnosticCodes.SupportReportDockerLogTruncated,
            bounded.Warnings);
        Assert.NotNull(bounded.DockerEvidence?.LogTail);
        Assert.True(
            bounded.DockerEvidence!.LogTail!.Content.Length <=
            options.SupportReportMaximumDockerLogCharacters);
        Assert.True(bounded.Events.Count < report.Events.Count);
    }

    [Fact]
    public void Support_report_options_reject_unbounded_request_response_and_docker_limits()
    {
        var request = new DiagnosticsApiOptions
        {
            SupportReportMaximumRequestBytes = 128
        };
        var response = new DiagnosticsApiOptions
        {
            SupportReportMaximumBytes = 32768
        };
        var docker = new DiagnosticsApiOptions
        {
            SupportReportMaximumDockerLogCharacters = 100001
        };

        Assert.Throws<InvalidOperationException>(() =>
            DiagnosticsApiOptions.Validate(request));
        Assert.Throws<InvalidOperationException>(() =>
            DiagnosticsApiOptions.Validate(response));
        Assert.Throws<InvalidOperationException>(() =>
            DiagnosticsApiOptions.Validate(docker));
    }

    private static DiagnosticsSupportReport Report(
        int eventCount,
        int messageCharacters,
        int dockerLogCharacters)
    {
        var incidentId = $"inc_{Guid.NewGuid():N}";
        var events = Enumerable.Range(0, eventCount)
            .Select(index => new DiagnosticsEventProjection(
                1,
                $"evt_{index:D32}",
                DateTimeOffset.UtcNow.AddSeconds(-index),
                MemDiagnosticSeverities.Error,
                "test.failure",
                "test",
                "diagnostics",
                "hardening",
                new string('m', messageCharacters),
                incidentId,
                "trace",
                "span",
                "request",
                "correlation",
                null,
                null,
                null,
                null,
                new Dictionary<string, string>
                {
                    ["detail"] = new string('d', messageCharacters)
                },
                null,
                "Review Diagnostics.",
                false,
                true,
                false))
            .ToArray();
        var capabilities = new DiagnosticsCapabilities(true, true, true, true);
        var storage = new DiagnosticsStorageCapacityHealth(
            "ready",
            10_000_000,
            20_000_000,
            null);
        var health = new DiagnosticsLoggingHealthResponse(
            DateTimeOffset.UtcNow,
            "ready",
            new DiagnosticsLocalRecorderHealth(
                true, "ready", true, true, null,
                DateTimeOffset.UtcNow, 1, 100, 0, null, storage),
            new DiagnosticsSafeEventStoreHealth(
                true, "ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                1, 0, 0, null, null, DateTimeOffset.UtcNow,
                0, 0, null, storage),
            new DiagnosticsSeqHealth(
                "optional-disabled", false, false, false, null),
            capabilities,
            false,
            Array.Empty<string>());
        var resource = new DiagnosticsResourceProjection(
            "migration",
            "migration-1",
            "Migration 1",
            null,
            null,
            "synapse",
            null);

        var runtimeContext = new MemControlPlaneRuntimeContextProjection(
            SchemaVersion: 1,
            ProductDisplayName: "MEM Control Plane",
            ApplicationName: "mem-control-plane",
            RuntimeMode: MemRuntimeModes.AutomatedTest,
            ControlPlaneInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ApiProcessInstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EnvironmentName: "Test",
            RunningInContainer: false,
            ContentRootKind: "test-host",
            StateRootKind: "disposable-test",
            StateRootProfile: MemStateRootProfiles.Default,
            UiDeliveryMode: MemUiDeliveryModes.TestHost,
            DockerEndpointKind: "local-unix-socket",
            ConfiguredContainerName: null,
            Version: "0.2.0-test",
            Commit: "test-commit",
            ValidationState: MemRuntimeValidationStates.Valid,
            MutationsAllowed: true,
            ShowDevelopmentBanner: true,
            Warnings: Array.Empty<string>());

        return new DiagnosticsSupportReport(
            1,
            DateTimeOffset.UtcNow,
            "0.2.0-test",
            runtimeContext,
            new DiagnosticsIncidentSummary(
                incidentId,
                MemDiagnosticSeverities.Error,
                "test.failure",
                "diagnostics",
                "hardening",
                "A bounded diagnostic incident.",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow,
                eventCount,
                false,
                false,
                resource,
                "/diagnostics"),
            events,
            Array.Empty<DiagnosticsOperationSummary>(),
            health,
            new DiagnosticsDockerEvidenceResponse(
                true,
                resource,
                DateTimeOffset.UtcNow,
                new DiagnosticsDockerEvidenceContainer(
                    "Migration private Synapse staging runtime",
                    "exited",
                    1,
                    "unhealthy",
                    null,
                    DateTimeOffset.UtcNow,
                    0,
                    "sha256:image"),
                new DiagnosticsDockerEvidenceLogTail(
                    500,
                    500,
                    dockerLogCharacters,
                    new string('l', dockerLogCharacters),
                    false,
                    true),
                null,
                Array.Empty<string>()),
            new DiagnosticsSupportReportRedaction(
                "mem-diagnostics-redaction-v1",
                true,
                Array.Empty<string>()),
            false,
            Array.Empty<string>());
    }
}
