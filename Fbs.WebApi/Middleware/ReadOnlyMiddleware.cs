namespace Fbs.WebApi.Middleware;

/// <summary>
/// With <c>Maintenance:ReadOnly=true</c>, everything can be read and nothing can be changed, so the data can
/// be moved without anything being made in the old place after it was copied. Changes are refused with a 503
/// that says to try again, which is also what makes Telegram deliver the updates it sends to the bot later,
/// rather than lose them.
/// </summary>
public class ReadOnlyMiddleware(IConfiguration configuration) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (
            configuration.GetValue<bool>("Maintenance:ReadOnly")
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            // The browser's check before a change, which changes nothing
            && !HttpMethods.IsOptions(context.Request.Method)
        )
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsJsonAsync(
                new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                    title = "The service is being moved",
                    status = StatusCodes.Status503ServiceUnavailable,
                    detail = "Nothing can be changed for a few minutes. Please try again shortly.",
                },
                options: null,
                contentType: "application/problem+json"
            );
            return;
        }

        await next(context);
    }
}
