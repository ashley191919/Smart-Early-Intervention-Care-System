using System.Text.Json;

namespace EarlyInterventionCare.Api.Middleware;

/// <summary>
/// Unregistered candidate outer exception boundary. Existing filters and specific
/// middleware must handle their known errors downstream; this does not classify 503s.
/// Does not catch canceled requests or rewrite an already-started response.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const string ErrorCode = "INTERNAL_ERROR";
    public const string SafeMessage = "系統發生錯誤，請稍後再試。";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            // Use only the server request ID, never a header or exception content.
            var traceId = context.TraceIdentifier;
            if (string.IsNullOrWhiteSpace(traceId))
            {
                traceId = Guid.NewGuid().ToString("N");
                context.TraceIdentifier = traceId;
            }
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.Headers.CacheControl = "no-store";
            // Failures/cancellation while writing propagate; never retry a second response.
            await context.Response.WriteAsJsonAsync(new { code = ErrorCode, message = SafeMessage, traceId },
                options: JsonOptions, contentType: "application/json; charset=utf-8", cancellationToken: context.RequestAborted);
        }
    }
}
