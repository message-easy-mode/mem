using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// HTTP-facing orchestration for incident lifecycle actions. The endpoint never
/// accepts an event watermark from the browser; MEM re-reads the incident and
/// captures the latest immutable event itself before persisting a disposition.
/// </summary>
public sealed class DiagnosticsIncidentActionService(
    DiagnosticsIncidentService incidents,
    DiagnosticsIncidentLifecycleService lifecycle)
{
    public async Task<DiagnosticsIncidentDetailResponse> AcknowledgeAsync(
        string incidentId,
        ClaimsPrincipal principal,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var watermark = await incidents.LoadWatermarkAsync(
            incidentId,
            cancellationToken);
        await ApplyAsync(
            () => lifecycle.AcknowledgeAsync(
                watermark,
                actorOperatorId,
                correlationId,
                cancellationToken));
        return await incidents.GetAsync(
            watermark.IncidentId,
            principal,
            cancellationToken);
    }

    public async Task<DiagnosticsIncidentDetailResponse> SnoozeAsync(
        string incidentId,
        DiagnosticsIncidentSnoozeRequest request,
        ClaimsPrincipal principal,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw InvalidAction(new ArgumentException("snooze request is required."));
        }

        var watermark = await incidents.LoadWatermarkAsync(
            incidentId,
            cancellationToken);
        await ApplyAsync(
            () => lifecycle.SnoozeAsync(
                watermark,
                request.SnoozedUntilUtc.ToUniversalTime(),
                actorOperatorId,
                correlationId,
                cancellationToken));
        return await incidents.GetAsync(
            watermark.IncidentId,
            principal,
            cancellationToken);
    }

    public async Task<DiagnosticsIncidentDetailResponse> ResolveAsync(
        string incidentId,
        DiagnosticsIncidentResolveRequest request,
        ClaimsPrincipal principal,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw InvalidAction(new ArgumentException("resolve request is required."));
        }

        var watermark = await incidents.LoadWatermarkAsync(
            incidentId,
            cancellationToken);
        await ApplyAsync(
            () => lifecycle.ResolveAsync(
                watermark,
                request.ResolutionCode,
                actorOperatorId,
                correlationId,
                cancellationToken));
        return await incidents.GetAsync(
            watermark.IncidentId,
            principal,
            cancellationToken);
    }

    public async Task<DiagnosticsIncidentDetailResponse> ReopenAsync(
        string incidentId,
        ClaimsPrincipal principal,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        // Validate that the incident still exists inside retained immutable
        // evidence before changing its durable overlay.
        var watermark = await incidents.LoadWatermarkAsync(
            incidentId,
            cancellationToken);
        await ApplyAsync(async () =>
        {
            _ = await lifecycle.ReopenAsync(
                watermark.IncidentId,
                actorOperatorId,
                correlationId,
                cancellationToken);
        });
        return await incidents.GetAsync(
            watermark.IncidentId,
            principal,
            cancellationToken);
    }

    private static async Task ApplyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new MemProblemException(
                StatusCodes.Status409Conflict,
                "diagnostics_incident_disposition_conflict",
                "The diagnostic incident changed",
                "The incident lifecycle changed concurrently. Refresh the incident and try the action again.",
                feature: "diagnostics",
                innerException: ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw InvalidAction(ex);
        }
        catch (ArgumentException ex)
        {
            throw InvalidAction(ex);
        }
    }

    private static MemProblemException InvalidAction(Exception ex) =>
        new(
            StatusCodes.Status400BadRequest,
            "diagnostics_incident_action_invalid",
            "The diagnostic incident action is invalid",
            ex is ArgumentOutOfRangeException
                ? "The requested snooze interval is outside the supported incident lifecycle boundary."
                : "The requested incident lifecycle action could not be accepted.",
            feature: "diagnostics",
            innerException: ex);
}
