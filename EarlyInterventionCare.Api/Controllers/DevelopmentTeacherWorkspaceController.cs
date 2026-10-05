using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text.Json;
using EarlyInterventionCare.Api.Development;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Controllers;

public sealed record VerifyWorkspaceCodeRequest([Required, MaxLength(64)] string? Code);

// Return stable errors without database details or connection credentials.
public sealed class WorkspaceErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is WorkspaceOperationException operation)
            context.Result = new ObjectResult(new { code = operation.Code, message = operation.Message, traceId = context.HttpContext.TraceIdentifier }) { StatusCode = operation.Status };
        else if (context.Exception is DbException or DbUpdateException or JsonException || context.Exception is InvalidOperationException)
            context.Result = new ObjectResult(new { code = "SAVE_UNAVAILABLE", message = "暫時無法讀寫開發資料庫，請確認 MySQL、連線設定及 migration。", traceId = context.HttpContext.TraceIdentifier }) { StatusCode = 503 };
        else return;
        context.ExceptionHandled = true;
    }
}

[ApiController]
[Route("api/dev/teacher-workspace")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[WorkspaceErrors]
public sealed class DevelopmentTeacherWorkspaceController(IWebHostEnvironment environment, TeacherWorkspaceService service) : ControllerBase
{
    private const string CookieName = "EarlyCare.DevTeacher";
    private const string CookiePath = "/api/dev/teacher-workspace";
    private CookieOptions Cookie => new() { HttpOnly = true, Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict, Path = CookiePath, IsEssential = true };

    [HttpPost("grants")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var origin = TeacherInvitation.LocalOrigin(Request);
        var grant = await service.CreateAsync(HttpContext.TraceIdentifier, ct);
        var invitation = TeacherInvitation.Generate(origin, grant.AuthorizationCode);
        return Ok(new { grant.GrantId, grant.AuthorizationCode, grant.TaskId,
            grant.QuestionnaireVersionId, grant.Notice, invitationUrl = invitation.Url,
            qrCodeDataUrl = invitation.QrImage });
    }

    [HttpPost("verify")]
    [EnableRateLimiting("teacher-workspace-code")]
    public async Task<IActionResult> Verify(VerifyWorkspaceCodeRequest request, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        await service.LogoutAsync(Request.Cookies[CookieName], ct, HttpContext.TraceIdentifier);
        Response.Cookies.Delete(CookieName, Cookie);
        var session = await service.VerifyAsync(request.Code, HttpContext.TraceIdentifier, ct);
        if (session is null) return Unauthorized(new { code = "GRANT_UNAVAILABLE", message = "測試授權碼無效或已失效。", traceId = HttpContext.TraceIdentifier });
        Response.Cookies.Append(CookieName, session, Cookie);
        return Ok(new { workspaceUrl = "/teacher-workspace.html?mode=authorized", notice = TeacherWorkspaceService.Notice });
    }

    [HttpGet("task")]
    public async Task<IActionResult> Task(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var task = await service.GetTaskAsync(Request.Cookies[CookieName], ct);
        return task is null ? Unauthorized(new { code = "SESSION_REQUIRED", message = "請先驗證有效的測試授權碼。", traceId = HttpContext.TraceIdentifier }) : Ok(task);
    }

    [HttpPost("grants/{grantId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid grantId, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        return await service.RevokeAsync(grantId, HttpContext.TraceIdentifier, ct) ? Ok(new { grantId, status = "REVOKED" }) : NotFound();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        await service.LogoutAsync(Request.Cookies[CookieName], ct, HttpContext.TraceIdentifier);
        Response.Cookies.Delete(CookieName, Cookie);
        return NoContent();
    }
}
