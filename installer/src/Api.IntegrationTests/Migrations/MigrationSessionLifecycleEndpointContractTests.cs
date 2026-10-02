using System.Reflection;
using System.Security.Claims;
using System.Text;
using Carter;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionLifecycleEndpointContractTests
{
    [Fact]
    public async Task Lifecycle_routes_are_migration_keyed_and_policy_protected()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<MigrationSessionProjectionService>();
        builder.Services.AddScoped<MigrationSessionInventoryService>();
        builder.Services.AddScoped<MigrationSessionLifecycleService>();
        builder.Services.AddScoped<MigrationSourceRequestService>();
        builder.Services.AddScoped<MigrationFinalPackageRecipientService>();
        builder.Services.AddScoped<MigrationFinalPackageUploadService>();

        await using var application = builder.Build();
        var module = new MigrationSessionEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = GetLifecycleEndpoints(application);
        Assert.Equal(5, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization =>
                    authorization.Policy ==
                    MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                    .Select(method =>
                        $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/lifecycle",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/lifecycle/archive",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/lifecycle/unarchive",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/lifecycle/cancel",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/lifecycle/delete",
            routes);
    }

    [Fact]
    public void Cancel_request_contains_operator_decisions_not_actor_or_server_paths()
    {
        var properties = typeof(MigrationSessionCancelRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(MigrationSessionCancelRequest.ExpectedStateVersion),
            nameof(MigrationSessionCancelRequest.AcknowledgeSourceUnaffected),
            nameof(MigrationSessionCancelRequest.EncryptedPackageRetention),
        };

        Assert.True(expected.SetEquals(properties));
        Assert.DoesNotContain("Actor", properties);
        Assert.DoesNotContain("CancelledBy", properties);
        Assert.DoesNotContain("HostPath", properties);
    }

    [Fact]
    public void Delete_request_contains_only_confirmation_and_concurrency_decisions()
    {
        var properties = typeof(MigrationSessionDeleteRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(MigrationSessionDeleteRequest.ExpectedStateVersion),
            nameof(MigrationSessionDeleteRequest.ConfirmationMigrationId),
            nameof(MigrationSessionDeleteRequest.AcknowledgeSourceUnaffected),
        };

        Assert.True(expected.SetEquals(properties));
        Assert.DoesNotContain("Actor", properties);
        Assert.DoesNotContain("ActorOperatorId", properties);
        Assert.DoesNotContain("HostPath", properties);
    }

    [Fact]
    public async Task Cancel_route_requires_recent_step_up_before_mutating()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-endpoint-denied-{Guid.NewGuid():N}.db");
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-endpoint-files-{Guid.NewGuid():N}");

        try
        {
            await using var application = await BuildApplicationAsync(
                databasePath,
                dataRoot,
                new DenyRecentStepUpAuthorizationService());
            var endpoint = GetCancelEndpoint(application);

            await using var scope = application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            await SeedAwaitingSessionAsync(db, "mig_step_up_required");

            var context = CreateCancelContext(
                scope.ServiceProvider,
                "mig_step_up_required",
                expectedStateVersion: 1,
                actor: "named-owner");
            await endpoint.RequestDelegate!(context);

            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());

            db.ChangeTracker.Clear();
            var persisted = await db.MigrationIntakes
                .AsNoTracking()
                .Include(x => x.PackageRevisions)
                .SingleAsync();
            Assert.Equal("awaiting-package", Assert.Single(persisted.PackageRevisions).Status);
            Assert.Null(persisted.CancelledBy);
        }
        finally
        {
            TryDelete(databasePath);
            TryDeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task Cancel_route_records_actor_from_authenticated_identity()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-endpoint-actor-{Guid.NewGuid():N}.db");
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-endpoint-files-{Guid.NewGuid():N}");

        try
        {
            await using var application = await BuildApplicationAsync(
                databasePath,
                dataRoot,
                new AllowAllAuthorizationService());
            var endpoint = GetCancelEndpoint(application);

            await using var scope = application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            await SeedAwaitingSessionAsync(db, "mig_identity_actor");

            var context = CreateCancelContext(
                scope.ServiceProvider,
                "mig_identity_actor",
                expectedStateVersion: 1,
                actor: "named-owner");
            await endpoint.RequestDelegate!(context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());

            db.ChangeTracker.Clear();
            var persisted = await db.MigrationIntakes
                .AsNoTracking()
                .Include(x => x.PackageRevisions)
                .SingleAsync();
            Assert.Equal("named-owner", persisted.CancelledBy);
            Assert.Equal(MigrationSessionLifecycleStatuses.Cancelled, persisted.LifecycleStatus);
            Assert.Null(Assert.Single(persisted.PackageRevisions).ProtectedAgeIdentity);
        }
        finally
        {
            TryDelete(databasePath);
            TryDeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task Delete_route_requires_recent_step_up_and_records_authenticated_actor_id()
    {
        var deniedDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-delete-denied-{Guid.NewGuid():N}.db");
        var allowedDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-delete-allowed-{Guid.NewGuid():N}.db");
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-lifecycle-delete-files-{Guid.NewGuid():N}");

        try
        {
            await using (var deniedApplication = await BuildApplicationAsync(
                deniedDatabasePath,
                dataRoot,
                new DenyRecentStepUpAuthorizationService()))
            {
                var endpoint = GetDeleteEndpoint(deniedApplication);
                await using var scope = deniedApplication.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await SeedCancelledSessionAsync(db, "mig_delete_step_up");

                var context = CreateDeleteContext(
                    scope.ServiceProvider,
                    "mig_delete_step_up",
                    expectedStateVersion: 1,
                    Guid.NewGuid());
                await endpoint.RequestDelegate!(context);

                Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
                Assert.True(await db.MigrationIntakes.AnyAsync());
                Assert.False(await db.MemOperatorAuditEvents.AnyAsync());
            }

            await using (var allowedApplication = await BuildApplicationAsync(
                allowedDatabasePath,
                dataRoot,
                new AllowAllAuthorizationService()))
            {
                var endpoint = GetDeleteEndpoint(allowedApplication);
                await using var scope = allowedApplication.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await SeedCancelledSessionAsync(db, "mig_delete_actor");
                var actorOperatorId = Guid.NewGuid();

                var context = CreateDeleteContext(
                    scope.ServiceProvider,
                    "mig_delete_actor",
                    expectedStateVersion: 1,
                    actorOperatorId);
                await endpoint.RequestDelegate!(context);

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                Assert.False(await db.MigrationIntakes.AnyAsync());
                var audit = await db.MemOperatorAuditEvents.AsNoTracking().SingleAsync();
                Assert.Equal(actorOperatorId, audit.ActorOperatorId);
                Assert.Equal("mig_delete_actor", audit.CorrelationId);
            }
        }
        finally
        {
            TryDelete(deniedDatabasePath);
            TryDelete(allowedDatabasePath);
            TryDeleteDirectory(dataRoot);
        }
    }

    private static async Task<WebApplication> BuildApplicationAsync(
        string databasePath,
        string dataRoot,
        IAuthorizationService authorizationService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["HostAgent:DataRoot"] = dataRoot,
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(authorizationService);
        builder.Services.AddDbContext<MemDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddScoped<IMemOperatorAuditService, MemOperatorAuditService>();
        builder.Services.AddScoped<MigrationSessionProjectionService>();
        builder.Services.AddScoped<MigrationSessionInventoryService>();
        builder.Services.AddScoped<MigrationSessionLifecycleService>();
        builder.Services.AddScoped<MigrationSourceRequestService>();
        builder.Services.AddScoped<MigrationFinalPackageRecipientService>();
        builder.Services.AddScoped<MigrationFinalPackageUploadService>();

        var application = builder.Build();
        new MigrationSessionEndpoints().AddRoutes(application);

        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        await db.Database.EnsureCreatedAsync();

        return application;
    }

    private static async Task SeedAwaitingSessionAsync(
        MemDbContext db,
        string migrationId)
    {
        var intakeId = Guid.NewGuid();
        var intake = new MigrationIntakeEntity
        {
            Id = intakeId,
            IntakeId = migrationId,
            DisplayName = "Lifecycle endpoint test",
            LifecycleStatus = MigrationSessionLifecycleStatuses.Active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            StateVersion = 1,
        };
        intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr_{Guid.NewGuid():N}",
            MigrationIntakeEntityId = intakeId,
            MigrationIntake = intake,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = $"{migrationId}:preview",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            ProtectedAgeIdentity = "revision-secret",
        });

        db.MigrationIntakes.Add(intake);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task SeedCancelledSessionAsync(
        MemDbContext db,
        string migrationId)
    {
        db.MigrationIntakes.Add(new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = migrationId,
            DisplayName = "Disposable lifecycle endpoint test",
            LifecycleStatus = MigrationSessionLifecycleStatuses.Cancelled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ClosedAtUtc = DateTime.UtcNow,
            ClosureKind = "operator-cancelled",
            StateVersion = 1,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static DefaultHttpContext CreateDeleteContext(
        IServiceProvider services,
        string migrationId,
        long expectedStateVersion,
        Guid actorOperatorId)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, actorOperatorId.ToString("D")),
                new Claim(ClaimTypes.Name, "named-owner"),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner),
            ],
            "test")),
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/api/operator/migrations/sessions/{migrationId}/lifecycle/delete";
        context.Request.RouteValues["migrationId"] = migrationId;
        context.Request.ContentType = "application/json";
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        var json =
            $$"""
            {
              "expectedStateVersion": {{expectedStateVersion}},
              "confirmationMigrationId": "{{migrationId}}",
              "acknowledgeSourceUnaffected": true
            }
            """;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static DefaultHttpContext CreateCancelContext(
        IServiceProvider services,
        string migrationId,
        long expectedStateVersion,
        string actor)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                new Claim(ClaimTypes.Name, actor),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner),
            ],
            "test")),
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path =
            $"/api/operator/migrations/sessions/{migrationId}/lifecycle/cancel";
        context.Request.RouteValues["migrationId"] = migrationId;
        context.Request.ContentType = "application/json";
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        var json =
            $$"""
            {
              "expectedStateVersion": {{expectedStateVersion}},
              "acknowledgeSourceUnaffected": true,
              "encryptedPackageRetention": "remove"
            }
            """;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RouteEndpoint[] GetLifecycleEndpoints(
        WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                Normalize(endpoint.RoutePattern.RawText).StartsWith(
                    "/api/operator/migrations/sessions/{migrationId}/lifecycle",
                    StringComparison.Ordinal))
            .ToArray();

    private static RouteEndpoint GetCancelEndpoint(
        WebApplication application) =>
        GetLifecycleEndpoints(application).Single(endpoint =>
            Normalize(endpoint.RoutePattern.RawText).EndsWith(
                "/cancel",
                StringComparison.Ordinal));

    private static RouteEndpoint GetDeleteEndpoint(
        WebApplication application) =>
        GetLifecycleEndpoints(application).Single(endpoint =>
            Normalize(endpoint.RoutePattern.RawText).EndsWith(
                "/delete",
                StringComparison.Ordinal));

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class AllowAllAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class DenyRecentStepUpAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(
                policyName == MemOperatorPolicies.RecentStepUp
                    ? AuthorizationResult.Failed()
                    : AuthorizationResult.Success());
    }
}
