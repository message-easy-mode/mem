using HostAgent.Runtime.Coturn;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnCheckEvidenceStoreTests
{
    [Fact]
    public async Task Latest_check_round_trips_without_secret_material()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-check-evidence-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnCheckEvidenceStore(root);
            var checkedAt = new DateTimeOffset(
                2026,
                8,
                22,
                8,
                0,
                0,
                TimeSpan.Zero);
            var result = SampleCheck(checkedAt) with
            {
                IncidentId = "inc_example"
            };

            var write = await store.WriteLatestAsync(
                result,
                CancellationToken.None);
            var read = await store.ReadLatestAsync(
                CancellationToken.None);

            Assert.True(write.Stored);
            Assert.Null(write.WarningCode);
            Assert.Null(read.WarningCode);

            var roundTripped = Assert.IsType<CoturnCheckResponse>(read.Result);
            Assert.Equal(result.Source, roundTripped.Source);
            Assert.Equal(result.Status, roundTripped.Status);
            Assert.Equal(result.CheckedAtUtc, roundTripped.CheckedAtUtc);
            Assert.Equal(result.FreshUntilUtc, roundTripped.FreshUntilUtc);
            Assert.Equal(result.ContainerState, roundTripped.ContainerState);
            Assert.Equal(result.Readiness, roundTripped.Readiness);
            Assert.Equal(result.PublicHost, roundTripped.PublicHost);
            Assert.Equal(result.RuntimeContainerId, roundTripped.RuntimeContainerId);
            Assert.Equal(result.RuntimeStartedAtUtc, roundTripped.RuntimeStartedAtUtc);
            Assert.Equal(result.RuntimeRestartCount, roundTripped.RuntimeRestartCount);
            Assert.Equal(result.Checks.ToArray(), roundTripped.Checks.ToArray());
            Assert.Equal(result.Allocation, roundTripped.Allocation);
            Assert.Equal(result.Warnings.ToArray(), roundTripped.Warnings.ToArray());
            Assert.Equal(result.Detail, roundTripped.Detail);
            Assert.Equal(result.EvidencePersisted, roundTripped.EvidencePersisted);
            Assert.Equal(result.IncidentId, roundTripped.IncidentId);

            var json = await File.ReadAllTextAsync(store.EvidencePath);
            Assert.DoesNotContain(
                "shared-secret",
                json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "static-auth-secret",
                json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "password",
                json,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Missing_latest_check_is_not_an_error()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-check-evidence-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnCheckEvidenceStore(root);

            var read = await store.ReadLatestAsync(
                CancellationToken.None);

            Assert.Null(read.Result);
            Assert.Null(read.WarningCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Oversized_latest_check_is_not_persisted()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-check-evidence-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnCheckEvidenceStore(root);
            var checkedAt = new DateTimeOffset(
                2026,
                8,
                22,
                8,
                0,
                0,
                TimeSpan.Zero);
            var result = SampleCheck(checkedAt) with
            {
                Detail = new string('x', 70_000)
            };

            var write = await store.WriteLatestAsync(
                result,
                CancellationToken.None);

            Assert.False(write.Stored);
            Assert.Equal(
                CoturnCheckEvidenceWarningCodes.WriteFailed,
                write.WarningCode);
            Assert.False(File.Exists(store.EvidencePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Incomplete_latest_check_document_is_rejected_as_invalid_evidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-check-evidence-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnCheckEvidenceStore(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                store.EvidencePath,
                """
                {
                  "schemaVersion": 1,
                  "result": {
                    "source": "control-plane",
                    "status": "passed",
                    "checkedAtUtc": "2026-08-22T08:00:00Z",
                    "freshUntilUtc": "2026-08-22T08:30:00Z",
                    "containerState": "running",
                    "readiness": "ready",
                    "publicHost": "turn.example.test"
                  }
                }
                """);

            var read = await store.ReadLatestAsync(
                CancellationToken.None);

            Assert.Null(read.Result);
            Assert.Equal(
                CoturnCheckEvidenceWarningCodes.InvalidDocument,
                read.WarningCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Malformed_latest_check_is_reported_as_unavailable_evidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-coturn-check-evidence-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CoturnCheckEvidenceStore(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                store.EvidencePath,
                "{ not valid json");

            var read = await store.ReadLatestAsync(
                CancellationToken.None);

            Assert.Null(read.Result);
            Assert.Equal(
                CoturnCheckEvidenceWarningCodes.ReadFailed,
                read.WarningCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CoturnCheckResponse SampleCheck(DateTimeOffset checkedAt) =>
        new(
            Source: "control-plane",
            Status: CoturnCheckStatuses.Passed,
            CheckedAtUtc: checkedAt,
            FreshUntilUtc: checkedAt.AddSeconds(
                CoturnDiagnosticsPolicy.FunctionalCheckFreshForSeconds),
            ContainerState: "running",
            Readiness: "ready",
            PublicHost: "turn.example.test",
            RuntimeContainerId: "coturn-container-1",
            RuntimeStartedAtUtc: checkedAt.AddMinutes(-10),
            RuntimeRestartCount: 0,
            Checks:
            [
                new CoturnCheckItem(
                    "container",
                    CoturnCheckStatuses.Passed,
                    "The MEM-owned Coturn container is running."),
                new CoturnCheckItem(
                    "host-dns",
                    CoturnCheckStatuses.Passed,
                    "The host resolver returned a non-loopback address.",
                    "Host resolver addresses: 10.10.0.193.")
            ],
            Allocation: new CoturnAllocationProbeResponse(
                CoturnCheckStatuses.Passed,
                "udp",
                "Coturn accepted temporary credentials and completed a local UDP allocation probe.",
                "allocation completed"),
            Warnings: [],
            Detail: "passed",
            EvidencePersisted: true,
            IncidentId: null);
}
