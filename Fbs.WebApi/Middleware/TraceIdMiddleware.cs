using System.Diagnostics;

namespace Fbs.WebApi.Middleware;

public class TraceIdMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (Activity.Current is not null)
        {
            context.Response.Headers.Append("X-Fbs-Trace-Id", Activity.Current.TraceId.ToString());
        }

        await next(context);
    }
}