using System.Data.Common;
using System.Text.Json;
using EarlyInterventionCare.Api.Services.Authentication;

namespace EarlyInterventionCare.Api.Development;

/// <summary>Read-only synthetic medical demo. Never queries cases, tasks or responses.</summary>
public static class MedicalDemoEndpoints
{
    public const string Prefix = "/api/medical-demo";
    public const string CaseId = "DEMO-001";
    public const string TaskId = "medical-demo-snap-teacher-001-v1";

    public static void UseMedicalDemoBoundary(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var legacyAsset = context.Request.Path.Equals(new PathString("/js/medical-demo-fixtures.js"));
            var api = context.Request.Path.StartsWithSegments(Prefix);
            if (legacyAsset || api)
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                // Environment is evaluated by ASP.NET, before auth or static files can respond.
                if (legacyAsset || !app.Environment.IsDevelopment())
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }
            await next();
        });
    }

    public static void MapMedicalDemoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Prefix).RequireAuthorization();
        group.MapGet("/availability", async (HttpContext context, IAuthenticationService auth, CancellationToken ct) =>
        {
            var denied = await CheckStaff(context, auth, ct);
            return denied ?? Results.Ok(new { enabled = true, label = "Demo 模擬資料" });
        });
        group.MapGet("/cases/{caseId}/tasks", async (string caseId, HttpContext context, IAuthenticationService auth, CancellationToken ct) =>
        {
            var denied = await CheckStaff(context, auth, ct);
            if (denied is not null) return denied;
            if (caseId == "DEMO-024") return Results.Ok(Array.Empty<object>());
            if (caseId != CaseId) return Results.NotFound();
            // Lists deliberately exclude answers; only the authenticated detail route supplies them.
            return Results.Ok(new[] { new { id = TaskId, caseId = CaseId, name = "SNAP-IV 評量表",
                role = "teacher", status = "done", version = "medical-demo-1", isDemo = true } });
        });
        group.MapGet("/cases/{caseId}/tasks/{taskId}", async (string caseId, string taskId, HttpContext context,
            IAuthenticationService auth, CancellationToken ct) =>
        {
            var denied = await CheckStaff(context, auth, ct);
            if (denied is not null) return denied;
            if (caseId != CaseId || taskId != TaskId) return Results.NotFound();
            // Embedded outside wwwroot. No filesystem path or patient identifier from the request is used.
            using var stream = typeof(MedicalDemoEndpoints).Assembly.GetManifestResourceStream(
                "EarlyInterventionCare.Api.Development.MedicalDemoFixture.json")!;
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return Results.Json(document.RootElement.Clone());
        });
    }

    private static async Task<IResult?> CheckStaff(HttpContext context, IAuthenticationService auth, CancellationToken ct)
    {
        if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
        var ids = context.User.FindAll("userId").ToArray();
        if (ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var id) || id == Guid.Empty)
            return Results.Unauthorized();
        try
        {
            // Same current-account lookup used by staff /me, not a role supplied by the client.
            var current = await auth.GetCurrentUserAsync(id, ct);
            if (current is null || current.UserId != id) return Results.Unauthorized();
            return StaffSessionAuthorization.CanCreateStaffSession(current.Role) ? null : Results.StatusCode(403);
        }
        catch (Exception ex) when (ex is DbException or TimeoutException)
        {
            return Results.Json(new { code = "MEDICAL_DEMO_UNAVAILABLE" }, statusCode: 503);
        }
    }
}
