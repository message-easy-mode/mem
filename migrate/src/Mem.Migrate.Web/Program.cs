using System.Text;
using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Application.Workflow;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Web.Assessment;
using Mem.Migrate.Web.Contracts;
using Mem.Migrate.Web.Hosting;
using Mem.Migrate.Web.Security;
using Mem.Migrate.Web.Workflow;
using Microsoft.AspNetCore.Http.Json;

var commandLine = SourceAssistantCommandLine.Parse(args);
if (commandLine.ShouldExit)
{
    if (!string.IsNullOrWhiteSpace(commandLine.Output))
    {
        Console.Out.WriteLine(commandLine.Output);
    }

    if (!string.IsNullOrWhiteSpace(commandLine.Error))
    {
        Console.Error.WriteLine($"ERROR: {commandLine.Error}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(SourceAssistantCommandLine.Usage());
    }

    return commandLine.ExitCode;
}

var options = commandLine.Options ?? throw new InvalidOperationException("Source Assistant options were not produced.");
var generatedAccess = AccessCodeCredential.Generate();
var accessCredential = generatedAccess.Credential;
var accessDisplayCode = generatedAccess.DisplayCode;
generatedAccess = generatedAccess with { DisplayCode = string.Empty };
var startedAtUtc = DateTimeOffset.UtcNow;
var applicationVersion = SourceAssistantCommandLine.GetApplicationVersion();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = Array.Empty<string>(),
    ApplicationName = typeof(SourceAssistantOptions).Assembly.GetName().Name,
    ContentRootPath = AppContext.BaseDirectory
});

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(options.ListenAddress, options.Port);
    kestrel.Limits.MaxRequestBodySize = 16 * 1024;
    kestrel.AddServerHeader = false;
});

builder.Services.Configure<JsonOptions>(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    json.SerializerOptions.WriteIndented = false;
});

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(accessCredential);
builder.Services.AddSingleton(serviceProvider => new AccessCodeAttemptLimiter(serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(serviceProvider => new SourceAssistantSessionStore(
    serviceProvider.GetRequiredService<TimeProvider>(),
    options.SessionIdleTimeout,
    options.SessionAbsoluteTimeout));
builder.Services.AddSingleton<ISourceAssessmentApplicationService>(_ =>
    SourceAssessmentRuntimeFactory.CreateApplication(new AssessmentOptions
    {
        WorkspacePath = options.WorkspaceRoot,
        OutputPath = options.ArtifactRoot,
        IncludeSensitivePaths = false,
        JsonConsoleOutput = false,
        NonInteractive = true
    }));
builder.Services.AddSingleton<SourceStackSelectionStore>();
builder.Services.AddSingleton<SourceAssessmentCoordinator>();
builder.Services.AddSingleton(serviceProvider => new SourcePreflightService(
    serviceProvider.GetRequiredService<ISourceAssessmentApplicationService>(),
    options.WorkspaceRoot,
    options.ArtifactRoot));
builder.Services.AddSingleton(_ =>
    SourceMigrationRuntimeFactory.Create(
        new AssessmentOptions
        {
            WorkspacePath = options.WorkspaceRoot,
            OutputPath = options.ArtifactRoot,
            IncludeSensitivePaths = false,
            JsonConsoleOutput = false,
            NonInteractive = true
        },
        applicationVersion));
builder.Services.AddSingleton<ISourceMigrationApplicationService>(
    serviceProvider =>
        serviceProvider.GetRequiredService<SourceMigrationApplicationService>());
builder.Services.AddSingleton<ISourcePackageLifecycleService>(
    serviceProvider =>
        serviceProvider.GetRequiredService<SourceMigrationApplicationService>());
builder.Services.AddSingleton<SourceWorkflowCoordinator>();
builder.Services.AddSingleton<SourcePackageDownloadCoordinator>();

var app = builder.Build();

var sourceMigrationService = app.Services.GetRequiredService<ISourceMigrationApplicationService>();
await sourceMigrationService.InitializeAsync(CancellationToken.None);
var durableWorkflow = await sourceMigrationService.GetLatestAsync(CancellationToken.None);
if (durableWorkflow is not null)
{
    var selectionStore = app.Services.GetRequiredService<SourceStackSelectionStore>();
    selectionStore.Select(
        durableWorkflow.AssessmentId,
        durableWorkflow.SelectedSourceStackId);
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers["Cache-Control"] = "no-store";
        context.Context.Response.Headers["Pragma"] = "no-cache";
    }
});
app.UseMiddleware<ApiSecurityMiddleware>();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse(
    1,
    "ready",
    "MEM Migrate Source Assistant",
    applicationVersion,
    startedAtUtc)));

app.MapPost("/api/access/login", async (
    HttpContext context,
    AccessCodeCredential credential,
    AccessCodeAttemptLimiter limiter,
    SourceAssistantSessionStore sessionStore) =>
{
    var attemptKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!limiter.CanAttempt(attemptKey, out var retryAfter))
    {
        context.Response.Headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many access-code attempts",
            detail: "Wait before trying the access code again.");
    }

    AccessLoginRequest? request;
    try
    {
        request = await context.Request.ReadFromJsonAsync<AccessLoginRequest>(context.RequestAborted);
    }
    catch (Exception exception) when (exception is System.Text.Json.JsonException or BadHttpRequestException or InvalidOperationException)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid request",
            detail: "Provide the Source Assistant access code.");
    }

    if (!credential.Verify(request?.AccessCode))
    {
        limiter.RecordFailure(attemptKey);
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Access denied",
            detail: "The access code was not accepted.");
    }

    limiter.RecordSuccess(attemptKey);
    var session = sessionStore.Create();
    context.Response.Cookies.Append(
        SourceAssistantSessionStore.CookieName,
        session.CookieToken,
        new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
            IsEssential = true
        });

    return Results.Ok(new AccessSessionResponse(1, true, session.CsrfToken, session.ExpiresAtUtc));
});

app.MapGet("/api/access/session", (
    HttpContext context,
    SourceAssistantSessionStore sessionStore) =>
{
    context.Request.Cookies.TryGetValue(SourceAssistantSessionStore.CookieName, out var cookieToken);
    return sessionStore.TryGet(cookieToken, out var session) && session is not null
        ? Results.Ok(new AccessSessionResponse(1, true, session.CsrfToken, session.ExpiresAtUtc))
        : Results.Ok(new AccessSessionResponse(1, false, null, null));
});

app.MapPost("/api/access/logout", (
    HttpContext context,
    SourceAssistantSessionStore sessionStore) =>
{
    context.Request.Cookies.TryGetValue(SourceAssistantSessionStore.CookieName, out var cookieToken);
    sessionStore.Revoke(cookieToken);
    context.Response.Cookies.Delete(
        SourceAssistantSessionStore.CookieName,
        new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/"
        });
    return Results.NoContent();
});

app.MapGet("/api/host/status", (SourceAssistantOptions hostOptions) => Results.Ok(new HostStatusResponse(
    1,
    "source-assessment-ready",
    hostOptions.ListenerDisplay,
    hostOptions.IsLoopback,
    hostOptions.AllowRemote,
    (int)hostOptions.SessionIdleTimeout.TotalMinutes,
    (int)hostOptions.SessionAbsoluteTimeout.TotalMinutes,
    true)));

app.MapGet("/api/preflight", async (
    SourcePreflightService preflight,
    SourceAssessmentCoordinator coordinator,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    try
    {
        return Results.Ok(await preflight.RunAsync(coordinator.IsRunning || workflowCoordinator.IsRunning, context.RequestAborted));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        app.Logger.LogWarning(exception, "Source preflight could not inspect the assessment journal.");
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Source preflight unavailable",
            detail: "The Source Assistant could not inspect its private source workspace.");
    }
});

app.MapGet("/api/assessments/latest", async (
    SourceAssessmentCoordinator coordinator,
    SourceStackSelectionStore selectionStore,
    HttpContext context) =>
{
    try
    {
        var assessment = await coordinator.GetLatestAsync(context.RequestAborted);
        return Results.Ok(ToEnvelope(assessment, coordinator, selectionStore));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        app.Logger.LogWarning(exception, "The latest source assessment could not be read.");
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Assessment unavailable",
            detail: "The latest source assessment could not be read safely.");
    }
});

app.MapPost("/api/assessments", async (
    SourceAssessmentCoordinator coordinator,
    SourceStackSelectionStore selectionStore,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    try
    {
        if (workflowCoordinator.IsRunning)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Source operation running",
                detail: "Wait for the active capture or packaging operation to finish before reassessing the source.");
        }

        var assessment = await coordinator.RunAsync(context.RequestAborted);
        return Results.Ok(ToEnvelope(assessment, coordinator, selectionStore));
    }
    catch (SourceAssessmentAlreadyRunningException)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Assessment already running",
            detail: "Wait for the active source assessment to finish.");
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.Problem(
            statusCode: 499,
            title: "Assessment request ended",
            detail: "The browser request ended before the source assessment completed. Reopen the Source Assistant and inspect the latest durable result.");
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Source assessment failed.");
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Source assessment failed",
            detail: "The source could not be assessed. Review the source host and application logs before retrying.");
    }
});

app.MapPost("/api/source-stacks/{sourceStackId}/select", async (
    string sourceStackId,
    SourceAssessmentCoordinator coordinator,
    SourceStackSelectionStore selectionStore,
    HttpContext context) =>
{
    var assessment = await coordinator.GetLatestAsync(context.RequestAborted);
    if (assessment is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Assessment required",
            detail: "Run a source assessment before selecting a stack.");
    }

    var stack = assessment.Stacks.FirstOrDefault(candidate =>
        string.Equals(candidate.SourceStackId, sourceStackId, StringComparison.OrdinalIgnoreCase));
    if (stack is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source stack not found",
            detail: "The selected stack is not present in the latest source assessment.");
    }

    selectionStore.Select(assessment.AssessmentId, stack.SourceStackId);
    return Results.Ok(ToEnvelope(assessment, coordinator, selectionStore));
});

app.MapGet("/api/workflows/latest", async (
    ISourceMigrationApplicationService migrationService,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    var workflow = await migrationService.GetLatestAsync(context.RequestAborted);
    return Results.Ok(new SourceWorkflowEnvelope(
        SchemaVersion: 1,
        Available: workflow is not null,
        Running: workflowCoordinator.IsRunning,
        Workflow: workflow));
});

app.MapGet("/api/workflows/overview", async (
    string? sourceStackId,
    int? limit,
    ISourceMigrationApplicationService migrationService,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    try
    {
        var overview = await migrationService.GetOverviewAsync(
            sourceStackId ?? string.Empty,
            limit ?? 10,
            workflowCoordinator.IsRunning,
            context.RequestAborted);
        return Results.Ok(new SourceWorkflowOverviewResponse(
            overview.SchemaVersion,
            overview.SourceStackId,
            overview.OperationRunning,
            overview.CanStartNewWorkflow,
            overview.StartNewBlockedReason,
            overview.CurrentWorkflow,
            overview.PreviousWorkflows));
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid workflow overview request",
            detail: exception.Message);
    }
});

app.MapGet("/api/workflows/{workflowId}", async (
    string workflowId,
    ISourceMigrationApplicationService migrationService,
    HttpContext context) =>
{
    try
    {
        var workflow = await migrationService.GetAsync(
            workflowId,
            context.RequestAborted);
        if (workflow is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Source workflow not found",
                detail: "The source workflow was not found.");
        }

        return Results.Ok(workflow);
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid source workflow",
            detail: exception.Message);
    }
});

app.MapPost("/api/intake-requests/validate", async (
    SourceRequestImportRequest request,
    ISourceMigrationApplicationService migrationService,
    SourceAssessmentCoordinator assessmentCoordinator,
    SourceStackSelectionStore selectionStore,
    HttpContext context) =>
{
    try
    {
        var assessment = await assessmentCoordinator.GetLatestAsync(context.RequestAborted);
        if (assessment is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Assessment required",
                detail: "Run a source assessment before importing a target migration request.");
        }

        var selectedStackId = selectionStore.GetSelectedSourceStackId(assessment.AssessmentId);
        if (string.IsNullOrWhiteSpace(selectedStackId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Source stack required",
                detail: "Select the stack intended for migration before importing the target request.");
        }

        var result = await migrationService.ImportRequestAsync(
            request.Request,
            assessment,
            selectedStackId,
            request.EncryptionReadinessAcknowledged,
            context.RequestAborted);
        return Results.Ok(result);
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid migration request",
            detail: exception.Message);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Migration request cannot be used",
            detail: exception.Message);
    }
});

app.MapGet("/api/workflows/{workflowId}/captures", async (
    string workflowId,
    ISourceMigrationApplicationService migrationService,
    HttpContext context) =>
{
    try
    {
        return Results.Ok(await migrationService.ListCapturesAsync(
            workflowId,
            context.RequestAborted));
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid source workflow",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
});

app.MapPost("/api/workflows/{workflowId}/captures/{captureId}/select", async (
    string workflowId,
    string captureId,
    ISourceMigrationApplicationService migrationService,
    HttpContext context) =>
{
    try
    {
        return Results.Ok(await migrationService.SelectCaptureAsync(
            workflowId,
            captureId,
            context.RequestAborted));
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid capture selection",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Capture cannot be selected",
            detail: exception.Message);
    }
});

app.MapPost("/api/workflows/{workflowId}/capture", async (
    string workflowId,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    try
    {
        var workflow = await workflowCoordinator.StartCaptureAsync(
            workflowId,
            context.RequestAborted);
        return Results.Accepted("/api/workflows/latest", workflow);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Capture cannot start",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
});

app.MapPost("/api/workflows/{workflowId}/package", async (
    string workflowId,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    try
    {
        var workflow = await workflowCoordinator.StartPackageAsync(
            workflowId,
            context.RequestAborted);
        return Results.Accepted("/api/workflows/latest", workflow);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package creation cannot start",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
});

app.MapGet("/api/workflows/{workflowId}/package/download", async (
    string workflowId,
    SourcePackageDownloadCoordinator downloadCoordinator,
    HttpContext context) =>
{
    try
    {
        var lease = await downloadCoordinator.BeginAsync(
            workflowId,
            context.RequestAborted);
        context.Response.RegisterForDispose(lease);
        context.Response.Headers["X-MEM-Package-SHA256"] =
            lease.Descriptor.Sha256;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";

        return Results.File(
            lease.Descriptor.FullPath,
            lease.Descriptor.ContentType,
            lease.Descriptor.FileName,
            lastModified: lease.Descriptor.LastModifiedUtc,
            enableRangeProcessing: true);
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid source workflow",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package download unavailable",
            detail: exception.Message);
    }
    catch (Exception exception) when (
        exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        app.Logger.LogWarning(
            exception,
            "The local encrypted package could not be opened safely for workflow {WorkflowId}.",
            workflowId);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package download unavailable",
            detail: "The local encrypted package could not be opened safely. Review the source package workspace.");
    }
});

app.MapGet("/api/workflows/{workflowId}/package/report", async (
    string workflowId,
    ISourcePackageLifecycleService lifecycleService,
    HttpContext context) =>
{
    try
    {
        var report = await lifecycleService.GetPackageReportAsync(
            workflowId,
            context.RequestAborted);
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";
        return Results.File(
            report.Contents,
            report.ContentType,
            report.FileName,
            enableRangeProcessing: false);
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid source workflow",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package report unavailable",
            detail: exception.Message);
    }
    catch (Exception exception) when (
        exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        app.Logger.LogWarning(
            exception,
            "The browser-safe package report could not be generated for workflow {WorkflowId}.",
            workflowId);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package report unavailable",
            detail: "The browser-safe package report could not be generated.");
    }
});

app.MapDelete("/api/workflows/{workflowId}/package", async (
    string workflowId,
    SourcePackageDownloadCoordinator downloadCoordinator,
    HttpContext context) =>
{
    try
    {
        return Results.Ok(await downloadCoordinator.DeleteAsync(
            workflowId,
            context.RequestAborted));
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid source workflow",
            detail: exception.Message);
    }
    catch (SourceWorkflowNotFoundException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Source workflow not found",
            detail: exception.Message);
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package deletion unavailable",
            detail: exception.Message);
    }
    catch (Exception exception) when (
        exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        app.Logger.LogWarning(
            exception,
            "The local encrypted package could not be deleted safely for workflow {WorkflowId}.",
            workflowId);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Package deletion incomplete",
            detail: "The Source Assistant could not delete every local package artifact. Review the source package workspace before retrying.");
    }
});

app.MapPost("/api/workflows/{workflowId}/cancel", async (
    string workflowId,
    SourceWorkflowCoordinator workflowCoordinator,
    HttpContext context) =>
{
    try
    {
        return Results.Accepted(
            "/api/workflows/latest",
            await workflowCoordinator.CancelAsync(
                workflowId,
                context.RequestAborted));
    }
    catch (SourceWorkflowConflictException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Cancellation unavailable",
            detail: exception.Message);
    }
});

app.MapGet("/api/assessments/latest/report", async (
    SourceAssessmentCoordinator coordinator,
    HttpContext context) =>
{
    var report = await coordinator.GetLatestReportAsync(context.RequestAborted);
    if (report is null)
    {
        return Results.NotFound();
    }

    return Results.File(
        Encoding.UTF8.GetBytes(report.Markdown + Environment.NewLine),
        "text/markdown; charset=utf-8",
        report.FileName,
        enableRangeProcessing: false);
});

var indexPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
if (File.Exists(indexPath))
{
    app.MapFallbackToFile("index.html");
}

await app.StartAsync();

Console.WriteLine();
Console.WriteLine("MEM Migrate Source Assistant is ready.");
Console.WriteLine();
Console.WriteLine($"Local URL:  {options.ListenerDisplay}");
Console.WriteLine($"Access code: {accessDisplayCode}");
accessDisplayCode = string.Empty;
Console.WriteLine($"Session:     {(int)options.SessionIdleTimeout.TotalMinutes} minutes idle · {(int)options.SessionAbsoluteTimeout.TotalMinutes} minutes maximum");
Console.WriteLine();
if (options.IsLoopback)
{
    Console.WriteLine("Remote browser access:");
    Console.WriteLine($"  ssh -L {options.Port}:{options.TunnelTargetDisplay}:{options.Port} <operator>@<source-host>");
    Console.WriteLine($"  then open http://localhost:{options.Port}");
}
else
{
    Console.WriteLine("WARNING: Remote binding is enabled. Use only on a trusted LAN or VPN.");
    Console.WriteLine("Public internet exposure is unsupported.");
}
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop the temporary management surface.");

await app.WaitForShutdownAsync();
return 0;

static SourceAssessmentEnvelope ToEnvelope(
    SourceAssessmentView? assessment,
    SourceAssessmentCoordinator coordinator,
    SourceStackSelectionStore selectionStore) =>
    new(
        SchemaVersion: 1,
        Available: assessment is not null,
        Running: coordinator.IsRunning,
        SelectedSourceStackId: assessment is null
            ? null
            : selectionStore.GetSelectedSourceStackId(assessment.AssessmentId),
        Assessment: assessment);
