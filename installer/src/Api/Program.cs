// src/Api/Program.cs

using Api.Diagnostics;
using Api.Logging;
using Api.Runtime;
using Api.Setup;
using Api.Recovery;
using Api.Support;
using Carter;
using Docker.DotNet;
using Infrastructure.Docker;
using Infrastructure.Health;
using Infrastructure.HostedServices;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Modules;
using Modules.Auth;
using Modules.Auth.Configuration;
using Modules.Integrations;
using Modules.Integrations.Seq.Services;
using Modules.Operator;
using Modules.Setup;
using Serilog;
using Serilog.Debugging;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;
using Shared.Extensions;
using HostAgent.DependencyInjection;
using HostAgent;

if (MemRuntimeManifestHostRecoveryCommand.IsRequested(args))
{
    Environment.ExitCode = await MemRuntimeManifestHostRecoveryCommand.RunAsync(args);
    return;
}

if (MemOperatorHostRecoveryCommand.IsRequested(args))
{
    Environment.ExitCode = await MemOperatorHostRecoveryCommand.RunAsync(args);
    return;
}

if (MemInstallReportHostCommand.IsRequested(args))
{
    Environment.ExitCode = await MemInstallReportHostCommand.RunAsync(args);
    return;
}

var serilogSelfLogState = new MemSerilogSelfLogState();
SelfLog.Enable(serilogSelfLogState.Record);
Log.Logger = MemLoggingBootstrap.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Resolve one server-owned description of the active Control Plane before
    // logging, stateful integrations, or Docker access are constructed. A
    // contradictory declared/observed mode fails startup rather than allowing
    // feature code to guess which runtime it is controlling.
    var runtimeContext = MemRuntimeContextBootstrap.Create(
        builder.Configuration,
        builder.Environment);
    builder.Services.AddSingleton(runtimeContext);

    // Scope optional Seq runtime identity and server-owned state to the active
    // Control Plane context before logging or Seq state stores are constructed.
    // Production keeps the established identity; local-source and containerized
    // development receive distinct names, ports, storage, secrets and state.
    SeqRuntimeContextProfile.ApplyConfigurationOverrides(
        builder.Configuration,
        runtimeContext);

    // Apply the server-owned desired Seq delivery state before Serilog and the
    // Seq options singleton are constructed. Delivery changes remain truthful:
    // they take effect only after an API restart. Authority selection is
    // resolved from the same server-owned runtime context used by Diagnostics.
    SeqDeliveryStateStore.ApplyConfigurationOverride(
        builder.Configuration,
        builder.Environment.ContentRootPath,
        runtimeContext);

    // ─────────────────────────────────────────────
    // Logging
    // ─────────────────────────────────────────────
    var loggingOptions = new MemLoggingOptions();
    builder.Configuration
        .GetSection(MemLoggingOptions.SectionName)
        .Bind(loggingOptions);
    MemLoggingOptionsValidator.ThrowIfInvalid(loggingOptions);

    var loggerBuild = MemLoggingBootstrap.CreateLogger(
        builder.Configuration,
        loggingOptions,
        builder.Environment.EnvironmentName,
        builder.Environment.ContentRootPath,
        runtimeContext);

    Log.Logger = loggerBuild.Logger;
    builder.Host.UseSerilog();

    var storageCapacityProbe = new MemStorageCapacityProbe();
    builder.Services.AddSingleton(loggingOptions);
    builder.Services.AddSingleton(new SeqLoggingRuntimeState(
        loggerBuild.SeqSinkConfigured,
        loggerBuild.SeqSinkWarningCode));
    builder.Services.AddSingleton(serilogSelfLogState);
    builder.Services.AddSingleton<IMemStorageCapacityProbe>(storageCapacityProbe);
    builder.Services.AddSingleton<IMemLocalLogHealthReader>(
        new MemLocalLogHealthState(
            loggingOptions,
            loggerBuild.PersistentFilePath,
            loggerBuild.PersistentRecorderWarning,
            serilogSelfLogState,
            storageCapacityProbe));

    if (loggerBuild.PersistentRecorderWarning is not null)
    {
        Log.Warning(
            "MEM persistent logging could not be enabled. Console logging remains active. {PersistentRecorderWarning}",
            loggerBuild.PersistentRecorderWarning);
    }
    else if (loggerBuild.PersistentFilePath is not null)
    {
        Log.Information(
            "MEM persistent structured logging enabled at {PersistentFilePath}",
            loggerBuild.PersistentFilePath);
    }

    if (loggerBuild.SeqSinkConfigured)
    {
        Log.Information("Optional Seq structured-log sink enabled.");
    }
    else if (!string.IsNullOrWhiteSpace(loggerBuild.SeqSinkWarningCode))
    {
        Log.Warning(
            "Optional Seq structured-log sink is unavailable. MEM local diagnostics remain active. {SeqWarningCode}",
            loggerBuild.SeqSinkWarningCode);
    }

    Log.Information(
        "MEM control plane starting. RuntimeMode={RuntimeMode} ControlPlaneInstanceId={ControlPlaneInstanceId} ApiProcessInstanceId={ApiProcessInstanceId} UiDeliveryMode={UiDeliveryMode} StateRootKind={StateRootKind}",
        runtimeContext.RuntimeMode,
        runtimeContext.ControlPlaneInstanceId,
        runtimeContext.ApiProcessInstanceId,
        runtimeContext.UiDeliveryMode,
        runtimeContext.StateRootKind);

    // ─────────────────────────────────────────────
    // Resolve SQLite path
    // ─────────────────────────────────────────────
    //
    // In local dev, ContentRootPath is normally:
    //   .../installer/src/Api
    //
    // In the Docker runtime image, ContentRootPath is normally:
    //   /app
    //
    // For relative SQLite paths, we anchor to the installer root in local dev,
    // but to /app in published/container mode if the /src layout is not present.
    //
    var sqliteFullPath = MemControlPlaneSqlitePathResolver.Resolve(
        builder.Configuration,
        builder.Environment);

    var contentRoot = builder.Environment.ContentRootPath;

    var possibleSrcDir = Directory.GetParent(contentRoot);
    var possibleInstallerRoot = possibleSrcDir?.Parent?.FullName;

    var installerRoot =
        possibleSrcDir?.Name.Equals("src", StringComparison.OrdinalIgnoreCase) == true
            ? possibleInstallerRoot ?? contentRoot
            : contentRoot;

    var sqliteDir = Path.GetDirectoryName(sqliteFullPath);
    if (!string.IsNullOrWhiteSpace(sqliteDir))
    {
        Directory.CreateDirectory(sqliteDir);
    }

    Log.Information("SQLite resolved path: {SqliteFullPath}", sqliteFullPath);

    // ─────────────────────────────────────────────
    // Data Protection key ring
    // ─────────────────────────────────────────────
    //
    // Normal named-operator cookies and Identity token providers must survive an
    // API container restart. Keep the key ring in the installer data volume rather
    // than the image filesystem. The deployment/bootstrapper is responsible for
    // granting this directory only to the control-plane service identity.
    //
    var dataProtectionOptions = new MemDataProtectionOptions();
    builder.Configuration
        .GetSection(MemDataProtectionOptions.SectionName)
        .Bind(dataProtectionOptions);

    if (string.IsNullOrWhiteSpace(dataProtectionOptions.ApplicationName))
    {
        throw new InvalidOperationException("DataProtection:ApplicationName is required.");
    }

    if (string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
    {
        throw new InvalidOperationException("DataProtection:KeyRingPath is required.");
    }

    var dataProtectionKeyRingPath = Path.IsPathRooted(dataProtectionOptions.KeyRingPath)
        ? dataProtectionOptions.KeyRingPath
        : Path.GetFullPath(Path.Combine(installerRoot, dataProtectionOptions.KeyRingPath));

    Directory.CreateDirectory(dataProtectionKeyRingPath);

    builder.Services
        .AddDataProtection()
        .SetApplicationName(dataProtectionOptions.ApplicationName)
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));

    Log.Information(
        "Data Protection key ring resolved path: {DataProtectionKeyRingPath}",
        dataProtectionKeyRingPath);

    // ─────────────────────────────────────────────
    // Core infra: SQLite + EF Core
    // ─────────────────────────────────────────────
    // One registration owns the scoped EF/native-connection contract. Health
    // checks and startup initialization use the same unpooled factory.
    builder.Services.AddControlPlaneSqlite(sqliteFullPath);
    Log.Information(
        "SQLite authority connections: private cache, provider pooling disabled, scoped EF ownership.");

    // Apply SQLite pragmas and ensure schema on startup.
    builder.Services.AddHostedService<SqlitePragmaInitializer>();

    // ─────────────────────────────────────────────
    // Health checks
    // ─────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("OK"), tags: ["live"])
        .AddCheck<SqliteHealthCheck>("sqlite", tags: ["ready"]);

    // ─────────────────────────────────────────────
    // Feature assembly scanning
    // ─────────────────────────────────────────────
    var modulesAssembly = typeof(ModulesAssemblyMarker).Assembly;
    var hostAgentAssembly = typeof(HostAgentAssemblyMarker).Assembly;

    var assemblies = new[]
    {
        modulesAssembly,
        hostAgentAssembly
    };

    // ─────────────────────────────────────────────
    // Framework registration
    // ─────────────────────────────────────────────
    builder.Services.AddCarterWithAssemblies(assemblies);
    builder.Services.AddMediatRWithAssemblies(assemblies);
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddProblemDetails();
    builder.Services.AddSingleton<MemExceptionClassifier>();
    builder.Services.AddSingleton<MemProblemDetailsFactory>();
    builder.Services.AddExceptionHandler<MemGlobalExceptionHandler>();

    // OpenAPI is intentionally local-development-only. It gives operators and
    // maintainers a discoverable, read-only API contract without exposing an
    // interactive API surface in normal deployed environments.
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "MEM Control Plane API",
                Version = "v1",
                Description = "Development-only OpenAPI contract for Message Easy Mode Control Plane and Host Agent endpoints."
            });

            // Swagger uses the same-origin installer session in this transition.
            // It does not expose an API-key field for Host Agent authority.
        });
    }

    // ─────────────────────────────────────────────
    // Docker infrastructure
    // ─────────────────────────────────────────────
    builder.Services.AddSingleton<DockerClient>(_ =>
    {
        return new DockerClientConfiguration(
            runtimeContext.DockerEndpoint
        ).CreateClient();
    });

    builder.Services.AddScoped<IDockerHost, DockerHost>();

    builder.Services.Configure<Core.Orchestration.OrchestrationOptions>(
        builder.Configuration.GetSection("Orchestration"));

    // ─────────────────────────────────────────────
    // Host Agent
    // ─────────────────────────────────────────────
    //
    // Privileged local host-agent services.
    // These will eventually own runtime stack provisioning:
    // Docker containers, Matrix/Element runtime setup,
    // NPM route publishing, host filesystem writes, etc.
    //
    builder.Services.AddHostAgent(builder.Configuration);

    // ─────────────────────────────────────────────
    // Module registrations
    // ─────────────────────────────────────────────
    //
    // Setup:
    //   first-run setup, host checks, domains/certificates,
    //   install plan, install run, verification, handoff.
    //
    // Operator:
    //   dashboard, services, diagnostics, and future day-to-day
    //   MEM control panel features.
    //
    // Integrations:
    //   Docker debug/runtime helpers, NPM, Postgres, Seq, PgAdmin,
    //   and other low-level adapters used by setup/operator modules.
    //
    builder.Services.AddIntegrations(builder.Configuration);
    builder.Services.AddSetupApplication(builder.Configuration);
    builder.Services.AddOperatorApplication(builder.Configuration);
    builder.Services.AddSingleton<ControlPlaneStartupDiagnosticPublisher>();
    builder.Services.AddHostedService<ControlPlaneStartupDiagnosticService>();

    // Register the named-operator Identity foundation before the legacy installer
    // unlock cookie. InstallerAuth remains the default scheme only until
    // SEC-AUTH-03 replaces the coarse transition session with real named login.
    builder.Services.AddMemOperatorIdentity(builder.Configuration, builder.Environment);
    builder.Services.AddInstallerAuth(builder.Configuration);

    var app = builder.Build();

    // ─────────────────────────────────────────────
    // Middleware
    // ─────────────────────────────────────────────
    app.UseMiddleware<MemRequestCorrelationMiddleware>();
    app.UseSerilogRequestLogging(MemRequestLogging.Configure);
    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.DocumentTitle = "MEM Control Plane API";
            options.SwaggerEndpoint("./v1/swagger.json", "MEM Control Plane API v1");
        });
    }

    // Serve embedded React app from wwwroot.
    //
    // Docker build should copy:
    //   installer/src/Web/dist -> /app/wwwroot
    //
    // This allows one image / one container:
    //   Kestrel serves API + React SPA.
    var spaIndexPath = Path.Combine(
        app.Environment.WebRootPath ?? string.Empty,
        "index.html");

    if (File.Exists(spaIndexPath))
    {
        Log.Information("React SPA detected at {SpaIndexPath}", spaIndexPath);

        app.UseDefaultFiles();
        app.UseStaticFiles();
    }
    else
    {
        Log.Warning(
            "React SPA index.html was not found. Expected path: {SpaIndexPath}. API will run, but browser SPA routes will not be served.",
            spaIndexPath);
    }

    app.UseAuthentication();
    app.UseMiddleware<MemOperatorLogContextMiddleware>();
    app.UseAuthorization();
    app.UseMiddleware<FirstTimeSetupMutationGuardMiddleware>();
    app.UseMiddleware<ControlPlaneDockerMutationGuardMiddleware>();

    // ─────────────────────────────────────────────
    // Health endpoints
    // ─────────────────────────────────────────────
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live")
    }).AllowAnonymous();

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("ready")
    }).AllowAnonymous();

    // ─────────────────────────────────────────────
    // API endpoints
    // ─────────────────────────────────────────────
    app.MapCarter();

    // ─────────────────────────────────────────────
    // React SPA fallback
    // ─────────────────────────────────────────────
    //
    // This must come after API endpoint mapping.
    // It allows direct browser refreshes on routes such as:
    //
    //   /setup
    //   /services
    //   /dashboard
    //
    if (File.Exists(spaIndexPath))
    {
        // The SPA shell must be reachable before authentication so that React
        // can render the first-owner, sign-in, startup and recovery journeys.
        // API authorization remains server-owned on the mapped API endpoints.
        app.MapFallbackToFile("index.html")
            .AllowAnonymous();
    }

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (!MemActiveApiProcessStateStore.TryWrite(runtimeContext, out var warningCode))
        {
            Log.Warning(
                "MEM could not persist the active API process identity for trusted host-command evidence. WarningCode={WarningCode}",
                warningCode);
        }

        Log.Information("MEM control plane started");
    });
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        MemActiveApiProcessStateStore.TryClear(runtimeContext);
        Log.Information("MEM control plane stopping");
    });
    app.Lifetime.ApplicationStopped.Register(() =>
        Log.Information("MEM control plane stopped"));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MEM control plane terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
    SelfLog.Disable();
}

public partial class Program { }
