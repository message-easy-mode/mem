using System.Text.Json;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsDockerEvidenceTests
{
    [Fact]
    public void Projection_contains_safe_log_evidence_without_a_container_target_contract()
    {
        var result = new MemDockerEvidenceReadResult(
            true,
            new MemDockerEvidence(
                new MemDiagnosticResource(
                    "migration",
                    "mig-1",
                    "Migration 1",
                    Service: "synapse",
                    WorkspacePath: "/srv/private/path"),
                DateTimeOffset.Parse("2026-08-02T01:02:00Z"),
                new MemDockerEvidenceContainer(
                    "Migration private Synapse staging runtime",
                    "exited",
                    1,
                    "unhealthy",
                    DateTimeOffset.Parse("2026-08-02T01:00:00Z"),
                    DateTimeOffset.Parse("2026-08-02T01:01:00Z"),
                    0,
                    "sha256:image"),
                new MemDockerEvidenceLogTail(
                    100,
                    2,
                    30000,
                    "safe line one\nsafe line two",
                    false,
                    true),
                Array.Empty<string>()),
            null,
            Array.Empty<string>());

        var projection = DiagnosticsDockerEvidenceService.Project(result);
        var json = JsonSerializer.Serialize(projection);

        Assert.True(projection.Available);
        Assert.Equal("exited", projection.Container!.ObservedState);
        Assert.Contains("safe line two", projection.LogTail!.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("containerId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("containerName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("containerReference", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspacePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/srv/private/path", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Unavailable_projection_remains_a_successful_partial_diagnostic_contract()
    {
        var projection = DiagnosticsDockerEvidenceService.Project(
            new MemDockerEvidenceReadResult(
                false,
                null,
                MemDiagnosticCodes.DockerEvidenceResourceNotResolved,
                [MemDiagnosticCodes.DockerEvidenceResourceNotResolved]));

        Assert.False(projection.Available);
        Assert.Null(projection.Container);
        Assert.Null(projection.LogTail);
        Assert.Equal(
            MemDiagnosticCodes.DockerEvidenceResourceNotResolved,
            projection.WarningCode);
    }
}
