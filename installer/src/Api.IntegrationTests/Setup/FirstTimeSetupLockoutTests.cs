using Api.Diagnostics;
using Api.Setup;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Identity;
using Modules.Setup.Lifecycle;
using Modules.Setup.InstallRuns;
using Shared.Exceptions;

namespace Api.IntegrationTests.Setup;

public sealed class FirstTimeSetupLockoutTests
{
    [Fact]
    public async Task STARTUP_01C_completed_installation_blocks_first_time_setup_mutation_with_stable_conflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Installations.Add(Installation("Succeeded"));
        await fixture.Db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = SetupRequest(HttpMethods.Post, "/api/setup/install-plans/");

        var problem = await Assert.ThrowsAsync<MemProblemException>(() =>
            middleware.InvokeAsync(context, fixture.LockService));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(FirstTimeSetupMutationGuardMiddleware.ProblemCode, problem.Code);
        Assert.Equal("setup", problem.Feature);
        Assert.Equal("first-time-lockout", problem.Stage);

        var classification = new MemExceptionClassifier().Classify(context, problem);
        var details = new MemProblemDetailsFactory().Create(
            context,
            classification,
            incidentId: null,
            diagnosticWarningCode: null);
        Assert.Equal(StatusCodes.Status409Conflict, details.Status);
        Assert.Equal(
            FirstTimeSetupMutationGuardMiddleware.ProblemCode,
            details.Extensions["code"]);
    }

    [Fact]
    public async Task STARTUP_01C_established_operator_state_without_installation_history_is_also_locked()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();
        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = "seq",
            ContainerName = "mem-seq-dev",
            Image = "datalust/seq:latest",
            ContainerPort = 80,
            PreferredHostPort = 17341,
            SelectedHostPort = 17341,
            Status = "Running",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            LastObservedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var state = await fixture.LockService.GetStateAsync(CancellationToken.None);

        Assert.True(state.Locked);
        Assert.Equal("established-operator-state", state.ReasonCode);
        Assert.Contains("runtime-service", state.Evidence ?? []);
    }

    [Fact]
    public async Task STARTUP_01C_completed_owner_alone_does_not_lock_first_time_setup()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();

        var state = await fixture.LockService.GetStateAsync(CancellationToken.None);

        Assert.False(state.Locked);
        Assert.Equal("available", state.ReasonCode);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_active_draft_owns_preinstall_domain_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();

        var draft = Installation(InstallationStatuses.Draft);
        fixture.Db.Installations.Add(draft);
        fixture.Db.Domains.Add(new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = "example.test",
            DisplayName = "Example test",
            Purpose = "platform",
            IsMainPlatformDomain = true,
            DnsProvider = "desec",
            DnsZone = "example.test",
            Status = "Planned",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var state = await fixture.LockService.GetStateAsync(CancellationToken.None);

        Assert.False(state.Locked);
        Assert.Equal("available", state.ReasonCode);
        Assert.Equal(draft.Id, state.ActiveInstallationId);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_domain_without_active_setup_authority_remains_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();

        fixture.Db.Domains.Add(new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = "orphaned.example.test",
            DisplayName = "Orphaned example",
            Purpose = "platform",
            IsMainPlatformDomain = true,
            DnsProvider = "desec",
            DnsZone = "example.test",
            Status = "Active",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var state = await fixture.LockService.GetStateAsync(CancellationToken.None);

        Assert.True(state.Locked);
        Assert.Equal("established-operator-state", state.ReasonCode);
        Assert.Contains("domain", state.Evidence ?? []);
    }

    [Fact]
    public async Task STARTUP_01C_active_incomplete_installation_keeps_setup_mutations_available()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();
        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = "seq",
            ContainerName = "mem-seq-local",
            Image = "datalust/seq:latest",
            ContainerPort = 80,
            PreferredHostPort = 16341,
            SelectedHostPort = 16341,
            Status = "Running",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            LastObservedAtUtc = DateTime.UtcNow
        });
        var active = Installation("Running");
        fixture.Db.Installations.Add(active);
        await fixture.Db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = SetupRequest(
            HttpMethods.Post,
            $"/api/setup/install-runs/{active.Id}/run");

        await middleware.InvokeAsync(context, fixture.LockService);

        Assert.True(nextCalled);
        var state = await fixture.LockService.GetStateAsync(CancellationToken.None);
        Assert.False(state.Locked);
        Assert.Equal(active.Id, state.ActiveInstallationId);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01F_handoff_completion_and_support_reports_remain_available_after_lockout()
    {
        await using var fixture = await Fixture.CreateAsync();
        var completed = Installation("Succeeded");
        fixture.Db.Installations.Add(completed);
        await fixture.Db.SaveChangesAsync();

        var handoffNextCalled = false;
        var handoffMiddleware = CreateMiddleware(() => handoffNextCalled = true);
        var handoffContext = SetupRequest(
            HttpMethods.Post,
            $"/api/setup/install-runs/{completed.Id}/handoff/complete");

        await handoffMiddleware.InvokeAsync(
            handoffContext,
            fixture.LockService);

        Assert.True(handoffNextCalled);

        var currentReportNextCalled = false;
        var currentReportMiddleware = CreateMiddleware(() => currentReportNextCalled = true);
        var currentReportContext = SetupRequest(
            HttpMethods.Post,
            "/api/setup/support-report");

        await currentReportMiddleware.InvokeAsync(
            currentReportContext,
            fixture.LockService);

        Assert.True(currentReportNextCalled);

        var installationReportNextCalled = false;
        var installationReportMiddleware = CreateMiddleware(() => installationReportNextCalled = true);
        var installationReportContext = SetupRequest(
            HttpMethods.Post,
            $"/api/setup/installations/{completed.Id}/support-report");

        await installationReportMiddleware.InvokeAsync(
            installationReportContext,
            fixture.LockService);

        Assert.True(installationReportNextCalled);

        var ordinaryNextCalled = false;
        var ordinaryMiddleware = CreateMiddleware(() => ordinaryNextCalled = true);
        var ordinaryContext = SetupRequest(
            HttpMethods.Post,
            $"/api/setup/install-runs/{completed.Id}/run");

        await Assert.ThrowsAsync<MemProblemException>(() =>
            ordinaryMiddleware.InvokeAsync(
                ordinaryContext,
                fixture.LockService));

        Assert.False(ordinaryNextCalled);
    }

    [Fact]
    public async Task STARTUP_01C_read_only_setup_state_remains_available_after_installation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Installations.Add(Installation("Succeeded"));
        await fixture.Db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = SetupRequest(HttpMethods.Get, "/api/setup/start/status");

        await middleware.InvokeAsync(context, fixture.LockService);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task STARTUP_01C_operator_mutations_are_outside_the_first_time_setup_guard()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Installations.Add(Installation("Succeeded"));
        await fixture.Db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = SetupRequest(HttpMethods.Post, "/api/operator/domains");

        await middleware.InvokeAsync(context, fixture.LockService);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task STARTUP_01C_completed_installation_remains_locked_even_when_a_newer_draft_exists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var completed = Installation("Succeeded", DateTime.UtcNow.AddMinutes(-10));
        var draft = Installation("Draft", DateTime.UtcNow);
        fixture.Db.Installations.AddRange(completed, draft);
        await fixture.Db.SaveChangesAsync();

        var state = await fixture.LockService.GetStateAsync(CancellationToken.None);

        Assert.True(state.Locked);
        Assert.Equal("installation-completed", state.ReasonCode);
        Assert.Null(state.ActiveInstallationId);
    }

    private static FirstTimeSetupMutationGuardMiddleware CreateMiddleware(Action onNext) =>
        new(
            _ =>
            {
                onNext();
                return Task.CompletedTask;
            },
            NullLogger<FirstTimeSetupMutationGuardMiddleware>.Instance);

    private static DefaultHttpContext SetupRequest(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }

    private static InstallationEntity Installation(
        string status,
        DateTime? updatedAtUtc = null)
    {
        var updated = updatedAtUtc ?? DateTime.UtcNow;
        return new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = status,
            CreatedAtUtc = updated.AddMinutes(-1),
            UpdatedAtUtc = updated,
            StartedAtUtc = status is "Running" or "Succeeded"
                ? updated.AddSeconds(-30)
                : null,
            CompletedAtUtc = status == "Succeeded" ? updated : null
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private Fixture(string databasePath, MemDbContext db)
        {
            _databasePath = databasePath;
            Db = db;
            LockService = new FirstTimeSetupLockService(db);
        }

        public MemDbContext Db { get; }
        public FirstTimeSetupLockService LockService { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-startup-01c-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            return new Fixture(databasePath, db);
        }

        public async Task AddCompletedPlatformOwnerAsync()
        {
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();

            Db.Users.Add(new MemOperator
            {
                Id = userId,
                UserName = "owner",
                NormalizedUserName = "OWNER",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                IsEnabled = true,
                IsBootstrapProvisioning = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            Db.Roles.Add(new IdentityRole<Guid>
            {
                Id = roleId,
                Name = MemOperatorRoles.PlatformOwner,
                NormalizedName = MemOperatorRoles.PlatformOwner.ToUpperInvariant()
            });
            Db.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = roleId
            });

            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
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
    }
}
