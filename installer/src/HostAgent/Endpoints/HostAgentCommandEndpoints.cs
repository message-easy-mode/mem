using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Coturn;
using HostAgent.Security;
using HostAgent.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Modules.Shared.RuntimeImages;

namespace HostAgent.Endpoints;

public sealed class HostAgentCommandEndpoints : ICarterModule
{
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent");

        group.MapGet("/runtime-images/policy",
            (
                HttpContext httpContext,
                [FromServices] IApprovedOperationalRuntimeImageProvider runtimeImages) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                try
                {
                    return Results.Ok(runtimeImages.GetPolicy());
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(
                        new HostAgentErrorResponse(
                            Error: "runtime_image_policy_invalid",
                            Detail: ex.Message),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

        group.MapPost("/commands/create-chat-stack-runtime",
            async (
                HttpContext httpContext,
                CreateChatStackRuntimeCommand command,
                [FromServices] RuntimeOperationStore operationStore,
                [FromServices] CreateChatStackRuntimeBackgroundDispatcher dispatcher) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                try
                {
                    CreateChatStackRuntimeHandler.Validate(command);
                    httpContext.RequestAborted.ThrowIfCancellationRequested();

                    var operationId = await operationStore.StartAsync(
                        runtimeStackId: null,
                        operation: "create-stack-runtime",
                        idempotencyKey: command.IdempotencyKey,
                        requestedBy: "host-agent",
                        hostMutationLevel: "filesystem,docker,ingress,postgres",
                        input: new
                        {
                            command.StackId,
                            command.MatrixInstanceId,
                            command.ElementInstanceId,
                            command.StackSlug,
                            command.RequestedDomainId,
                            command.MatrixImage,
                            command.MatrixVersion,
                            command.ElementImage,
                            command.ElementVersion,
                            command.IdempotencyKey,
                            command.DisplayName,
                            command.Category
                        },
                        httpContext.RequestAborted);

                    await operationStore.UpdateStepAsync(
                        operationId,
                        "queued",
                        httpContext.RequestAborted);

                    if (!dispatcher.TryEnqueue(command, operationId))
                    {
                        using var journalTimeout =
                            new CancellationTokenSource(FailureJournalTimeout);
                        await operationStore.FailAsync(
                            operationId,
                            currentStep: "queued",
                            error: "The create-stack background queue is full.",
                            evidence: new
                            {
                                failureKind = "create-stack-background-queue-full"
                            },
                            journalTimeout.Token);

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "create_stack_queue_full",
                                Detail: "MEM could not accept another stack-creation operation at this time."),
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    var pollUrl =
                        $"/internal/host-agent/operations/{operationId:D}";

                    return Results.Accepted(
                        pollUrl,
                        new CreateChatStackRuntimeAcceptedResponse(
                            OperationId: operationId,
                            StackId: command.StackId,
                            StackSlug: command.StackSlug!.Trim(),
                            Status: "accepted",
                            PollUrl: pollUrl));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "invalid_host_agent_command",
                        Detail: ex.Message));
                }
            });

        group.MapGet("/operations/{operationId:guid}",
            async (
                Guid operationId,
                HttpContext httpContext,
                [FromServices] RuntimeOperationStore operationStore,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                var operation = await operationStore.FindByIdAsync(operationId, ct);

                var supportedOperation = operation?.Operation is
                    "create-stack-runtime" or
                    "destroy-stack-runtime" or
                    CoturnPlatformInstallOperationService.OperationName or
                    CoturnPlatformMaintenanceOperationService.OperationName;

                if (operation is null || !supportedOperation)
                {
                    return Results.NotFound(new HostAgentErrorResponse(
                        Error: "runtime_operation_not_found",
                        Detail: "The requested runtime operation was not found."));
                }

                var terminal = operation.CompletedAtUtc.HasValue;
                var succeeded = terminal &&
                    string.Equals(operation.Status, "succeeded", StringComparison.OrdinalIgnoreCase);

                return Results.Ok(new CreateChatStackRuntimeOperationResponse(
                    OperationId: operation.Id,
                    RuntimeStackId: operation.RuntimeStackId,
                    Status: operation.Status,
                    CurrentStep: operation.CurrentStep,
                    RequestedAtUtc: operation.RequestedAtUtc,
                    StartedAtUtc: operation.StartedAtUtc,
                    CompletedAtUtc: operation.CompletedAtUtc,
                    LastError: operation.LastError,
                    Terminal: terminal,
                    Succeeded: succeeded));
            });
    }
}
