using System.Data.Common;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EarlyInterventionCare.Api.ParentModule;

public static class ParentModuleRegistration
{
    private sealed class RegistrationMarker { }
    // Composition hook from 11's existing Definition registration; no Program/DbContext edits.
    public static IServiceCollection AddParentV53(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(RegistrationMarker))) return services;
        services.AddSingleton(new RegistrationMarker());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddScoped<CaseAccessService>();
        services.TryAddScoped<CaseGuardianService>();
        services.TryAddScoped<Services.Authentication.ParentEmailAuthenticationService>();
        services.TryAddSingleton<Services.Authentication.IParentSmsVerification, Services.Authentication.ParentSmsUnavailable>();
        services.TryAddScoped<Services.Authentication.ParentOtpAuthenticationService>();
        services.AddOptions<Controllers.ParentEmailLoginOptions>().BindConfiguration("ParentEmailLogin");
        services.AddOptions<Services.Email.ParentEmailOptions>().BindConfiguration("Email");
        services.TryAddTransient<Services.Email.IParentEmailTransport, Services.Email.MailKitParentEmailTransport>();
        services.TryAddTransient<Services.Email.IParentEmailSender, Services.Email.ParentEmailSender>();
        services.TryAddSingleton<Services.Email.IParentEmailVerification, Services.Email.ParentEmailVerification>();
        services.AddOptions<ParentModuleOptions>().BindConfiguration("ParentModule");
        services.TryAddScoped<IParentActorProvider, ParentActorProvider>();
        services.TryAddScoped<ParentConsentService>();
        services.TryAddScoped<IParentQuestionnaireAccess, ParentQuestionnaireAccess>();
        services.TryAddScoped<IParentQuestionnaireGateway, UnavailableParentQuestionnaireGateway>();
        services.TryAddScoped<ParentConsentFilter>();
        services.Configure<MvcOptions>(o => { o.Filters.Add<ParentConsentFilter>(); o.Filters.Add<ParentModuleErrors>(); });
        return services;
    }
}

// Protect older parent-readable Case/Profile routes too; hiding the new UI is not authorization.
public sealed class ParentConsentFilter(ApplicationDbContext db, CaseAccessService access,
    IParentActorProvider actors, ParentConsentService consents) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/api/parent")) context.HttpContext.Response.Headers.CacheControl = "no-store";
        var relevant = path.StartsWithSegments("/api/parent") || path.StartsWithSegments("/api/cases") || path.StartsWithSegments("/api/guardians");
        if (!relevant || path.StartsWithSegments("/api/parent/auth") || path.StartsWithSegments("/api/parent/consent") ||
            !context.HttpContext.User.Identities.Any(x => x.IsAuthenticated)) { await next(); return; }
        try
        {
            var actor = await access.ActorAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
            if (actor.Role == "PARENT")
            {
                if (context.RouteData.Values.ContainsKey("taskId") && !actor.EmailVerified)
                    throw new CaseAccessException("TASK_NOT_FOUND", 404, "找不到問卷，或目前已無法存取。");
                actor = await actors.ResolveAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
                Guid? caseId = null;
                if (context.RouteData.Values.TryGetValue("caseId", out var value) && Guid.TryParse(value?.ToString(), out var id)) caseId = id;
                else if (context.RouteData.Values.TryGetValue("taskId", out var task) && Guid.TryParse(task?.ToString(), out var taskId))
                {
                    caseId = await db.QuestionnaireTasks.AsNoTracking().Where(t => t.TaskId == taskId && t.RespondentRole == "PARENT" &&
                        t.TaskStatus != "CANCELLED" && access.ReadableCases(actor).Any(c => c.CaseId == t.CaseId)).Select(t => (Guid?)t.CaseId).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                    if (caseId == null) throw new CaseAccessException("TASK_NOT_FOUND", 404, "找不到問卷，或目前已無法存取。");
                }
                if (caseId != null) await consents.RequireAsync(actor, caseId, context.HttpContext.RequestAborted);
                else await consents.RequireAllAsync(actor, context.HttpContext.RequestAborted);
            }
        }
        catch (CaseAccessException e) { context.Result = ParentModuleErrors.Result(e.Code, e.Message, e.Status, context.HttpContext.TraceIdentifier); return; }
        catch (Exception e) when (e is DbException or DbUpdateException or TimeoutException)
        { context.Result = ParentModuleErrors.Result("SERVICE_UNAVAILABLE", "資料服務暫時無法使用。", 503, context.HttpContext.TraceIdentifier); return; }
        await next();
    }
}
public sealed class ParentModuleErrors : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is CaseAccessException e) context.Result = Result(e.Code, e.Message, e.Status, context.HttpContext.TraceIdentifier);
        else if (context.Exception is DbException or DbUpdateException or TimeoutException)
            context.Result = Result("SERVICE_UNAVAILABLE", "資料服務暫時無法使用。", 503, context.HttpContext.TraceIdentifier);
        else return;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.ExceptionHandled = true;
    }
    internal static ObjectResult Result(string code, string message, int status, string traceId) => new(new { code, message, traceId }) { StatusCode = status };
}
