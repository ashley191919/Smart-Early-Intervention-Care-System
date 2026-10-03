using EarlyInterventionCare.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
public sealed class TeacherTestFormController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("/teacher/test-form")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get([FromQuery] string? token)
    {
        if (!environment.IsDevelopment()) return NotFound();
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'";
        var service = HttpContext.RequestServices.GetRequiredService<ITeacherGrantService>();
        var grant = Request.Query["token"].Count == 1 ? service.Validate(token) : null;
        if (grant is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Content("<!doctype html><html lang=\"zh-Hant\"><meta charset=\"utf-8\"><title>連結無效</title><p>連結無效或已到期</p></html>", "text/html; charset=utf-8");
        }

        // All displayed content is a fixed fixture. Query parameters cannot select data.
        return Content("""
            <!doctype html>
            <html lang="zh-Hant">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>教師測試表單</title></head>
            <body>
            <h1>教師測試表單</h1>
            <p>開發測試：尚未提供提交功能</p>
            <p>虛構個案：dev-case-001；問卷版本：dev-questionnaire-v1；填答任務：dev-task-001</p>
            <p>這是固定測試資料，不代表真實家長同意。選項不會保存。</p>
            <fieldset><legend>1. 測試幼兒是否能在提醒後參與團體活動？</legend>
            <label><input type="radio" name="question1" value="yes">可以</label>
            <label><input type="radio" name="question1" value="sometimes">有時可以</label>
            <label><input type="radio" name="question1" value="no">尚未觀察到</label></fieldset>
            <fieldset><legend>2. 測試幼兒是否能用簡單語句表達需求？</legend>
            <label><input type="radio" name="question2" value="yes">可以</label>
            <label><input type="radio" name="question2" value="sometimes">有時可以</label>
            <label><input type="radio" name="question2" value="no">尚未觀察到</label></fieldset>
            </body></html>
            """, "text/html; charset=utf-8");
    }
}
