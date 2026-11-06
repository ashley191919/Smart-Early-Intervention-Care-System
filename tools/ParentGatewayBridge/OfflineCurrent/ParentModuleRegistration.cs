using System.Data.Common;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace EarlyInterventionCare.Api.ParentModule;
public sealed class ParentModuleErrors : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/parent") &&
            context.Exception is Services.Questionnaires.QuestionnaireWorkflowException core)
            context.Result = ParentCoreHttpErrors.Result(core, context.HttpContext.TraceIdentifier);
        else if (context.Exception is CaseAccessException e) context.Result = Result(e.Code, e.Message, e.Status, context.HttpContext.TraceIdentifier);
        else if (context.Exception is DbException or DbUpdateException or TimeoutException)
            context.Result = Result("SERVICE_UNAVAILABLE", "資料服務暫時無法使用。", 503, context.HttpContext.TraceIdentifier);
        else return;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.ExceptionHandled = true;
    }
    internal static ObjectResult Result(string code, string message, int status, string traceId) => new(new { code, message, traceId }) { StatusCode = status };
}
