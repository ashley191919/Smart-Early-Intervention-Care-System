namespace EarlyInterventionCare.Api.Services.Authorization;

/// <summary>
/// Handles only unavailable permission lookups. Other modules retain their error handling.
/// Must wrap authentication/authorization in the request pipeline.
/// </summary>
public sealed class PermissionLookupExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (PermissionLookupException) when (
            !context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new
            {
                code = "AUTHORIZATION_UNAVAILABLE",
                message = "授權服務暫時無法使用。"
            }, context.RequestAborted);
        }
    }
}
