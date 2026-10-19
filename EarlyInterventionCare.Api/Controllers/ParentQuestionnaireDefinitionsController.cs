using System.Data.Common;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController, Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ParentQuestionnaireDefinitionErrors, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ParentQuestionnaireDefinitionsController(
    QuestionnaireDefinitionService definitions, IParentQuestionnaireDefinitionAccess access) : ControllerBase
{
    [HttpGet("api/parent/tasks/{taskId:guid}/questionnaire")]
    public async Task<IActionResult> Definition(Guid taskId, CancellationToken ct)
    {
        var result = await definitions.GetForParentAsync(User, taskId, ct);
        // Catch a revocation/account change that committed during the definition query.
        // This is a second read, not an atomic lock or the future Submit transaction protocol.
        if (!await access.CanReadParentTaskAsync(User, taskId, ct))
            throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Task unavailable.", 404);
        return Ok(result);
    }
}

public sealed class ParentQuestionnaireDefinitionErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var failure = context.Exception switch
        {
            QuestionnaireWorkflowException e => (e.Code, e.Status),
            CaseAccessException e => (e.Code, e.Status),
            DbException or DbUpdateException or TimeoutException => ("SERVICE_UNAVAILABLE", 503),
            _ => ((string Code, int Status)?)null
        };
        if (failure is not { } error) return;
        var message = error.Status switch
        {
            401 => "登入已失效，請重新登入。",
            403 => "您目前無法存取這份問卷。",
            404 => "找不到可存取的問卷任務。",
            _ => "問卷目前無法載入，請稍後再試。"
        };
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(new { code = error.Code, message, traceId = context.HttpContext.TraceIdentifier })
        { StatusCode = error.Status };
        context.ExceptionHandled = true;
    }
}
