using System.Data.Common;
using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace EarlyInterventionCare.Api.Controllers;
public sealed class CaseErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is CaseAccessException e) context.Result = new ObjectResult(new { code = e.Code, message = e.Message }) { StatusCode = e.Status };
        else if (context.Exception is DbUpdateConcurrencyException) context.Result = new ObjectResult(new { code = "REVISION_CONFLICT", message = "資料已更新，請重新載入再修改。" }) { StatusCode = 409 };
        else if (context.Exception is MySqlConnector.MySqlException { Number: 1062 }
            or DbUpdateException { InnerException: MySqlConnector.MySqlException { Number: 1062 } })
            context.Result = new ObjectResult(new { code = "DATA_CONFLICT", message = "資料已存在或已被其他操作更新，請重新載入。" }) { StatusCode = 409 };
        else if (context.Exception is DbException or DbUpdateException or TimeoutException) context.Result = new ObjectResult(new { code = "SERVICE_UNAVAILABLE", message = "個案服務暫時無法使用。" }) { StatusCode = 503 };
        else return;
        context.ExceptionHandled = true;
    }
}
[ApiController, Authorize, CaseErrors, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CasesController(CaseAccessService access, CaseGuardianService service, EarlyInterventionCare.Api.Services.Questionnaires.IQuestionnaireStore questionnaires) : ControllerBase
{
    [HttpGet("api/cases")]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20, CancellationToken ct = default) => Ok(await service.ListAsync(await access.ActorAsync(User, ct), page, pageSize, ct));
    [HttpGet("api/cases/{caseId:guid}")]
    public async Task<IActionResult> Detail(Guid caseId, CancellationToken ct) => Ok(CaseGuardianService.Dto(await service.ReadAsync(await access.ActorAsync(User, ct), caseId, ct)));
    [HttpPost("api/cases")]
    public async Task<IActionResult> Create(CaseInput input, CancellationToken ct) { var c = await service.CreateAsync(await access.ActorAsync(User, ct), input, ct); return CreatedAtAction(nameof(Detail), new { caseId = c.CaseId }, c); }
    [HttpPut("api/cases/{caseId:guid}")]
    public async Task<IActionResult> Update(Guid caseId, CaseUpdateInput input, CancellationToken ct) => Ok(await service.UpdateAsync(await access.ActorAsync(User, ct), caseId, input, ct));
    [HttpGet("api/cases/{caseId:guid}/guardians")]
    public async Task<IActionResult> Guardians(Guid caseId, CancellationToken ct) => Ok(await service.GuardiansAsync(await access.ActorAsync(User, ct), caseId, ct));
    [HttpPost("api/cases/{caseId:guid}/guardians")]
    public async Task<IActionResult> Guardian(Guid caseId, GuardianInput input, CancellationToken ct) => StatusCode(201, await service.AddGuardianAsync(await access.ActorAsync(User, ct), caseId, input, ct));
    [HttpPut("api/cases/{caseId:guid}/guardians/{guardianId:guid}")]
    public async Task<IActionResult> UpdateGuardian(Guid caseId, Guid guardianId, GuardianUpdateInput input, CancellationToken ct) { await service.UpdateGuardianAsync(await access.ActorAsync(User, ct), caseId, guardianId, input, ct); return NoContent(); }
    [HttpPost("api/cases/{caseId:guid}/guardians/{guardianId:guid}/invitations")]
    public async Task<IActionResult> Invite(Guid caseId, Guid guardianId, CancellationToken ct) { var i = await service.InviteAsync(await access.ActorAsync(User, ct), caseId, guardianId, ct); return StatusCode(201, new { i.InvitationId, invitationCode = i.Code, i.ExpiresAtUtc }); }
    [HttpPost("api/parent-binding-invitations/{invitationId:guid}/revoke")]
    public async Task<IActionResult> RevokeInvite(Guid invitationId, CancellationToken ct) { await service.RevokeInviteAsync(await access.ActorAsync(User, ct), invitationId, ct); return NoContent(); }
    [HttpPost("api/cases/{caseId:guid}/guardians/{guardianId:guid}/revoke")]
    public async Task<IActionResult> RevokeLink(Guid caseId, Guid guardianId, CancellationToken ct) { await service.RevokeLinkAsync(await access.ActorAsync(User, ct), caseId, guardianId, ct); return NoContent(); }
    [HttpGet("api/guardians")]
    public async Task<IActionResult> Contacts(int page = 1, int pageSize = 20, CancellationToken ct = default) => Ok(await service.ContactListAsync(await access.ActorAsync(User, ct), page, pageSize, ct));
    [HttpPost("api/cases/{caseId:guid}/guardians/{guardianId:guid}/link")]
    public async Task<IActionResult> Link(Guid caseId, Guid guardianId, GuardianLinkInput input, CancellationToken ct) => Ok(await service.LinkGuardianAsync(await access.ActorAsync(User, ct), caseId, guardianId, input, ct));
    [HttpGet("api/parent/cases")]
    public async Task<IActionResult> MyCases(int page = 1, int pageSize = 20, CancellationToken ct = default) { var a = await access.ActorAsync(User, ct); CaseAccessService.Parent(a); return Ok(await service.ParentCasesAsync(a, page, pageSize, ct)); }
    [HttpPost("api/parent/bindings"), EnableRateLimiting("parent-binding")]
    public async Task<IActionResult> Bind(BindInput input, CancellationToken ct) => Ok(await service.BindAsync(await access.ActorAsync(User, ct), input.InvitationCode, ct));
    [HttpGet("api/parent/cases/{caseId:guid}/tasks")]
    public async Task<IActionResult> ParentTasks(Guid caseId, CancellationToken ct)
    {
        var actor = await access.ActorAsync(User, ct); CaseAccessService.Parent(actor); await service.ReadAsync(actor, caseId, ct);
        return Ok((await questionnaires.ListTasksAsync(caseId, ct)).Where(t => t.RespondentRole == "PARENT" && t.TaskStatus != "CANCELLED").ToArray());
    }
    [HttpGet("api/parent/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct) => Ok(await service.ProfilesAsync(await access.ActorAsync(User, ct), ct));
    [HttpPut("api/parent/profile/{guardianId:guid}")]
    public async Task<IActionResult> UpdateProfile(Guid guardianId, ProfileInput input, CancellationToken ct) { await service.UpdateProfileAsync(await access.ActorAsync(User, ct), guardianId, input, ct); return NoContent(); }
    [HttpGet("api/parent/cases/{caseId:guid}/teacher-completion")]
    public async Task<IActionResult> TeacherCompletion(Guid caseId, CancellationToken ct) => Ok(await service.TeacherCompletionAsync(await access.ActorAsync(User, ct), caseId, ct));
}
