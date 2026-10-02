using System.Security.Claims;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsEventService(
    IMemDiagnosticEventReader reader,
    DiagnosticsEventCollector collector,
    DiagnosticsCapabilityService capabilities,
    DiagnosticsQueryParser queryParser)
{
    public async Task<DiagnosticsEventPageResponse> QueryAsync(
        MemDiagnosticQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(principal);

        var page = await QuerySafeAsync(query, cancellationToken);
        var includeOwnerFacts = capabilities
            .GetCapabilities(principal)
            .CanViewOwnerHealthFacts;

        return new DiagnosticsEventPageResponse(
            page.FromUtc,
            page.UntilUtc,
            page.PageSize,
            page.WindowClamped,
            page.Events
                .Select(@event => DiagnosticsProjection.Event(@event, includeOwnerFacts))
                .ToArray(),
            page.NextCursor,
            page.Warnings);
    }

    public async Task<DiagnosticsEventProjection> GetAsync(
        string eventId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var normalized = queryParser.ParseRequiredIdentifier(
            eventId,
            "eventId",
            "evt_",
            80);
        var result = await collector.CollectEventAsync(normalized, cancellationToken);
        var @event = result.Events.FirstOrDefault(candidate =>
            string.Equals(candidate.EventId, normalized, StringComparison.Ordinal));

        if (@event is null)
        {
            throw new MemProblemException(
                StatusCodes.Status404NotFound,
                "diagnostics_event_not_found",
                "Diagnostic event not found",
                "The diagnostic event has expired or does not exist.",
                feature: "diagnostics");
        }

        var includeOwnerFacts = capabilities
            .GetCapabilities(principal)
            .CanViewOwnerHealthFacts;
        return DiagnosticsProjection.Event(@event, includeOwnerFacts);
    }

    private async Task<MemDiagnosticEventPage> QuerySafeAsync(
        MemDiagnosticQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.QueryAsync(query, cancellationToken);
        }
        catch (MemDiagnosticCursorException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "diagnostics_query_invalid",
                "The diagnostics query is invalid",
                "The supplied diagnostics filter could not be accepted.",
                feature: "diagnostics",
                innerException: ex);
        }
    }
}
