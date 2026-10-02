using System.Diagnostics;
using Api.Logging;
using Microsoft.AspNetCore.Mvc;

namespace Api.Diagnostics;

public sealed class MemProblemDetailsFactory
{
    public ProblemDetails Create(
        HttpContext context,
        MemExceptionClassification classification,
        string? incidentId,
        string? diagnosticWarningCode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(classification);

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var spanId = Activity.Current?.SpanId.ToString();
        var correlationId = MemRequestLogging.GetCorrelationId(context);

        var problem = new ProblemDetails
        {
            Type = $"https://mem.invalid/problems/{classification.Code}",
            Title = classification.Title,
            Detail = classification.Detail,
            Status = classification.StatusCode,
            Instance = context.Request.Path.Value
        };

        problem.Extensions["code"] = classification.Code;
        problem.Extensions["error"] = classification.Code;
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["requestId"] = context.TraceIdentifier;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["retryable"] = classification.Retryable;

        if (!string.IsNullOrWhiteSpace(spanId))
        {
            problem.Extensions["spanId"] = spanId;
        }

        if (!string.IsNullOrWhiteSpace(incidentId))
        {
            problem.Extensions["incidentId"] = incidentId;
        }

        if (classification.OperationId.HasValue)
        {
            problem.Extensions["operationId"] = classification.OperationId.Value;
        }

        if (!string.IsNullOrWhiteSpace(classification.SuggestedAction))
        {
            problem.Extensions["suggestedAction"] = classification.SuggestedAction;
        }

        if (classification.ValidationErrors is not null &&
            classification.ValidationErrors.Count > 0)
        {
            problem.Extensions["validationErrors"] = classification.ValidationErrors;
        }

        if (!string.IsNullOrWhiteSpace(diagnosticWarningCode))
        {
            problem.Extensions["diagnosticCapture"] = "degraded";
            problem.Extensions["diagnosticWarningCode"] = diagnosticWarningCode;
        }

        return problem;
    }
}
