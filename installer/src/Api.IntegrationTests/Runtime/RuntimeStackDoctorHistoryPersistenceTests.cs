using HostAgent.Endpoints;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Readiness;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackDoctorHistoryPersistenceTests
{
    [Theory]
    [InlineData("/internal/host-agent/runtime-stacks/{slugOrId}/doctor/history", "GET")]
    [InlineData("/internal/host-agent/runtime-stacks/{slugOrId}/doctor/reports/{reportId:guid}", "GET")]
    public async Task Doctor_history_routes_are_registered(string route, string method)
    {
        var builder = WebApplication.CreateBuilder();
        await using var application = builder.Build();

        new HostAgentRuntimeStacksEndpoint().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                route,
                StringComparison.Ordinal));

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        Assert.NotNull(methods);
        Assert.Equal(method, Assert.Single(methods!.HttpMethods));
    }

    [Fact]
    public async Task Previous_doctor_reports_are_paginated_newest_first_without_the_current_report()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-stack-doctor-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            await using var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var stackId = Guid.NewGuid();
            db.RuntimeStacks.Add(Stack(stackId, "doctor-history-stack"));
            await db.SaveChangesAsync();

            var manifest = Manifest(stackId, "doctor-history-stack");
            var store = new RuntimeReadinessReportStore(db);
            var doctorReports = new List<Guid>();

            for (var index = 1; index <= 13; index++)
            {
                var reportId = await store.SaveAsync(
                    manifest,
                    Verification($"doctor.check.{index}", index % 4 != 0),
                    reportKind: "doctor",
                    triggeredBy: "test",
                    operationId: Guid.NewGuid(),
                    CancellationToken.None);

                var entity = await db.RuntimeReadinessReports.SingleAsync(x => x.Id == reportId);
                entity.CreatedAtUtc = DateTime.Parse($"2026-08-25T01:{index:00}:00Z").ToUniversalTime();
                await db.SaveChangesAsync();
                doctorReports.Add(reportId);
            }

            var unrelatedReportId = await store.SaveAsync(
                manifest,
                Verification("production-verification", true),
                reportKind: "production-verification",
                triggeredBy: "test",
                operationId: Guid.NewGuid(),
                CancellationToken.None);
            var unrelated = await db.RuntimeReadinessReports.SingleAsync(x => x.Id == unrelatedReportId);
            unrelated.CreatedAtUtc = DateTime.Parse("2026-08-25T01:59:00Z").ToUniversalTime();
            await db.SaveChangesAsync();

            var firstPage = await store.ListPreviousAsync(
                stackId,
                "doctor",
                page: 1,
                pageSize: 5,
                CancellationToken.None);

            Assert.Equal(12, firstPage.TotalCount);
            Assert.Equal(1, firstPage.Page);
            Assert.Equal(5, firstPage.PageSize);
            Assert.Equal(3, firstPage.TotalPages);
            Assert.False(firstPage.HasPreviousPage);
            Assert.True(firstPage.HasNextPage);
            Assert.Equal(doctorReports[11], firstPage.Reports[0].ReportId);
            Assert.Equal(doctorReports[7], firstPage.Reports[4].ReportId);
            Assert.DoesNotContain(firstPage.Reports, x => x.ReportId == doctorReports[12]);
            Assert.DoesNotContain(firstPage.Reports, x => x.ReportId == unrelatedReportId);
            Assert.All(firstPage.Reports, x => Assert.Equal(1, x.CheckCount));

            var lastPage = await store.ListPreviousAsync(
                stackId,
                "doctor",
                page: 99,
                pageSize: 5,
                CancellationToken.None);

            Assert.Equal(3, lastPage.Page);
            Assert.Equal(2, lastPage.Reports.Count);
            Assert.True(lastPage.HasPreviousPage);
            Assert.False(lastPage.HasNextPage);
            Assert.Equal(doctorReports[1], lastPage.Reports[0].ReportId);
            Assert.Equal(doctorReports[0], lastPage.Reports[1].ReportId);
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
    public async Task Historical_report_lookup_is_scoped_to_stack_and_report_kind()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-stack-doctor-history-lookup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            await using var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var firstStackId = Guid.NewGuid();
            var secondStackId = Guid.NewGuid();
            db.RuntimeStacks.AddRange(
                Stack(firstStackId, "first-stack"),
                Stack(secondStackId, "second-stack"));
            await db.SaveChangesAsync();

            var store = new RuntimeReadinessReportStore(db);
            var doctorReportId = await store.SaveAsync(
                Manifest(firstStackId, "first-stack"),
                Verification("doctor.persisted", false),
                reportKind: "doctor",
                triggeredBy: "test",
                operationId: Guid.NewGuid(),
                CancellationToken.None);

            var report = await store.GetByIdAsync(
                firstStackId,
                "doctor",
                doctorReportId,
                CancellationToken.None);
            var wrongStack = await store.GetByIdAsync(
                secondStackId,
                "doctor",
                doctorReportId,
                CancellationToken.None);
            var wrongKind = await store.GetByIdAsync(
                firstStackId,
                "production-verification",
                doctorReportId,
                CancellationToken.None);

            Assert.NotNull(report);
            Assert.Equal(doctorReportId, report!.ReportId);
            Assert.False(report.AllPassed);
            Assert.Equal("failed", report.Status);
            Assert.Single(report.Checks);
            Assert.Null(wrongStack);
            Assert.Null(wrongKind);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static RuntimeStackEntity Stack(Guid id, string slug) =>
        new()
        {
            Id = id,
            Slug = slug,
            DisplayName = slug,
            Status = "ready",
            LastVerifiedStatus = "passed",
            LastVerifiedAtUtc = DateTime.Parse("2026-08-25T01:00:00Z").ToUniversalTime(),
            CreatedAtUtc = DateTime.Parse("2026-08-01T00:00:00Z").ToUniversalTime(),
            UpdatedAtUtc = DateTime.Parse("2026-08-25T01:00:00Z").ToUniversalTime(),
            MatrixInstanceId = Guid.NewGuid(),
            ElementInstanceId = Guid.NewGuid()
        };

    private static RuntimeStackManifest Manifest(Guid stackId, string slug) =>
        new(
            Source: "control-plane",
            StackId: stackId,
            Slug: slug,
            LastVerifiedStatus: "passed",
            LastVerifiedAtUtc: DateTimeOffset.Parse("2026-08-25T01:00:00Z"),
            Matrix: Service("matrix", $"matrix.{slug}.test"),
            Element: Service("element-web", $"chat.{slug}.test"),
            Warnings: [],
            Metadata: new Dictionary<string, string?>());

    private static RuntimeReadinessVerificationResult Verification(string code, bool success) =>
        new([
            new RuntimeReadinessCheckResult(
                code,
                $"Check {code}",
                "https://doctor.test",
                success,
                success ? 200 : 503,
                success ? "Passed." : "Failed.",
                null)
        ]);

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
