using System.Data.Common;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Cases;

namespace EarlyInterventionCare.Api.Controllers;

public sealed class QuestionnaireErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is QuestionnaireOperationException operation)
            context.Result = new ObjectResult(new { code = operation.Code, message = operation.Message }) { StatusCode = operation.Status };
        else if (context.Exception is DbException or DbUpdateException or TimeoutException)
            context.Result = new ObjectResult(new { code = "SERVICE_UNAVAILABLE", message = "問卷服務暫時無法使用。" }) { StatusCode = 503 };
        else return;
        context.ExceptionHandled = true;
    }
}

[ApiController]
[Authorize]
[QuestionnaireErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QuestionnairesController(QuestionnaireService service, ApplicationDbContext db,
    IWebHostEnvironment environment, IAuthorizationService? authorization = null,
    IMedicalTaskCaseReadScope? medicalTaskScope = null) : ControllerBase
{
    [HttpGet("api/questionnaires")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = await CheckUser(ct);
        return denied ?? Ok(await service.ListAsync(ct));
    }

    [HttpGet("api/cases/{caseId:guid}/tasks")]
    public async Task<IActionResult> Tasks(Guid caseId, CancellationToken ct)
    {
        var denied = await CheckTaskRead(caseId, ct);
        if (denied is not null) return denied;
        return Ok(await service.ListTasksAsync(caseId, ct));
    }

    [HttpGet("api/cases/{caseId:guid}/tasks/completion")]
    public async Task<IActionResult> Completion(Guid caseId, CancellationToken ct)
    {
        var denied = await CheckTaskRead(caseId, ct);
        if (denied is not null) return denied;
        return Ok(await service.CompletionAsync(caseId, ct));
    }

    [HttpPost("api/cases/{caseId:guid}/tasks")]
    public async Task<IActionResult> Assign(Guid caseId, AssignQuestionnaireRequest request, CancellationToken ct)
    {
        var denied = await CheckUser(ct);
        if (denied is not null) return denied;
        if (!CaseAllowed(caseId)) return StatusCode(403, new { code = "CASE_ACCESS_NOT_INTEGRATED", message = "目前僅支援 Development 虛構個案；正式個案權限尚未接入。" });
        var taskId = await service.AssignAsync(caseId, request, ct);
        return CreatedAtAction(nameof(Tasks), new { caseId }, new { taskId, caseId, taskStatus = "PENDING" });
    }

    private bool CaseAllowed(Guid caseId) => environment.IsDevelopment() && caseId == CoreDevelopmentSeed.CaseId;

    // GET-only additional restrictions. Legacy List/POST authorization remains unchanged.
    private async Task<IActionResult?> CheckTaskRead(Guid caseId, CancellationToken ct)
    {
        IActionResult Unauthenticated() => Unauthorized(new { code = "UNAUTHENTICATED", message = "請重新登入。" });
        var identities = User.Identities.Where(i => i.IsAuthenticated).ToArray();
        if (identities.Length != 1) return Unauthenticated();
        var ids = identities[0].FindAll("userId").ToArray();
        if (ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var id) || id == Guid.Empty)
            return Unauthenticated();
        var denied = await CheckUser(ct);
        if (denied is UnauthorizedResult) return Unauthenticated();
        if (denied is not null) return denied;
        if (!CaseAllowed(caseId)) return StatusCode(403, new { code = "CASE_ACCESS_NOT_INTEGRATED", message = "目前僅支援 Development 虛構個案；正式個案權限尚未接入。" });
        // Optional constructor dependencies preserve existing manual callers, never grant access.
        if (authorization is null || medicalTaskScope is null)
            return StatusCode(503, new { code = "SERVICE_UNAVAILABLE", message = "問卷服務暫時無法使用。" });
        if (!(await authorization.AuthorizeAsync(User, null, "Permission:questionnaire.task.read")).Succeeded)
            return StatusCode(403, new { code = "FORBIDDEN", message = "無法執行此操作。" });
        try
        {
            if (!await medicalTaskScope.CanReadCaseAsync(User, caseId, ct))
                return NotFound(new { code = "CASE_NOT_FOUND", message = "找不到個案。" });
        }
        catch (CaseAccessException error) when (error.Status == 401 && error.Code == "UNAUTHENTICATED")
        {
            return Unauthenticated();
        }
        return null;
    }

    private async Task<IActionResult?> CheckUser(CancellationToken ct)
    {
        var claims = User.FindAll("userId").ToArray();
        if (claims.Length != 1 || !Guid.TryParse(claims[0].Value, out var userId) || userId == Guid.Empty) return Unauthorized();
        var account = await (from user in db.Users.AsNoTracking()
            join role in db.Roles.AsNoTracking() on user.RoleId equals role.Id
            where user.Id == userId select new { user.Status, role.Name }).SingleOrDefaultAsync(ct);
        if (account is null || account.Status != "ACTIVE") return Unauthorized();
        return account.Name is "ADMIN" or "CASE_MANAGER" ? null : Forbid();
    }
}
