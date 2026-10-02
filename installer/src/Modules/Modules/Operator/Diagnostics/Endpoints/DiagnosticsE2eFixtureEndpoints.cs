using System.Diagnostics;
using Carter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Auth.Identity;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Endpoints;

/// <summary>
/// Development-only deterministic fixture used by the disposable browser proof.
/// The route is absent unless both the Development environment and the explicit
/// Diagnostics:TestFixture:Enabled switch are active.
/// </summary>
public sealed class DiagnosticsE2eFixtureEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var options = app.ServiceProvider.GetService<DiagnosticsE2eFixtureOptions>();

        if (!environment.IsDevelopment() || options?.Enabled != true)
        {
            return;
        }

        app.MapPost("/api/operator/diagnostics/test-fixture/incident", async (
            HttpContext context,
            IMemDiagnosticEventWriter writer,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);

            var activity = Activity.Current;
            var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
            var result = await writer.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Error,
                    EventCode: "diagnostics.release_proof.failure",
                    Source: "DiagnosticsE2eFixture",
                    Feature: "diagnostics",
                    Message: "A deterministic diagnostics release-proof incident was created.",
                    Stage: "release-proof",
                    CreateIncident: true,
                    Resource: new MemDiagnosticResource(
                        Kind: "diagnostics",
                        Id: "release-proof",
                        DisplayName: "Diagnostics release proof",
                        WorkspacePath: "/diagnostics/logs"),
                    Expected: new Dictionary<string, string?>
                    {
                        ["result"] = "successful release-proof diagnostic handoff"
                    },
                    Observed: new Dictionary<string, string?>
                    {
                        ["result"] = "deterministic test incident"
                    },
                    Details: new Dictionary<string, string?>
                    {
                        ["fixture"] = "development-only"
                    },
                    SuggestedAction: "Open Diagnostics and verify the incident, support report, and logging health.",
                    Retryable: false,
                    Context: new MemDiagnosticContext(
                        TraceId: activity?.TraceId.ToString(),
                        SpanId: activity?.SpanId.ToString(),
                        RequestId: context.TraceIdentifier,
                        CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? null : correlationId)),
                cancellationToken);

            if (!result.Stored || string.IsNullOrWhiteSpace(result.IncidentId))
            {
                throw new MemProblemException(
                    StatusCodes.Status503ServiceUnavailable,
                    "diagnostics_test_fixture_store_unavailable",
                    "The diagnostics test incident could not be stored",
                    "The safe diagnostic event store did not accept the release-proof incident.",
                    retryable: true,
                    feature: "diagnostics",
                    stage: "release-proof");
            }

            return Results.Json(
                new DiagnosticsE2eFixtureIncidentResponse(
                    result.EventId,
                    result.IncidentId,
                    activity?.TraceId.ToString(),
                    string.IsNullOrWhiteSpace(correlationId) ? null : correlationId),
                statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateDiagnosticsE2eFixtureIncident")
        .WithTags("Operator Diagnostics Test Fixture")
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform)
        .Produces<DiagnosticsE2eFixtureIncidentResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
