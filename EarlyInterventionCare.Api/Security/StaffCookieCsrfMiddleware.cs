using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;

namespace EarlyInterventionCare.Api.Security;

/// <summary>
/// Protects every unsafe request authenticated through the staff cookie. Explicit bearer clients
/// remain header-authenticated and are not required to send browser anti-forgery material.
/// </summary>
public sealed class StaffCookieCsrfMiddleware
{
    private readonly RequestDelegate _next;

    public StaffCookieCsrfMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery, StaffSessionOptions options)
    {
        if (!RequiresProtection(context, options))
        {
            await _next(context);
            return;
        }

        var origins = context.Request.Headers.Origin;
        if (origins.Count != 1 || string.IsNullOrWhiteSpace(origins[0]))
        {
            await RejectAsync(context, "ORIGIN_REQUIRED", "Cookie 寫入請求必須提供可信 Origin。");
            return;
        }

        if (!options.IsTrustedOrigin(origins[0]))
        {
            await RejectAsync(context, "INVALID_ORIGIN", "Cookie 寫入請求來源不受信任。");
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await RejectAsync(context, "CSRF_VALIDATION_FAILED", "CSRF Token 缺失或無效。");
            return;
        }

        await _next(context);
    }

    private static bool RequiresProtection(HttpContext context, StaffSessionOptions options) =>
        options.IsEnabled &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method) &&
        context.Request.Cookies.ContainsKey(StaffSessionCookieDefaults.Name) &&
        !StaffAuthenticationSource.IsValidatedBearerHeader(context);

    private static async Task RejectAsync(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { code, message, traceId = context.TraceIdentifier });
    }
}
