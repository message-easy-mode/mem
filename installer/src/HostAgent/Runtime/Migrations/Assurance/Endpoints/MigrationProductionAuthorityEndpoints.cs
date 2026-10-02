using System.Security.Claims;
using Carter;
using HostAgent.Commands;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.Assurance.Endpoints;

public sealed class MigrationProductionAuthorityEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/migrations/sessions/{migrationId}/production-authority")
            .WithTags("Operator / Migrations / Production authority")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", StateAsync);
        group.MapPost("/operator-attested-snapshot", CreateOperatorAttestedSnapshotAsync);
    }

    private static async Task<IResult> StateAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationProductionAuthorityService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.GetStateAsync(migrationId, ct));
    }

    private static async Task<IResult> CreateOperatorAttestedSnapshotAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] CreateOperatorAttestedSnapshotAuthorityRequest? request,
        MigrationProductionAuthorityService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);

        var stepUp = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "operator_attestation_body_required",
                "Provide every required simplified-assurance acknowledgement."));
        }

        var operatorIdText = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(operatorIdText, out var operatorId))
        {
            return Results.Json(
                new HostAgentErrorResponse(
                    "named_operator_required",
                    "A named Platform Owner session is required to create production authority."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await ExecuteAsync(() => service.CreateOperatorAttestedSnapshotAsync(
            migrationId,
            operatorId,
            request,
            ct));
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse(
                "migration_production_authority_source_not_found",
                ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse(
                    "migration_production_authority_evidence_invalid",
                    ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (MigrationProductionAuthorityException ex)
        {
            var statusCode = ex.Kind == MigrationProductionAuthorityFailureKind.InvalidRequest
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status409Conflict;
            return Results.Json(
                new HostAgentErrorResponse(ex.Code, ex.Message),
                statusCode: statusCode);
        }
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
