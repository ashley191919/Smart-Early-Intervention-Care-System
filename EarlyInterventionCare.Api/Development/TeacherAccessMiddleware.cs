namespace EarlyInterventionCare.Api.Development;

public static class TeacherAccessMiddleware
{
    public static void UseDevelopmentTeacherAccess(this WebApplication app)
    {
        // Runs before model binding/controller activation, even for malformed JSON.
        app.Use(async (context, next) =>
        {
            var protectedPath = context.Request.Path.StartsWithSegments("/api/dev/teacher-grants")
                || context.Request.Path.StartsWithSegments("/api/dev/audit-logs")
                || context.Request.Path.StartsWithSegments("/teacher/test-form");
            if (protectedPath)
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["Pragma"] = "no-cache";
                if (!app.Environment.IsDevelopment())
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }
            await next();
        });
    }
}
