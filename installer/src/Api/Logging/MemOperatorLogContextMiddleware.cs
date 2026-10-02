using System.Security.Claims;
using Serilog.Context;

namespace Api.Logging;

public sealed class MemOperatorLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var operatorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        using (LogContext.PushProperty("OperatorUserId", operatorUserId))
        {
            await next(context);
        }
    }
}
