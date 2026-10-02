using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Shared.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const int SlowMs = 1500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        _ = request;
        _ = ct;

        var name = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await next();
            sw.Stop();

            var elapsedMs = sw.ElapsedMilliseconds;
            if (elapsedMs >= SlowMs)
            {
                logger.LogWarning(
                    "Handled {Request} in {ElapsedMs}ms (SLOW)",
                    name,
                    elapsedMs);
            }
            else
            {
                logger.LogDebug(
                    "Handled {Request} in {ElapsedMs}ms",
                    name,
                    elapsedMs);
            }

            return response;
        }
        catch (Exception exception)
        {
            sw.Stop();

            // The global HTTP exception handler owns the single full technical
            // exception log for request failures. Keep pipeline timing context
            // without duplicating the exception and stack trace at Error level.
            logger.LogWarning(
                "Request pipeline observed {ExceptionType} for {Request} after {ElapsedMs}ms; the request boundary will record technical evidence",
                exception.GetType().FullName,
                name,
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
