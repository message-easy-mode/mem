using Docker.DotNet;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Api.Diagnostics;

public sealed class MemExceptionClassifier
{
    public MemExceptionClassification Classify(
        HttpContext context,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        var feature = ResolveFeature(context.Request.Path);

        return exception switch
        {
            MemProblemException problem => FromProblem(problem, feature),
            ValidationException validation => new MemExceptionClassification(
                StatusCodes.Status400BadRequest,
                MemProblemCodes.ValidationFailed,
                "Request validation failed",
                "One or more supplied values were invalid.",
                CreateIncident: false,
                Retryable: false,
                DiagnosticEventCode: "api.request.validation_failed",
                DiagnosticSeverity: MemDiagnosticSeverities.Warning,
                Feature: feature,
                ValidationErrors: ProjectValidationErrors(validation)),
            BadRequestException => new MemExceptionClassification(
                StatusCodes.Status400BadRequest,
                MemProblemCodes.BadRequest,
                "The request could not be accepted",
                "MEM could not accept the request in its current form.",
                CreateIncident: false,
                Retryable: false,
                DiagnosticEventCode: "api.request.bad_request",
                DiagnosticSeverity: MemDiagnosticSeverities.Warning,
                Feature: feature),
            NotFoundException => new MemExceptionClassification(
                StatusCodes.Status404NotFound,
                MemProblemCodes.NotFound,
                "The requested resource was not found",
                "MEM could not find the requested resource.",
                CreateIncident: false,
                Retryable: false,
                DiagnosticEventCode: "api.request.not_found",
                DiagnosticSeverity: MemDiagnosticSeverities.Information,
                Feature: feature),
            MemDiagnosticCursorException => new MemExceptionClassification(
                StatusCodes.Status400BadRequest,
                MemDiagnosticCodes.CursorInvalid,
                "The diagnostics cursor is invalid",
                "Refresh the diagnostics view and try the request again.",
                CreateIncident: false,
                Retryable: true,
                DiagnosticEventCode: "api.diagnostics.cursor_invalid",
                DiagnosticSeverity: MemDiagnosticSeverities.Warning,
                Feature: "diagnostics"),
            UnauthorizedAccessException => new MemExceptionClassification(
                StatusCodes.Status403Forbidden,
                MemProblemCodes.AccessDenied,
                "Access denied",
                "The current operator is not permitted to perform this action.",
                CreateIncident: false,
                Retryable: false,
                DiagnosticEventCode: "api.request.access_denied",
                DiagnosticSeverity: MemDiagnosticSeverities.Warning,
                Feature: feature),
            _ => ClassifyTechnical(exception, feature)
        };
    }

    private static MemExceptionClassification ClassifyTechnical(
        Exception exception,
        string feature)
    {
        if (ContainsException<TimeoutException>(exception) ||
            ContainsException<OperationCanceledException>(exception))
        {
            return Timeout(feature);
        }

        if (ContainsException<DockerApiException>(exception))
        {
            return Docker(feature);
        }

        if (ContainsException<SqliteException>(exception) ||
            ContainsException<DbUpdateException>(exception))
        {
            return Database(feature);
        }

        if (ContainsException<HttpRequestException>(exception))
        {
            return Dependency(feature);
        }

        if (ContainsException<InternalServerException>(exception))
        {
            return Internal(feature);
        }

        return Unexpected(feature);
    }

    private static MemExceptionClassification FromProblem(
        MemProblemException problem,
        string fallbackFeature)
    {
        var severity = problem.StatusCode >= StatusCodes.Status500InternalServerError ||
                       problem.CreateIncident
            ? MemDiagnosticSeverities.Error
            : MemDiagnosticSeverities.Warning;

        return new MemExceptionClassification(
            problem.StatusCode,
            problem.Code,
            problem.Title,
            problem.SafeDetail,
            problem.CreateIncident || problem.StatusCode >= StatusCodes.Status500InternalServerError,
            problem.Retryable,
            $"api.problem.{NormalizeCode(problem.Code)}",
            severity,
            string.IsNullOrWhiteSpace(problem.Feature)
                ? fallbackFeature
                : problem.Feature.Trim(),
            problem.Stage,
            problem.SuggestedAction,
            problem.OperationId,
            problem.Resource,
            problem.DiagnosticDetails);
    }

    private static MemExceptionClassification Timeout(string feature) =>
        new(
            StatusCodes.Status504GatewayTimeout,
            MemProblemCodes.RequestTimeout,
            "The operation timed out",
            "MEM did not complete the operation within the allowed time.",
            CreateIncident: true,
            Retryable: true,
            DiagnosticEventCode: "api.request.timeout",
            DiagnosticSeverity: MemDiagnosticSeverities.Error,
            Feature: feature,
            SuggestedAction: "Open Diagnostics to review the final recorded stage before retrying.");

    private static MemExceptionClassification Docker(string feature) =>
        new(
            StatusCodes.Status502BadGateway,
            MemProblemCodes.DockerOperationFailed,
            "Docker operation failed",
            "MEM could not complete the requested Docker operation.",
            CreateIncident: true,
            Retryable: true,
            DiagnosticEventCode: "api.docker.operation_failed",
            DiagnosticSeverity: MemDiagnosticSeverities.Error,
            Feature: feature,
            SuggestedAction: "Open Diagnostics and review the recorded Docker failure evidence.");

    private static MemExceptionClassification Dependency(string feature) =>
        new(
            StatusCodes.Status502BadGateway,
            MemProblemCodes.DependencyRequestFailed,
            "A dependent service did not respond",
            "MEM could not complete a request to a required dependent service.",
            CreateIncident: true,
            Retryable: true,
            DiagnosticEventCode: "api.dependency.request_failed",
            DiagnosticSeverity: MemDiagnosticSeverities.Error,
            Feature: feature,
            SuggestedAction: "Open Diagnostics and review the dependency failure before retrying.");

    private static MemExceptionClassification Database(string feature) =>
        new(
            StatusCodes.Status500InternalServerError,
            MemProblemCodes.DatabaseOperationFailed,
            "The control-plane data operation failed",
            "MEM could not complete a required control-plane data operation.",
            CreateIncident: true,
            Retryable: false,
            DiagnosticEventCode: "api.database.operation_failed",
            DiagnosticSeverity: MemDiagnosticSeverities.Error,
            Feature: feature,
            SuggestedAction: "Open Diagnostics and review control-plane storage health before retrying.");

    private static MemExceptionClassification Internal(string feature) =>
        new(
            StatusCodes.Status500InternalServerError,
            MemProblemCodes.InternalFailure,
            "MEM could not complete the operation",
            "An internal control-plane failure prevented the operation from completing.",
            CreateIncident: true,
            Retryable: false,
            DiagnosticEventCode: "api.request.internal_failure",
            DiagnosticSeverity: MemDiagnosticSeverities.Error,
            Feature: feature,
            SuggestedAction: "Open Diagnostics and copy the incident details for support.");

    private static MemExceptionClassification Unexpected(string feature) =>
        new(
            StatusCodes.Status500InternalServerError,
            MemProblemCodes.UnexpectedFailure,
            "MEM encountered an unexpected failure",
            "The control plane could not complete the request. Technical evidence was recorded for diagnosis.",
            CreateIncident: true,
            Retryable: false,
            DiagnosticEventCode: "api.request.unexpected_failure",
            DiagnosticSeverity: MemDiagnosticSeverities.Error,
            Feature: feature,
            SuggestedAction: "Open Diagnostics and copy the incident details for support.");

    private static IReadOnlyDictionary<string, string[]> ProjectValidationErrors(
        ValidationException validation)
    {
        return validation.Errors
            .GroupBy(
                failure => NormalizePropertyName(failure.PropertyName),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => string.IsNullOrWhiteSpace(failure.ErrorCode)
                        ? "invalid"
                        : NormalizeCode(failure.ErrorCode))
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePropertyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "$";
        }

        var normalized = new string(value
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            .Take(120)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? "$" : normalized;
    }

    private static string NormalizeCode(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            .Take(160)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? MemProblemCodes.UnexpectedFailure
            : normalized;
    }

    private static bool ContainsException<TException>(
        Exception exception,
        int depth = 0)
        where TException : Exception
    {
        if (exception is TException)
        {
            return true;
        }

        if (depth >= 8)
        {
            return false;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(inner =>
                ContainsException<TException>(inner, depth + 1));
        }

        return exception.InnerException is not null &&
               ContainsException<TException>(exception.InnerException, depth + 1);
    }

    private static string ResolveFeature(PathString path)
    {
        var value = path.Value?.ToLowerInvariant() ?? string.Empty;

        if (value.Contains("/migrations", StringComparison.Ordinal)) return "migrations";
        if (value.Contains("/restores", StringComparison.Ordinal)) return "restore";
        if (value.Contains("/backups", StringComparison.Ordinal)) return "backups";
        if (value.Contains("/federation", StringComparison.Ordinal)) return "federation";
        if (value.Contains("/turn", StringComparison.Ordinal) ||
            value.Contains("/coturn", StringComparison.Ordinal)) return "turn";
        if (value.Contains("/stacks", StringComparison.Ordinal)) return "stacks";
        if (value.Contains("/setup", StringComparison.Ordinal) ||
            value.Contains("/install", StringComparison.Ordinal)) return "setup";
        if (value.Contains("/auth", StringComparison.Ordinal) ||
            value.Contains("/security", StringComparison.Ordinal)) return "security";
        if (value.Contains("/diagnostics", StringComparison.Ordinal)) return "diagnostics";

        return "api";
    }
}
