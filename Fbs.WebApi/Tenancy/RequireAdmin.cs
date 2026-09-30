using FastEndpoints;

namespace Fbs.WebApi.Tenancy;

/// <summary>
/// For what only an admin of the organisation can do. It has to come after <see cref="ResolveTenant"/>, which is
/// what finds out who is making the request.
/// </summary>
public sealed class RequireAdmin : IPreProcessor<object>
{
    public async Task PreProcessAsync(IPreProcessorContext<object> context, CancellationToken ct)
    {
        if (context.HttpContext.Response.HasStarted)
        {
            return;
        }

        if (!context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().IsAdmin)
        {
            await Problem.SendAsync(context.HttpContext.Response, StatusCodes.Status403Forbidden, "Admins only", "Only an admin can do this.", "admin-only", ct);
        }
    }
}
