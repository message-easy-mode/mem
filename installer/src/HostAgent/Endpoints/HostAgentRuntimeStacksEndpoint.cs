using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Readiness;
using HostAgent.Runtime.Stacks.Destroy;
using HostAgent.Runtime.Stacks.Diagnostics;
using HostAgent.Runtime.Stacks.Inventory;
using HostAgent.Runtime.Stacks.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HostAgent.Security;

namespace HostAgent.Endpoints;

public sealed class HostAgentRuntimeStacksEndpoint : ICarterModule
{
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DestroyAcceptanceTimeout = TimeSpan.FromSeconds(10);
    private static readonly SemaphoreSlim DestroyAcceptanceGate = new(1, 1);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new RuntimeStackInspectResponse(
                                Source: "control-plane",
                                Status: "unauthorized",
                                StackId: null,
                                Slug: slugOrId,
                                Matrix: null,
                                Element: null,
                                LastVerifiedAtUtc: null,
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(
                        slugOrId,
                        ct);

                    if (manifest is null)
                    {
                        return Results.NotFound(new RuntimeStackInspectResponse(
                            Source: "control-plane",
                            Status: "not_found",
                            StackId: null,
                            Slug: slugOrId,
                            Matrix: null,
                            Element: null,
                            LastVerifiedAtUtc: null,
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    return Results.Ok(RuntimeStackInspectResponse.FromManifest(
                        manifest,
                        DateTimeOffset.UtcNow));
                })
            .WithName("InspectHostAgentRuntimeStack")
            .WithTags("HostAgent");

        app.MapGet(
                "/internal/host-agent/runtime-stacks",
                async (
                    HttpContext httpContext,
                    int? page,
                    int? pageSize,
                    string? search,
                    string? status,
                    string? category,
                    string? sortBy,
                    string? sortDirection,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new RuntimeStackListResponse(
                                Source: "control-plane",
                                Status: "unauthorized",
                                Stacks: [],
                                Detail: auth.Detail,
                                Summary: new RuntimeStackInventorySummary(0, 0, 0, 0, 0, 0),
                                TotalMatchingStacks: 0,
                                Page: 1,
                                PageSize: 10,
                                TotalPages: 1,
                                HasPreviousPage: false,
                                HasNextPage: false,
                                IsPaged: page.HasValue || pageSize.HasValue,
                                Categories: []),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifests = await manifestStore.ListAsync(ct);

                    try
                    {
                        var observedAtUtc = DateTimeOffset.UtcNow;
                        var inventory = RuntimeStackInventoryQuery.Apply(
                            manifests,
                            new RuntimeStackInventoryRequest(
                                Page: page,
                                PageSize: pageSize,
                                Search: search,
                                Status: status,
                                SortBy: sortBy,
                                SortDirection: sortDirection,
                                Category: category),
                            observedAtUtc);

                        return Results.Ok(new RuntimeStackListResponse(
                            Source: "control-plane",
                            Status: "ok",
                            Stacks: inventory.Stacks
                                .Select(manifest => RuntimeStackSummaryResponse.FromManifest(
                                    manifest,
                                    observedAtUtc))
                                .ToArray(),
                            Detail: inventory.TotalMatchingStacks == 0 && inventory.Summary.TotalStacks > 0
                                ? "No runtime stacks match the current inventory filters."
                                : null,
                            Summary: inventory.Summary,
                            TotalMatchingStacks: inventory.TotalMatchingStacks,
                            Page: inventory.Page,
                            PageSize: inventory.PageSize,
                            TotalPages: inventory.TotalPages,
                            HasPreviousPage: inventory.HasPreviousPage,
                            HasNextPage: inventory.HasNextPage,
                            IsPaged: inventory.IsPaged,
                            Categories: inventory.Categories));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new
                        {
                            error = "invalid_runtime_stack_list_request",
                            detail = ex.Message
                        });
                    }
                })
            .WithName("ListHostAgentRuntimeStacks")
            .WithTags("HostAgent");

        app.MapPut(
                "/internal/host-agent/runtime-stacks/{slugOrId}/identity",
                async (
                    string slugOrId,
                    RuntimeStackIdentityUpdateRequest request,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "unauthorized",
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);
                    if (manifest is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "runtime_stack_not_found",
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    try
                    {
                        var displayName = RuntimeStackIdentity.NormalizeDisplayName(
                            request.DisplayName,
                            manifest.Slug);
                        var category = RuntimeStackIdentity.NormalizeCategory(request.Category);
                        var updated = await manifestStore.UpdateIdentityAsync(
                            manifest.StackId,
                            displayName,
                            category,
                            ct);

                        return updated is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                Error: "runtime_stack_not_found",
                                Detail: $"Runtime stack '{slugOrId}' was not found while updating its identity."))
                            : Results.Ok(RuntimeStackInspectResponse.FromManifest(updated, DateTimeOffset.UtcNow));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_runtime_stack_identity",
                            Detail: ex.Message));
                    }
                })
            .WithName("UpdateHostAgentRuntimeStackIdentity")
            .WithTags("HostAgent");

        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}/logo",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeStackLogoService logoService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);
                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "unauthorized",
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);
                    if (manifest is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "runtime_stack_not_found",
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    var logo = logoService.Resolve(manifest);
                    if (logo is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "runtime_stack_logo_not_found",
                            Detail: $"Runtime stack '{manifest.Slug}' does not have an available custom logo."));
                    }

                    httpContext.Response.Headers.CacheControl = "private, max-age=86400";
                    httpContext.Response.Headers.ETag = $"\"{logo.Sha256}\"";
                    return Results.File(
                        logo.Path,
                        contentType: logo.ContentType,
                        enableRangeProcessing: false);
                })
            .WithName("GetHostAgentRuntimeStackLogo")
            .WithTags("HostAgent");

        app.MapPut(
                "/internal/host-agent/runtime-stacks/{slugOrId}/logo",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    IFormFile? file,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeStackLogoService logoService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);
                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "unauthorized",
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    if (file is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "stack_logo_upload_missing",
                            Detail: "Upload a PNG stack logo using multipart/form-data field name 'file'."));
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);
                    if (manifest is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "runtime_stack_not_found",
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    try
                    {
                        var updated = await logoService.UploadAsync(manifest, file, ct);
                        return Results.Ok(RuntimeStackInspectResponse.FromManifest(updated, DateTimeOffset.UtcNow));
                    }
                    catch (RuntimeStackLogoValidationException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: ex.Code,
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status422UnprocessableEntity);
                    }
                    catch (IOException)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "stack_logo_storage_failed",
                                Detail: "The stack logo could not be stored safely. Existing logo state was preserved where possible."),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "stack_logo_storage_failed",
                                Detail: "The stack logo could not be stored because the Control Plane does not have the required filesystem access."),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(
                RuntimeStackLogoService.MaximumBytes + 64 * 1024))
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("PutHostAgentRuntimeStackLogo")
            .WithTags("HostAgent");

        app.MapDelete(
                "/internal/host-agent/runtime-stacks/{slugOrId}/logo",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeStackLogoService logoService,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);
                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "unauthorized",
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);
                    if (manifest is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "runtime_stack_not_found",
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    try
                    {
                        var updated = await logoService.RemoveAsync(manifest, ct);
                        return Results.Ok(RuntimeStackInspectResponse.FromManifest(updated, DateTimeOffset.UtcNow));
                    }
                    catch (IOException)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "stack_logo_remove_failed",
                                Detail: "The stack logo could not be removed safely."),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "stack_logo_remove_failed",
                                Detail: "The stack logo could not be removed because the Control Plane does not have the required filesystem access."),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                })
            .WithName("DeleteHostAgentRuntimeStackLogo")
            .WithTags("HostAgent");

        app.MapGet(
        "/internal/host-agent/runtime-stacks/{slugOrId}/operations",
        async (
            string slugOrId,
            int? limit,
            HttpContext httpContext,
            [FromServices] RuntimeStackManifestStore manifestStore,
            [FromServices] RuntimeOperationStore operationStore,
            CancellationToken ct) =>
        {
            var auth = Authorize(httpContext);

            if (!auth.Authorized)
            {
                return Results.Json(
                    new RuntimeStackOperationsResponse(
                        Source: "control-plane",
                        Status: "unauthorized",
                        StackId: null,
                        Slug: slugOrId,
                        Operations: [],
                        Detail: auth.Detail),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var manifest = await manifestStore.FindAsync(
                slugOrId,
                ct);

            if (manifest is null)
            {
                return Results.NotFound(new RuntimeStackOperationsResponse(
                    Source: "control-plane",
                    Status: "not_found",
                    StackId: null,
                    Slug: slugOrId,
                    Operations: [],
                    Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
            }

            var operations = await operationStore.ListForStackAsync(
                manifest.StackId,
                limit.GetValueOrDefault(25),
                ct);

            return Results.Ok(new RuntimeStackOperationsResponse(
                Source: "control-plane",
                Status: "ok",
                StackId: manifest.StackId,
                Slug: manifest.Slug,
                Operations: operations
                    .Select(RuntimeStackOperationResponse.FromSummary)
                    .ToArray(),
                Detail: null));
        })
    .WithName("ListHostAgentRuntimeStackOperations")
    .WithTags("HostAgent");

        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}/doctor/latest",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeReadinessReportStore readinessReportStore,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new RuntimeStackLatestDoctorResponse(
                                Source: "control-plane",
                                Status: "unauthorized",
                                StackId: null,
                                Slug: slugOrId,
                                Report: null,
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);

                    if (manifest is null)
                    {
                        return Results.NotFound(new RuntimeStackLatestDoctorResponse(
                            Source: "control-plane",
                            Status: "not_found",
                            StackId: null,
                            Slug: slugOrId,
                            Report: null,
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    var snapshot = await readinessReportStore.GetLatestAsync(
                        manifest.StackId,
                        reportKind: "doctor",
                        ct);

                    if (snapshot is null)
                    {
                        return Results.Ok(new RuntimeStackLatestDoctorResponse(
                            Source: "control-plane",
                            Status: "not_recorded",
                            StackId: manifest.StackId,
                            Slug: manifest.Slug,
                            Report: null,
                            Detail: "No persisted Doctor report has been recorded for this runtime stack yet."));
                    }

                    var report = RuntimeStackDoctorResponse.FromPersisted(manifest, snapshot);

                    return Results.Ok(new RuntimeStackLatestDoctorResponse(
                        Source: "control-plane",
                        Status: "ok",
                        StackId: manifest.StackId,
                        Slug: manifest.Slug,
                        Report: report,
                        Detail: null));
                })
            .WithName("GetLatestHostAgentRuntimeStackDoctor")
            .WithTags("HostAgent");

        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}/doctor/history",
                async (
                    string slugOrId,
                    int? page,
                    int? pageSize,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeReadinessReportStore readinessReportStore,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            RuntimeStackDoctorHistoryResponse.Empty(
                                slugOrId,
                                status: "unauthorized",
                                detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);

                    if (manifest is null)
                    {
                        return Results.NotFound(RuntimeStackDoctorHistoryResponse.Empty(
                            slugOrId,
                            status: "not_found",
                            detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    var effectivePage = page ?? 1;
                    var effectivePageSize = pageSize ?? RuntimeReadinessReportStore.DefaultHistoryPageSize;

                    if (effectivePage < 1 ||
                        effectivePageSize < 1 ||
                        effectivePageSize > RuntimeReadinessReportStore.MaximumHistoryPageSize)
                    {
                        return Results.BadRequest(new
                        {
                            error = "invalid_doctor_history_request",
                            detail = $"Doctor history page must be at least 1 and pageSize must be between 1 and {RuntimeReadinessReportStore.MaximumHistoryPageSize}."
                        });
                    }

                    var history = await readinessReportStore.ListPreviousAsync(
                        manifest.StackId,
                        reportKind: "doctor",
                        effectivePage,
                        effectivePageSize,
                        ct);

                    return Results.Ok(new RuntimeStackDoctorHistoryResponse(
                        Source: "control-plane",
                        Status: "ok",
                        StackId: manifest.StackId,
                        Slug: manifest.Slug,
                        Reports: history.Reports
                            .Select(RuntimeStackDoctorHistoryItemResponse.FromSnapshot)
                            .ToArray(),
                        TotalCount: history.TotalCount,
                        Page: history.Page,
                        PageSize: history.PageSize,
                        TotalPages: history.TotalPages,
                        HasPreviousPage: history.HasPreviousPage,
                        HasNextPage: history.HasNextPage,
                        Detail: history.TotalCount == 0
                            ? "No previous completed Doctor reports have been recorded for this runtime stack yet."
                            : null));
                })
            .WithName("ListHostAgentRuntimeStackDoctorHistory")
            .WithTags("HostAgent");

        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}/doctor/reports/{reportId:guid}",
                async (
                    string slugOrId,
                    Guid reportId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeReadinessReportStore readinessReportStore,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "unauthorized",
                                Detail: auth.Detail),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(slugOrId, ct);

                    if (manifest is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "runtime_stack_not_found",
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store."));
                    }

                    var snapshot = await readinessReportStore.GetByIdAsync(
                        manifest.StackId,
                        reportKind: "doctor",
                        reportId,
                        ct);

                    if (snapshot is null)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "doctor_report_not_found",
                            Detail: $"Doctor report '{reportId}' was not found for runtime stack '{manifest.Slug}'."));
                    }

                    return Results.Ok(RuntimeStackDoctorResponse.FromPersisted(manifest, snapshot));
                })
            .WithName("GetHostAgentRuntimeStackDoctorReport")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/doctor",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] RuntimeStackManifestStore manifestStore,
                    [FromServices] RuntimeReadinessVerifier readinessVerifier,
                    [FromServices] RuntimeReadinessReportStore readinessReportStore,
                    [FromServices] RuntimeOperationStore operationStore,
                    [FromServices] RuntimeStackDoctorExecutionLimiter doctorExecutionLimiter,
                    [FromServices] IServiceScopeFactory serviceScopeFactory,
                    CancellationToken ct) =>
                {
                    var auth = Authorize(httpContext);

                    if (!auth.Authorized)
                    {
                        return Results.Json(
                            new RuntimeStackDoctorResponse(
                                Source: "control-plane",
                                Status: "unauthorized",
                                StackId: null,
                                Slug: slugOrId,
                                LastVerifiedStatus: null,
                                LastVerifiedAtUtc: null,
                                CheckedAtUtc: DateTimeOffset.UtcNow,
                                AllPassed: false,
                                Checks: [],
                                Detail: auth.Detail,
                                OperationId: null,
                                ReportId: null),
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var manifest = await manifestStore.FindAsync(
                        slugOrId,
                        ct);

                    if (manifest is null)
                    {
                        return Results.NotFound(new RuntimeStackDoctorResponse(
                            Source: "control-plane",
                            Status: "not_found",
                            StackId: null,
                            Slug: slugOrId,
                            LastVerifiedStatus: null,
                            LastVerifiedAtUtc: null,
                            CheckedAtUtc: DateTimeOffset.UtcNow,
                            AllPassed: false,
                            Checks: [],
                            Detail: $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store.",
                            OperationId: null,
                            ReportId: null));
                    }

                    if (manifest.Element is null)
                    {
                        return Results.BadRequest(new RuntimeStackDoctorResponse(
                            Source: "control-plane",
                            Status: "unsupported",
                            StackId: manifest.StackId,
                            Slug: manifest.Slug,
                            LastVerifiedStatus: manifest.LastVerifiedStatus,
                            LastVerifiedAtUtc: manifest.LastVerifiedAtUtc,
                            CheckedAtUtc: DateTimeOffset.UtcNow,
                            AllPassed: false,
                            Checks: [],
                            Detail: "Runtime stack doctor currently requires both Matrix and Element service manifests.",
                            OperationId: null,
                            ReportId: null));
                    }

                    if (string.IsNullOrWhiteSpace(manifest.Matrix.InternalBaseUrl) ||
                        string.IsNullOrWhiteSpace(manifest.Element.InternalBaseUrl) ||
                        string.IsNullOrWhiteSpace(manifest.Matrix.PublicBaseUrl) ||
                        string.IsNullOrWhiteSpace(manifest.Element.PublicBaseUrl) ||
                        string.IsNullOrWhiteSpace(manifest.Matrix.PublicHost) ||
                        string.IsNullOrWhiteSpace(manifest.Element.PublicHost) ||
                        string.IsNullOrWhiteSpace(manifest.Matrix.InternalHost) ||
                        string.IsNullOrWhiteSpace(manifest.Element.InternalHost))
                    {
                        return Results.BadRequest(new RuntimeStackDoctorResponse(
                            Source: "control-plane",
                            Status: "manifest_incomplete",
                            StackId: manifest.StackId,
                            Slug: manifest.Slug,
                            LastVerifiedStatus: manifest.LastVerifiedStatus,
                            LastVerifiedAtUtc: manifest.LastVerifiedAtUtc,
                            CheckedAtUtc: DateTimeOffset.UtcNow,
                            AllPassed: false,
                            Checks: [],
                            Detail: "Runtime stack manifest is missing required Matrix/Element URL or host fields.",
                            OperationId: null,
                            ReportId: null));
                    }

                    var operationId = await operationStore.StartAsync(
                        runtimeStackId: manifest.StackId,
                        operation: "doctor-stack",
                        idempotencyKey: null,
                        requestedBy: "mem-cli",
                        hostMutationLevel: "none",
                        input: new
                        {
                            manifest.StackId,
                            manifest.Slug,
                            manifest.LastVerifiedStatus,
                            manifest.LastVerifiedAtUtc
                        },
                        ct);

                    try
                    {
                        var response = await doctorExecutionLimiter.ExecuteAsync(
                            async doctorCt =>
                            {
                                var verification = await readinessVerifier.VerifyAsync(
                                    new RuntimeReadinessVerificationRequest(
                                        MatrixInternalBaseUrl: manifest.Matrix.InternalBaseUrl,
                                        ElementInternalBaseUrl: manifest.Element.InternalBaseUrl,
                                        MatrixPublicBaseUrl: manifest.Matrix.PublicBaseUrl,
                                        ElementPublicBaseUrl: manifest.Element.PublicBaseUrl,
                                        MatrixPublicHost: manifest.Matrix.PublicHost,
                                        ElementPublicHost: manifest.Element.PublicHost,
                                        MatrixForwardHost: manifest.Matrix.InternalHost,
                                        MatrixForwardPort: 8008,
                                        ElementForwardHost: manifest.Element.InternalHost,
                                        ElementForwardPort: 80,
                                        ExpectedNpmCertificateId: manifest.Matrix.NpmCertificateId ?? manifest.Element.NpmCertificateId),
                                    doctorCt);

                                var reportId = await readinessReportStore.SaveAsync(
                                    manifest,
                                    verification,
                                    reportKind: "doctor",
                                    triggeredBy: "mem-cli",
                                    operationId: operationId,
                                    doctorCt);

                                var checkedAtUtc = DateTimeOffset.UtcNow;
                                var verificationStatus = verification.AllPassed ? "passed" : "failed";
                                var verifiedManifest = await manifestStore.UpdateVerificationAsync(
                                    manifest.StackId,
                                    verificationStatus,
                                    checkedAtUtc,
                                    doctorCt) ?? throw new InvalidOperationException(
                                        $"Runtime stack '{manifest.Slug}' disappeared while persisting Doctor verification evidence.");

                                await operationStore.CompleteAsync(
                                    operationId,
                                    status: verificationStatus,
                                    currentStep: "completed",
                                    result: new
                                    {
                                        ReportId = reportId,
                                        verification.AllPassed,
                                        CheckCount = verification.Checks.Count,
                                        FailedCheckCount = verification.Checks.Count(x => !x.Success)
                                    },
                                    evidence: verification.Checks,
                                    doctorCt);

                                return new RuntimeStackDoctorResponse(
                                    Source: "control-plane",
                                    Status: verificationStatus,
                                    StackId: verifiedManifest.StackId,
                                    Slug: verifiedManifest.Slug,
                                    LastVerifiedStatus: verifiedManifest.LastVerifiedStatus,
                                    LastVerifiedAtUtc: verifiedManifest.LastVerifiedAtUtc,
                                    CheckedAtUtc: checkedAtUtc,
                                    AllPassed: verification.AllPassed,
                                    Checks: verification.Checks
                                        .Select(RuntimeStackDoctorCheckResponse.FromCheck)
                                        .ToArray(),
                                    Detail: verification.AllPassed
                                        ? "Runtime stack doctor checks passed."
                                        : "One or more runtime stack doctor checks failed.",
                                    OperationId: operationId,
                                    ReportId: reportId);
                            },
                            ct);

                        return Results.Ok(response);
                    }
                    catch (RuntimeStackDoctorTimeoutException ex)
                    {
                        var timeoutSeconds = (int)Math.Round(ex.Timeout.TotalSeconds);
                        var detail =
                            $"Doctor did not complete within {timeoutSeconds} seconds. Check DNS, routing, NPM, and stack service availability, then run Doctor again.";

                        await TryFailDoctorOperationAsync(
                            operationStore,
                            operationId,
                            currentStep: "timed-out",
                            error: detail,
                            failureKind: "doctor-execution-timeout",
                            timeoutSeconds: timeoutSeconds);

                        return Results.Json(
                            new RuntimeStackDoctorResponse(
                                Source: "control-plane",
                                Status: "timeout",
                                StackId: manifest.StackId,
                                Slug: manifest.Slug,
                                LastVerifiedStatus: manifest.LastVerifiedStatus,
                                LastVerifiedAtUtc: manifest.LastVerifiedAtUtc,
                                CheckedAtUtc: DateTimeOffset.UtcNow,
                                AllPassed: false,
                                Checks: [],
                                Detail: detail,
                                OperationId: operationId,
                                ReportId: null),
                            statusCode: StatusCodes.Status504GatewayTimeout);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        await TryCancelDoctorOperationAsync(
                            serviceScopeFactory,
                            operationId);

                        throw;
                    }
                    catch (Exception ex)
                    {
                        await TryFailDoctorOperationAsync(
                            operationStore,
                            operationId,
                            currentStep: "failed",
                            error: ex.Message,
                            failureKind: "doctor-execution-failed",
                            timeoutSeconds: null);

                        throw;
                    }
                })
            .WithName("DoctorHostAgentRuntimeStack")
            .WithTags("HostAgent");

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/destroy",
                async (
                    string slugOrId,
                    [FromBody] RuntimeStackDestroyRequest? destroyRequest,
                    HttpContext httpContext) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";

                    // Resolve only authorization authority before the high-risk
                    // step-up guard. Mutation services are deliberately resolved
                    // only after the caller has passed the server-side policy.
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();

                    var stepUpDenied = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (stepUpDenied is not null)
                    {
                        return stepUpDenied;
                    }

                    // Once authorized, acceptance is short and server-owned. This
                    // prevents RequestAborted from stranding a RuntimeOperation
                    // between durable journal creation and background dispatch.
                    var applicationLifetime = httpContext.RequestServices
                        .GetRequiredService<IHostApplicationLifetime>();
                    var destroyService = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackDestroyService>();
                    var operationStore = httpContext.RequestServices
                        .GetRequiredService<RuntimeOperationStore>();
                    var dispatcher = httpContext.RequestServices
                        .GetRequiredService<RuntimeStackDestroyBackgroundDispatcher>();

                    Guid? startedOperationId = null;
                    var enqueued = false;

                    try
                    {
                        httpContext.RequestAborted.ThrowIfCancellationRequested();
                        await DestroyAcceptanceGate.WaitAsync(
                            httpContext.RequestAborted);

                        try
                        {
                            using var acceptanceTimeout =
                                new CancellationTokenSource(DestroyAcceptanceTimeout);
                            using var acceptanceSource =
                                CancellationTokenSource.CreateLinkedTokenSource(
                                    acceptanceTimeout.Token,
                                    applicationLifetime.ApplicationStopping);
                            var acceptanceToken = acceptanceSource.Token;

                            var acceptance = await destroyService.PrepareAsync(
                                slugOrId,
                                destroyRequest,
                                acceptanceToken);
                            var idempotencyKey = RuntimeStackDestroyService.ResolveIdempotencyKey(
                                acceptance.Request,
                                acceptance.RuntimeStackId);

                            var existing = await operationStore.FindByIdempotencyKeyAsync(
                                acceptance.RuntimeStackId,
                                "destroy-stack-runtime",
                                idempotencyKey,
                                acceptanceToken);

                            if (existing is not null)
                            {
                                return AcceptedDestroyOperation(
                                    existing.Id,
                                    acceptance,
                                    reusedExistingOperation: true);
                            }

                            var active = await operationStore
                                .FindActiveMutatingOperationForStackAsync(
                                    acceptance.RuntimeStackId,
                                    acceptanceToken);

                            if (active is not null)
                            {
                                if (string.Equals(
                                        active.Operation,
                                        "destroy-stack-runtime",
                                        StringComparison.Ordinal))
                                {
                                    return AcceptedDestroyOperation(
                                        active.Id,
                                        acceptance,
                                        reusedExistingOperation: true);
                                }

                                return Results.Json(
                                    new
                                    {
                                        error = "runtime_stack_operation_in_progress",
                                        detail = "Another mutating operation is already active for this stack.",
                                        operationId = active.Id,
                                        operation = active.Operation,
                                        currentStep = active.CurrentStep
                                    },
                                    statusCode: StatusCodes.Status409Conflict);
                            }

                            startedOperationId = await operationStore.StartAsync(
                                runtimeStackId: acceptance.RuntimeStackRowExists
                                    ? acceptance.RuntimeStackId
                                    : null,
                                operation: "destroy-stack-runtime",
                                idempotencyKey: idempotencyKey,
                                requestedBy: "host-agent",
                                hostMutationLevel: acceptance.HostMutationLevel,
                                input: RuntimeStackDestroyService.BuildOperationInput(acceptance),
                                ct: acceptanceToken);

                            await operationStore.UpdateStepAsync(
                                startedOperationId.Value,
                                "queued",
                                acceptanceToken);

                            if (!dispatcher.TryEnqueue(acceptance, startedOperationId.Value))
                            {
                                using var journalTimeout =
                                    new CancellationTokenSource(FailureJournalTimeout);
                                await operationStore.FailAsync(
                                    startedOperationId.Value,
                                    currentStep: "queued",
                                    error: "The destroy-stack background queue is full.",
                                    evidence: new
                                    {
                                        failureKind = "destroy-stack-background-queue-full",
                                        acceptance.RuntimeStackId,
                                        acceptance.Slug
                                    },
                                    ct: journalTimeout.Token);

                                return Results.Json(
                                    new
                                    {
                                        error = "destroy_stack_queue_full",
                                        detail = "MEM could not accept another stack-destroy operation at this time."
                                    },
                                    statusCode: StatusCodes.Status503ServiceUnavailable);
                            }

                            enqueued = true;
                            return AcceptedDestroyOperation(
                                startedOperationId.Value,
                                acceptance,
                                reusedExistingOperation: false);
                        }
                        finally
                        {
                            DestroyAcceptanceGate.Release();
                        }
                    }
                    catch (Exception ex) when (startedOperationId.HasValue && !enqueued)
                    {
                        await TryFailUnqueuedDestroyOperationAsync(
                            operationStore,
                            startedOperationId.Value,
                            ex);
                        throw;
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new
                        {
                            source = "control-plane",
                            status = "invalid_destroy_request",
                            slug = slugOrId,
                            detail = ex.Message
                        });
                    }
                })
            .WithName("DestroyHostAgentRuntimeStack")
            .WithTags("HostAgent");
    }

    private static async Task TryCancelDoctorOperationAsync(
        IServiceScopeFactory serviceScopeFactory,
        Guid operationId)
    {
        try
        {
            using var journalTimeout = new CancellationTokenSource(FailureJournalTimeout);

            // The Doctor task can still be unwinding against the cancelled request
            // scope. Use a fresh scope so terminal journaling owns an independent
            // DbContext and cannot race the dying request-scoped store instance.
            await using var journalScope = serviceScopeFactory.CreateAsyncScope();
            var operationStore = journalScope.ServiceProvider
                .GetRequiredService<RuntimeOperationStore>();

            await operationStore.CancelIfRunningAsync(
                operationId,
                currentStep: "request-aborted",
                result: new
                {
                    terminationKind = "doctor-request-aborted"
                },
                evidence: null,
                ct: journalTimeout.Token);
        }
        catch
        {
            // The request cancellation remains authoritative. Cancellation journaling
            // is deliberately bounded and must not turn an abandoned browser request
            // into a second operator-visible failure or Diagnostics incident.
        }
    }

    private static async Task TryFailDoctorOperationAsync(
        RuntimeOperationStore operationStore,
        Guid operationId,
        string currentStep,
        string error,
        string failureKind,
        int? timeoutSeconds)
    {
        try
        {
            using var journalTimeout = new CancellationTokenSource(FailureJournalTimeout);
            await operationStore.FailAsync(
                operationId,
                currentStep: currentStep,
                error: error,
                result: new
                {
                    failureKind,
                    timeoutSeconds
                },
                evidence: null,
                ct: journalTimeout.Token);
        }
        catch
        {
            // The original Doctor failure remains authoritative. Failure journaling
            // is deliberately bounded so a stuck persistence path cannot extend the
            // operator-visible Doctor execution indefinitely.
        }
    }

    private static IResult AcceptedDestroyOperation(
        Guid operationId,
        RuntimeStackDestroyAcceptance acceptance,
        bool reusedExistingOperation)
    {
        var pollUrl = $"/internal/host-agent/operations/{operationId:D}";
        return Results.Accepted(
            pollUrl,
            new RuntimeStackDestroyAcceptedResponse(
                OperationId: operationId,
                RuntimeStackId: acceptance.RuntimeStackId,
                Slug: acceptance.Slug,
                Status: "accepted",
                PollUrl: pollUrl,
                ReusedExistingOperation: reusedExistingOperation));
    }

    private static async Task TryFailUnqueuedDestroyOperationAsync(
        RuntimeOperationStore operationStore,
        Guid operationId,
        Exception exception)
    {
        try
        {
            using var journalTimeout =
                new CancellationTokenSource(FailureJournalTimeout);
            await operationStore.FailAsync(
                operationId,
                currentStep: "acceptance",
                error: exception.Message,
                evidence: new
                {
                    failureKind = "destroy-stack-acceptance-failed-before-dispatch",
                    exceptionType = exception.GetType().FullName
                },
                ct: journalTimeout.Token);
        }
        catch
        {
            // The original exception remains authoritative. The operation may be
            // reconciled from the durable journal during later support/recovery.
        }
    }

    private static AuthResult Authorize(HttpContext httpContext)
    {
        return HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(httpContext.User)
            ? new AuthResult(Authorized: true, Detail: null)
            : new AuthResult(
                Authorized: false,
                Detail: "A valid installer unlock session is required.");
    }
}

public sealed record AuthResult(
    bool Authorized,
    string? Detail);

public sealed record RuntimeStackIdentityUpdateRequest(
    string? DisplayName,
    string? Category);

public sealed record RuntimeStackListResponse(
    string Source,
    string Status,
    IReadOnlyList<RuntimeStackSummaryResponse> Stacks,
    string? Detail,
    RuntimeStackInventorySummary Summary,
    int TotalMatchingStacks,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    bool IsPaged,
    IReadOnlyList<RuntimeStackInventoryCategoryFacet> Categories);

public sealed record RuntimeStackSummaryResponse(
    Guid StackId,
    string Slug,
    string LastVerifiedStatus,
    DateTimeOffset LastVerifiedAtUtc,
    string? MatrixPublicBaseUrl,
    string? ElementPublicBaseUrl,
    string Health,
    string? DisplayName = null,
    string? Category = null,
    string? LogoUrl = null,
    string? VerificationFreshness = null)
{
    public static RuntimeStackSummaryResponse FromManifest(
        RuntimeStackManifest manifest) =>
        FromManifest(manifest, DateTimeOffset.UtcNow);

    public static RuntimeStackSummaryResponse FromManifest(
        RuntimeStackManifest manifest,
        DateTimeOffset observedAtUtc)
    {
        return new RuntimeStackSummaryResponse(
            StackId: manifest.StackId,
            Slug: manifest.Slug,
            LastVerifiedStatus: manifest.LastVerifiedStatus,
            LastVerifiedAtUtc: manifest.LastVerifiedAtUtc,
            MatrixPublicBaseUrl: manifest.Matrix.PublicBaseUrl,
            ElementPublicBaseUrl: manifest.Element?.PublicBaseUrl,
            Health: RuntimeStackInventoryQuery.ClassifyOperationalHealth(
                manifest.LastVerifiedStatus,
                manifest.LastVerifiedAtUtc,
                observedAtUtc),
            DisplayName: RuntimeStackIdentity.ResolveDisplayName(manifest),
            Category: RuntimeStackIdentity.ResolveCategory(manifest),
            LogoUrl: RuntimeStackIdentity.ResolveLogoUrl(manifest),
            VerificationFreshness: RuntimeStackVerificationFreshness.Classify(
                manifest.LastVerifiedAtUtc,
                observedAtUtc));
    }
}

public sealed record RuntimeStackInspectResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    RuntimeStackServiceInspectResponse? Matrix,
    RuntimeStackServiceInspectResponse? Element,
    DateTimeOffset? LastVerifiedAtUtc,
    string? Detail,
    string? DisplayName = null,
    string? Category = null,
    string? LogoUrl = null,
    string? Health = null,
    string? VerificationFreshness = null)
{
    public static RuntimeStackInspectResponse FromManifest(
        RuntimeStackManifest manifest) =>
        FromManifest(manifest, DateTimeOffset.UtcNow);

    public static RuntimeStackInspectResponse FromManifest(
        RuntimeStackManifest manifest,
        DateTimeOffset observedAtUtc)
    {
        return new RuntimeStackInspectResponse(
            Source: manifest.Source,
            Status: manifest.LastVerifiedStatus,
            StackId: manifest.StackId,
            Slug: manifest.Slug,
            Matrix: RuntimeStackServiceInspectResponse.FromManifest(manifest.Matrix),
            Element: manifest.Element is null
                ? null
                : RuntimeStackServiceInspectResponse.FromManifest(manifest.Element),
            LastVerifiedAtUtc: manifest.LastVerifiedAtUtc,
            Detail: "Runtime stack loaded from local control-plane manifest store.",
            DisplayName: RuntimeStackIdentity.ResolveDisplayName(manifest),
            Category: RuntimeStackIdentity.ResolveCategory(manifest),
            LogoUrl: RuntimeStackIdentity.ResolveLogoUrl(manifest),
            Health: RuntimeStackInventoryQuery.ClassifyOperationalHealth(
                manifest.LastVerifiedStatus,
                manifest.LastVerifiedAtUtc,
                observedAtUtc),
            VerificationFreshness: RuntimeStackVerificationFreshness.Classify(
                manifest.LastVerifiedAtUtc,
                observedAtUtc));
    }
}

public sealed record RuntimeStackServiceInspectResponse(
    string ServiceKey,
    Guid InstanceId,
    string? ContainerId,
    string? ContainerName,
    string? InternalHost,
    string? InternalBaseUrl,
    string? PublicHost,
    string? PublicBaseUrl,
    string? DataPath,
    string? ConfigPath,
    string? PublicRouteId,
    int? NpmCertificateId,
    IReadOnlyDictionary<string, string?> RuntimeMetadata)
{
    public static RuntimeStackServiceInspectResponse FromManifest(
        RuntimeStackServiceManifest service)
    {
        return new RuntimeStackServiceInspectResponse(
            ServiceKey: service.ServiceKey,
            InstanceId: service.InstanceId,
            ContainerId: service.ContainerId,
            ContainerName: service.ContainerName,
            InternalHost: service.InternalHost,
            InternalBaseUrl: service.InternalBaseUrl,
            PublicHost: service.PublicHost,
            PublicBaseUrl: service.PublicBaseUrl,
            DataPath: service.DataPath,
            ConfigPath: service.ConfigPath,
            PublicRouteId: service.PublicRouteId,
            NpmCertificateId: service.NpmCertificateId,
            RuntimeMetadata: service.RuntimeMetadata);
    }
}

public sealed record RuntimeStackLatestDoctorResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    RuntimeStackDoctorResponse? Report,
    string? Detail);

public sealed record RuntimeStackDoctorResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    string? LastVerifiedStatus,
    DateTimeOffset? LastVerifiedAtUtc,
    DateTimeOffset CheckedAtUtc,
    bool AllPassed,
    IReadOnlyList<RuntimeStackDoctorCheckResponse> Checks,
    string? Detail,
    Guid? OperationId,
    Guid? ReportId)
{
    public static RuntimeStackDoctorResponse FromPersisted(
        RuntimeStackManifest manifest,
        RuntimeReadinessReportSnapshot snapshot)
    {
        return new RuntimeStackDoctorResponse(
            Source: "control-plane",
            Status: snapshot.Status,
            StackId: manifest.StackId,
            Slug: manifest.Slug,
            LastVerifiedStatus: snapshot.Status,
            LastVerifiedAtUtc: snapshot.CreatedAtUtc,
            CheckedAtUtc: snapshot.CreatedAtUtc,
            AllPassed: snapshot.AllPassed,
            Checks: snapshot.Checks
                .Select(RuntimeStackDoctorCheckResponse.FromPersistedCheck)
                .ToArray(),
            Detail: snapshot.Summary ??
                (snapshot.AllPassed
                    ? "Runtime stack doctor checks passed."
                    : "One or more runtime stack doctor checks failed."),
            OperationId: snapshot.OperationId,
            ReportId: snapshot.ReportId);
    }
}

public sealed record RuntimeStackDoctorHistoryResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    IReadOnlyList<RuntimeStackDoctorHistoryItemResponse> Reports,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    string? Detail)
{
    public static RuntimeStackDoctorHistoryResponse Empty(
        string slug,
        string status,
        string? detail)
    {
        return new RuntimeStackDoctorHistoryResponse(
            Source: "control-plane",
            Status: status,
            StackId: null,
            Slug: slug,
            Reports: [],
            TotalCount: 0,
            Page: 1,
            PageSize: RuntimeReadinessReportStore.DefaultHistoryPageSize,
            TotalPages: 1,
            HasPreviousPage: false,
            HasNextPage: false,
            Detail: detail);
    }
}

public sealed record RuntimeStackDoctorHistoryItemResponse(
    Guid ReportId,
    Guid? OperationId,
    string Status,
    bool AllPassed,
    DateTimeOffset CheckedAtUtc,
    int CheckCount,
    int FailedCheckCount,
    int WarningCount,
    string? Detail)
{
    public static RuntimeStackDoctorHistoryItemResponse FromSnapshot(
        RuntimeReadinessReportSummarySnapshot snapshot)
    {
        return new RuntimeStackDoctorHistoryItemResponse(
            ReportId: snapshot.ReportId,
            OperationId: snapshot.OperationId,
            Status: snapshot.Status,
            AllPassed: snapshot.AllPassed,
            CheckedAtUtc: snapshot.CreatedAtUtc,
            CheckCount: snapshot.CheckCount,
            FailedCheckCount: snapshot.FailedCheckCount,
            WarningCount: snapshot.WarningCount,
            Detail: snapshot.Summary);
    }
}

public sealed record RuntimeStackDoctorCheckResponse(
    string Code,
    string Name,
    string Url,
    bool Success,
    int? StatusCode,
    string? Detail,
    string? BodyPreview)
{
    public static RuntimeStackDoctorCheckResponse FromCheck(
        RuntimeReadinessCheckResult check)
    {
        return new RuntimeStackDoctorCheckResponse(
            Code: check.Code,
            Name: check.Name,
            Url: check.Url,
            Success: check.Success,
            StatusCode: check.StatusCode,
            Detail: check.Detail,
            BodyPreview: check.BodyPreview);
    }

    public static RuntimeStackDoctorCheckResponse FromPersistedCheck(
        RuntimeReadinessReportCheckPayload check)
    {
        return new RuntimeStackDoctorCheckResponse(
            Code: check.Code,
            Name: check.Name,
            Url: check.Url,
            Success: check.Success,
            StatusCode: check.StatusCode,
            Detail: check.Detail,
            BodyPreview: check.BodyPreview);
    }
}

public sealed record RuntimeStackOperationsResponse(
    string Source,
    string Status,
    Guid? StackId,
    string Slug,
    IReadOnlyList<RuntimeStackOperationResponse> Operations,
    string? Detail);

public sealed record RuntimeStackOperationResponse(
    Guid Id,
    Guid? RuntimeStackId,
    string Operation,
    string Status,
    string? IdempotencyKey,
    string? RequestedBy,
    string? HostMutationLevel,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError)
{
    public static RuntimeStackOperationResponse FromSummary(
        RuntimeOperationSummary summary)
    {
        return new RuntimeStackOperationResponse(
            Id: summary.Id,
            RuntimeStackId: summary.RuntimeStackId,
            Operation: summary.Operation,
            Status: summary.Status,
            IdempotencyKey: summary.IdempotencyKey,
            RequestedBy: summary.RequestedBy,
            HostMutationLevel: summary.HostMutationLevel,
            CurrentStep: summary.CurrentStep,
            RequestedAtUtc: summary.RequestedAtUtc,
            StartedAtUtc: summary.StartedAtUtc,
            CompletedAtUtc: summary.CompletedAtUtc,
            LastError: summary.LastError);
    }
}