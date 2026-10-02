using System.Security.Claims;
using System.Text.Json;
using Modules.Operator.Dashboard;
using Carter;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Shared.Extensions;

namespace Api.IntegrationTests.Dashboard;

/// <summary>
/// Contract coverage for the browser-facing dashboard route. The tests map
/// only the Modules-owned Carter module under test, following the established
/// endpoint-contract test pattern. This avoids materialising unrelated Modules
/// routes whose service dependencies are intentionally outside this fixture.
/// The tests prove route metadata, response shape, cache controls, and
/// projection redaction without requiring Docker, NPM, or a browser runtime.
/// </summary>
public sealed class DashboardOverviewEndpointContractTests
{
    private const string PrivatePersistenceMarker =
        "dash-02-private-persistence-marker-must-never-serialize";

    [Fact]
    public async Task DASH_02_exposes_the_modules_owned_overview_carter_module_and_binds_it_to_read_safe_operator_policy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<DashboardOverviewService>();

        await using var application = builder.Build();

        var dashboardModule = new DashboardOverviewEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(dashboardModule);
        dashboardModule.AddRoutes(application);

        var endpoint = FindOverviewEndpoint(application);
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.Contains(
            authorization,
            data => string.Equals(
                data.Policy,
                MemOperatorPolicies.ReadSafeStatus,
                StringComparison.Ordinal));
        Assert.NotNull(methods);
        Assert.Equal(HttpMethods.Get, Assert.Single(methods!.HttpMethods));
    }

    [Fact]
    public async Task DASH_02_returns_a_no_store_browser_safe_overview_projection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Application.Services.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = CreatePlatformOwnerPrincipal()
        };

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/operator/dashboard/overview";
        context.Response.Body = new MemoryStream();

        var endpoint = FindOverviewEndpoint(fixture.Application);
        var requestDelegate = endpoint.RequestDelegate
            ?? throw new InvalidOperationException(
                "Dashboard overview route does not have a request delegate.");

        await requestDelegate(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        var overview = JsonSerializer.Deserialize<DashboardOverviewResponse>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(overview);
        Assert.Equal("control-plane", overview!.Source);
        Assert.True(overview.Capabilities.CanOperate);
        Assert.True(overview.Capabilities.CanManagePlatform);
        Assert.Equal(1, overview.Stacks.Total);
        Assert.Equal(3, overview.Platform.RequiredServiceCount);
        Assert.Equal(3, overview.Platform.RunningRequiredServiceCount);
        Assert.Contains(overview.Platform.Services, service => service.Key == "coturn");
        Assert.Equal("available", overview.Host.State);
        Assert.Equal("Ubuntu 24.04.2 LTS", overview.Host.OperatingSystem);
        Assert.Equal(8L, overview.Host.CpuCount);
        Assert.Equal(16L * 1024 * 1024 * 1024, overview.Host.MemoryTotalBytes);
        Assert.Null(overview.Host.Disk);
        Assert.Equal(
            "containerized_host_filesystem_unavailable",
            overview.Host.StorageUnavailableReasonCode);
        Assert.NotEmpty(overview.Activity.Items);
        Assert.DoesNotContain(PrivatePersistenceMarker, body, StringComparison.Ordinal);
        Assert.DoesNotContain("/srv/mem/private", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validation-id", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access-token", body, StringComparison.OrdinalIgnoreCase);
    }

    private static RouteEndpoint FindOverviewEndpoint(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    "/api/operator/dashboard/overview",
                    StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Get)));

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        authenticationType: "test"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private Fixture(WebApplication application, string databasePath)
        {
            Application = application;
            _databasePath = databasePath;
        }

        public WebApplication Application { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-dashboard-endpoint-{Guid.NewGuid():N}.db");

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddAuthorization();
            builder.Services.AddDbContext<MemDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddScoped<IDashboardRuntimeProbe, ReadyRuntimeProbe>();
            builder.Services.AddScoped<DashboardActivityReader>();
            builder.Services.AddScoped<DashboardOverviewService>();

            var application = builder.Build();
            new DashboardOverviewEndpoints().AddRoutes(application);

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.MigrateAsync();
                await SeedAsync(db);
            }

            return new Fixture(application, databasePath);
        }

        public async ValueTask DisposeAsync()
        {
            await Application.DisposeAsync();

            foreach (var path in new[]
                     {
                         _databasePath,
                         _databasePath + "-shm",
                         _databasePath + "-wal"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static async Task SeedAsync(MemDbContext db)
        {
            var now = DateTime.UtcNow;
            var stackId = Guid.NewGuid();

            db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = stackId,
                Slug = "family-chat",
                DisplayName = "Family chat",
                Status = "passed",
                LastVerifiedStatus = "passed",
                LastVerifiedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                MatrixInstanceId = Guid.NewGuid(),
                MatrixPublicBaseUrl = "https://matrix.family.example.test",
                ElementPublicBaseUrl = "https://chat.family.example.test",
                DataRoot = $"/srv/mem/private/{PrivatePersistenceMarker}",
                ManifestPath = $"/srv/mem/private/{PrivatePersistenceMarker}/manifest.json",
                LastError = PrivatePersistenceMarker,
                MetadataJson = $"{{\"access-token\":\"{PrivatePersistenceMarker}\"}}"
            });

            db.RuntimeOperations.Add(new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = stackId,
                Operation = "backup-stack",
                Status = "completed",
                RequestedAtUtc = now,
                StartedAtUtc = now,
                CompletedAtUtc = now,
                CurrentStep = PrivatePersistenceMarker,
                AttemptCount = 1,
                InputJson = $"{{\"access-token\":\"{PrivatePersistenceMarker}\"}}",
                ResultJson = $"{{\"private\":\"{PrivatePersistenceMarker}\"}}",
                EvidenceJson = $"{{\"path\":\"/srv/mem/private/{PrivatePersistenceMarker}\"}}",
                LastError = PrivatePersistenceMarker
            });

            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = "bkp_dashboard_endpoint_test",
                OriginKind = "local-captured",
                DisplayName = PrivatePersistenceMarker,
                PayloadState = "available",
                PayloadStorageKind = "local-backup-directory",
                PayloadDirectoryPath = $"/srv/mem/private/{PrivatePersistenceMarker}",
                SourceStackSlug = "family-chat",
                SourceBackupId = PrivatePersistenceMarker,
                ValidationId = $"validation-id-{PrivatePersistenceMarker}",
                IntegrityStatus = "valid",
                IntegritySummary = PrivatePersistenceMarker,
                WarningCount = 0,
                PayloadBytes = 4_096,
                CreatedAtUtc = now,
                CapturedAtUtc = now
            });

            await db.SaveChangesAsync();
        }
    }

    private sealed class ReadyRuntimeProbe : IDashboardRuntimeProbe
    {
        private static readonly DateTimeOffset ObservedAtUtc = new(
            2026,
            7,
            6,
            2,
            0,
            0,
            TimeSpan.Zero);

        public Task<DashboardDockerObservation> ObserveDockerAsync(CancellationToken ct) =>
            Task.FromResult(new DashboardDockerObservation("responsive", ObservedAtUtc));

        public Task<DashboardHostObservation> ObserveHostAsync(CancellationToken ct) =>
            Task.FromResult(new DashboardHostObservation(
                "available",
                ObservedAtUtc,
                null,
                "Ubuntu 24.04.2 LTS",
                "x86_64",
                8,
                16L * 1024 * 1024 * 1024,
                "29.4.2",
                7,
                12,
                null,
                "containerized_host_filesystem_unavailable"));

        public Task<IReadOnlyList<DashboardPlatformServiceObservation>> ObservePlatformServicesAsync(
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DashboardPlatformServiceObservation>>(
            [
                new DashboardPlatformServiceObservation("postgres", "required", "running", ObservedAtUtc),
                new DashboardPlatformServiceObservation("npm_ingress", "required", "running", ObservedAtUtc),
                new DashboardPlatformServiceObservation("coturn", "required", "running", ObservedAtUtc)
            ]);

        public Task<DashboardIngressObservation> ObserveIngressAsync(CancellationToken ct) =>
            Task.FromResult(new DashboardIngressObservation("ready", ObservedAtUtc));
    }
}
