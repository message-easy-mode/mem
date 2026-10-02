using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Readiness;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackDoctorReportPersistenceTests
{
    [Fact]
    public async Task Latest_doctor_report_is_recoverable_from_durable_readiness_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-stack-doctor-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            await using var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var stackId = Guid.NewGuid();
            db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = stackId,
                Slug = "doctor-stack",
                DisplayName = "Doctor Stack",
                Status = "ready",
                LastVerifiedStatus = "passed",
                LastVerifiedAtUtc = DateTime.Parse("2026-08-25T01:00:00Z").ToUniversalTime(),
                CreatedAtUtc = DateTime.Parse("2026-08-01T00:00:00Z").ToUniversalTime(),
                UpdatedAtUtc = DateTime.Parse("2026-08-25T01:00:00Z").ToUniversalTime(),
                MatrixInstanceId = Guid.NewGuid(),
                ElementInstanceId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();

            var manifest = new RuntimeStackManifest(
                Source: "control-plane",
                StackId: stackId,
                Slug: "doctor-stack",
                LastVerifiedStatus: "passed",
                LastVerifiedAtUtc: DateTimeOffset.Parse("2026-08-25T01:00:00Z"),
                Matrix: Service("matrix", "matrix.doctor.test"),
                Element: Service("element-web", "chat.doctor.test"),
                Warnings: [],
                Metadata: new Dictionary<string, string?>());

            var store = new RuntimeReadinessReportStore(db);
            var firstOperationId = Guid.NewGuid();
            var firstReportId = await store.SaveAsync(
                manifest,
                new RuntimeReadinessVerificationResult([
                    new RuntimeReadinessCheckResult(
                        "doctor.first",
                        "First check",
                        "https://matrix.doctor.test",
                        false,
                        503,
                        "First report failed.",
                        null)
                ]),
                reportKind: "doctor",
                triggeredBy: "test",
                operationId: firstOperationId,
                CancellationToken.None);

            var firstEntity = await db.RuntimeReadinessReports.SingleAsync(x => x.Id == firstReportId);
            firstEntity.CreatedAtUtc = DateTime.Parse("2026-08-25T01:01:00Z").ToUniversalTime();
            await db.SaveChangesAsync();

            var secondOperationId = Guid.NewGuid();
            var secondReportId = await store.SaveAsync(
                manifest,
                new RuntimeReadinessVerificationResult([
                    new RuntimeReadinessCheckResult(
                        "doctor.second",
                        "Second check",
                        "https://chat.doctor.test",
                        true,
                        200,
                        "Second report passed.",
                        "ok")
                ]),
                reportKind: "doctor",
                triggeredBy: "test",
                operationId: secondOperationId,
                CancellationToken.None);

            var secondEntity = await db.RuntimeReadinessReports.SingleAsync(x => x.Id == secondReportId);
            secondEntity.CreatedAtUtc = DateTime.Parse("2026-08-25T01:02:00Z").ToUniversalTime();
            await db.SaveChangesAsync();

            var unrelatedReportId = await store.SaveAsync(
                manifest,
                new RuntimeReadinessVerificationResult([
                    new RuntimeReadinessCheckResult(
                        "verification.newer",
                        "Newer non-Doctor check",
                        "https://matrix.doctor.test",
                        true,
                        200,
                        "Not a Doctor report.",
                        null)
                ]),
                reportKind: "production-verification",
                triggeredBy: "test",
                operationId: Guid.NewGuid(),
                CancellationToken.None);

            var unrelatedEntity = await db.RuntimeReadinessReports.SingleAsync(x => x.Id == unrelatedReportId);
            unrelatedEntity.CreatedAtUtc = DateTime.Parse("2026-08-25T01:03:00Z").ToUniversalTime();
            await db.SaveChangesAsync();

            var latest = await store.GetLatestAsync(stackId, "doctor", CancellationToken.None);

            Assert.NotNull(latest);
            Assert.Equal(secondReportId, latest!.ReportId);
            Assert.Equal(secondOperationId, latest.OperationId);
            Assert.True(latest.AllPassed);
            Assert.Equal("passed", latest.Status);
            Assert.Equal(DateTimeOffset.Parse("2026-08-25T01:02:00Z"), latest.CreatedAtUtc);
            var check = Assert.Single(latest.Checks);
            Assert.Equal("doctor.second", check.Code);
            Assert.Equal(200, check.StatusCode);
            Assert.Equal("ok", check.BodyPreview);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static RuntimeStackServiceManifest Service(string serviceKey, string host) =>
        new(
            InstanceId: Guid.NewGuid(),
            ServiceKey: serviceKey,
            ContainerId: null,
            ContainerName: null,
            HostPort: 0,
            DataPath: null,
            ServerName: host,
            PublicHost: host,
            PublicBaseUrl: $"https://{host}",
            InternalHost: host,
            InternalBaseUrl: $"http://{host}",
            PublicRouteId: null,
            InternalRouteId: null,
            NpmCertificateId: null,
            RuntimeMetadata: new Dictionary<string, string?>());
}
