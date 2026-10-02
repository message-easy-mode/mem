using System.Globalization;
using Microsoft.AspNetCore.Http;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsQueryParser(
    DiagnosticsApiOptions options,
    TimeProvider timeProvider)
{
    public MemDiagnosticQuery Parse(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var from = ParseDateTimeOffset(query, "sinceUtc");
        var until = ParseDateTimeOffset(query, "untilUtc");
        if (from.HasValue && until.HasValue && from.Value > until.Value)
        {
            throw Invalid("diagnostics_query_range_invalid", "sinceUtc cannot be later than untilUtc.");
        }

        var now = timeProvider.GetUtcNow();
        var effectiveUntil = until.HasValue && until.Value < now
            ? until.Value
            : now;
        if (from.HasValue && from.Value > effectiveUntil)
        {
            throw Invalid(
                "diagnostics_query_range_invalid",
                "sinceUtc cannot be later than the effective UTC query end.");
        }

        if (from.HasValue &&
            effectiveUntil - from.Value > TimeSpan.FromHours(options.MaximumQueryWindowHours))
        {
            throw Invalid(
                "diagnostics_query_range_too_large",
                $"Diagnostics queries cannot exceed {options.MaximumQueryWindowHours} hours.");
        }

        var pageSize = ParseInt(query, "pageSize");
        if (pageSize.HasValue &&
            (pageSize.Value < 1 || pageSize.Value > options.MaximumPageSize))
        {
            throw Invalid(
                "diagnostics_page_size_invalid",
                $"pageSize must be between 1 and {options.MaximumPageSize}.");
        }

        var operationId = ParseGuid(query, "operationId");
        var search = Optional(query, "search", options.MaximumSearchCharacters);
        var severity = Optional(query, "severity", 32);
        if (severity is not null && !MemDiagnosticSeverities.IsKnown(severity))
        {
            throw Invalid(
                "diagnostics_severity_invalid",
                "severity is not a supported MEM diagnostics severity.");
        }

        return new MemDiagnosticQuery(
            FromUtc: from,
            UntilUtc: until,
            Severity: severity,
            Source: Optional(query, "source", 100),
            Feature: Optional(query, "feature", 100),
            Stage: Optional(query, "stage", 120),
            EventCode: Optional(query, "eventCode", 200),
            IncidentId: Optional(query, "incidentId", 80),
            TraceId: Optional(query, "traceId", 80),
            OperationId: operationId,
            StackId: Optional(query, "stackId", 100),
            EventId: Optional(query, "eventId", 80),
            ResourceKind: Optional(query, "resourceKind", 80),
            Search: search,
            Cursor: Optional(query, "cursor", 4096),
            PageSize: pageSize);
    }

    public string ParseLifecycleFilter(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var value = query["lifecycle"].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DiagnosticsIncidentService.LifecycleAll;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is DiagnosticsIncidentService.LifecycleAll or
            DiagnosticsIncidentLifecycleStates.Open or
            DiagnosticsIncidentLifecycleStates.Acknowledged or
            DiagnosticsIncidentLifecycleStates.Snoozed or
            DiagnosticsIncidentLifecycleStates.Resolved)
        {
            return normalized;
        }

        throw Invalid(
            "diagnostics_incident_lifecycle_invalid",
            "lifecycle must be all, open, acknowledged, snoozed, or resolved.");
    }

    public string ParseRequiredIdentifier(
        string? value,
        string name,
        string requiredPrefix,
        int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(
                "diagnostics_identifier_invalid",
                $"{name} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length <= requiredPrefix.Length ||
            normalized.Length > maximumCharacters ||
            !normalized.StartsWith(requiredPrefix, StringComparison.Ordinal) ||
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character != '_' &&
                character != '-' &&
                character != '.'))
        {
            throw Invalid(
                "diagnostics_identifier_invalid",
                $"{name} is not a valid MEM diagnostics identifier.");
        }

        return normalized;
    }

    private static DateTimeOffset? ParseDateTimeOffset(
        IQueryCollection query,
        string name)
    {
        var value = query[name].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw Invalid(
                "diagnostics_time_invalid",
                $"{name} must be an ISO-8601 timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    private static int? ParseInt(IQueryCollection query, string name)
    {
        var value = query[name].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw Invalid(
                "diagnostics_number_invalid",
                $"{name} must be an integer.");
        }

        return parsed;
    }

    private static Guid? ParseGuid(IQueryCollection query, string name)
    {
        var value = query[name].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed))
        {
            throw Invalid(
                "diagnostics_guid_invalid",
                $"{name} must be a GUID.");
        }

        return parsed;
    }

    private static string? Optional(
        IQueryCollection query,
        string name,
        int maximumCharacters)
    {
        var value = query[name].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maximumCharacters ||
            trimmed.Any(character => char.IsControl(character)))
        {
            throw Invalid(
                "diagnostics_filter_invalid",
                $"{name} exceeds the allowed diagnostics boundary.");
        }

        return trimmed;
    }

    private static MemProblemException Invalid(string code, string detail) =>
        new(
            StatusCodes.Status400BadRequest,
            code,
            "The diagnostics query is invalid",
            detail,
            feature: "diagnostics");
}
