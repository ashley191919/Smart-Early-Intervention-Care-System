using System.ComponentModel.DataAnnotations;
using EarlyInterventionCare.Api.Development;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EarlyInterventionCare.Api.Controllers;

public sealed record VerifyWorkspaceCodeRequest([Required, MaxLength(64)] string? Code);

[ApiController]
[Route("api/dev/teacher-workspace")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DevelopmentTeacherWorkspaceController(IWebHostEnvironment environment) : ControllerBase
{
    private const string CookieName = "EarlyCare.DevTeacher";
    private const string CookiePath = "/api/dev/teacher-workspace";
    private TeacherWorkspaceService Service => HttpContext.RequestServices.GetRequiredService<TeacherWorkspaceService>();
    private CookieOptions Cookie => new() { HttpOnly = true, Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict, Path = CookiePath, IsEssential = true };

    [HttpPost("grants")]
    public IActionResult Create()
    {
        if (!environment.IsDevelopment()) return NotFound();
        try { return Ok(Service.Create(HttpContext.TraceIdentifier)); }
        catch (InvalidOperationException) { return StatusCode(503, new { message = "測試授權已達上限，請重啟開發服務。" }); }
    }

    [HttpPost("verify")]
    [EnableRateLimiting("teacher-workspace-code")]
    public IActionResult Verify(VerifyWorkspaceCodeRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        Service.Logout(Request.Cookies[CookieName]);
        var session = Service.Verify(request.Code, HttpContext.TraceIdentifier);
        if (session is null)
        {
            Response.Cookies.Delete(CookieName, Cookie);
            return Unauthorized(new { message = "測試授權碼無效或已失效。" });
        }
        Response.Cookies.Append(CookieName, session, Cookie);
        return Ok(new { workspaceUrl = "/teacher-workspace.html?mode=authorized", notice = TeacherWorkspaceService.Notice });
    }

    [HttpGet("task")]
    public IActionResult Task()
    {
        if (!environment.IsDevelopment()) return NotFound();
        var task = Service.GetTask(Request.Cookies[CookieName]);
        return task is null ? Unauthorized(new { message = "請先驗證有效的測試授權碼。" }) : Ok(task);
    }

    [HttpPost("grants/{grantId:guid}/revoke")]
    public IActionResult Revoke(Guid grantId)
    {
        if (!environment.IsDevelopment()) return NotFound();
        return Service.Revoke(grantId, HttpContext.TraceIdentifier) ? Ok(new { grantId, status = "REVOKED" }) : NotFound();
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (!environment.IsDevelopment()) return NotFound();
        Service.Logout(Request.Cookies[CookieName]);
        Response.Cookies.Delete(CookieName, Cookie);
        return NoContent();
    }
}
