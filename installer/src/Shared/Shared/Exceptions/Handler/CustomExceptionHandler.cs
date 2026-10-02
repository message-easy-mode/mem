using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shared.Exceptions;

namespace Shared.Exceptions.Handler;

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (detail, title, statusCode) = exception switch
        {
            InternalServerException =>
            (
                exception.Message,
                exception.GetType().Name,
                StatusCodes.Status500InternalServerError
            ),
            ValidationException =>
            (
                exception.Message,
                exception.GetType().Name,
                StatusCodes.Status400BadRequest
            ),
            BadRequestException =>
            (
                exception.Message,
                exception.GetType().Name,
                StatusCodes.Status400BadRequest
            ),
            NotFoundException =>
            (
                exception.Message,
                exception.GetType().Name,
                StatusCodes.Status404NotFound
            ),
            _ =>
            (
                exception.Message,
                exception.GetType().Name,
                StatusCodes.Status500InternalServerError
            )
        };

        context.Response.StatusCode = statusCode;

        // ✅ Log level by status:
        // - 404 -> Information
        // - 400/422 etc -> Warning
        // - 500 -> Error
        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["TraceId"] = context.TraceIdentifier,
                   ["Path"] = context.Request.Path.Value
               }))
        {
            if (statusCode >= 500)
            {
                logger.LogError(exception, "Unhandled exception ({StatusCode}): {Title}", statusCode, title);
            }
            else if (statusCode == StatusCodes.Status404NotFound)
            {
                logger.LogInformation(exception, "Not found ({StatusCode}): {Title}", statusCode, title);
            }
            else
            {
                logger.LogWarning(exception, "Request rejected ({StatusCode}): {Title}", statusCode, title);
            }
        }

        var problemDetails = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = statusCode,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["ValidationErrors"] = validationException.Errors;
        }

        await context.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellationToken);
        return true;
    }
}
