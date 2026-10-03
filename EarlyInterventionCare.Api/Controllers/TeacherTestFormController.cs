using EarlyInterventionCare.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
public sealed class TeacherTestFormController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("/teacher/test-form")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Submit([FromQuery] string? token)
    {
        if (!environment.IsDevelopment()) return NotFound();
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
        var service = HttpContext.RequestServices.GetRequiredService<ITeacherGrantService>();
        if (Request.Query["token"].Count != 1 || service.Validate(token) is null)
            return NotFound("連結無效或已到期");
        if (!Request.HasFormContentType) return BadRequest("兩題皆必填，請選擇合法選項後重送。");
        var form = await Request.ReadFormAsync();
        var result = service.Submit(token,
            form["question1"].Count == 1 ? form["question1"].ToString() : null,
            form["question2"].Count == 1 ? form["question2"].ToString() : null);
        if (result == EarlyInterventionCare.Api.Development.SubmissionResult.InvalidGrant)
            return NotFound("連結無效或已到期");
        if (result == EarlyInterventionCare.Api.Development.SubmissionResult.InvalidAnswers)
            return BadRequest("兩題皆必填，請返回表單選擇合法選項後重送。");
        if (result == EarlyInterventionCare.Api.Development.SubmissionResult.SaveFailed)
            return StatusCode(503, "保存失敗，請稍後重送。");
        return Content("<!doctype html><html lang=\"zh-Hant\"><meta charset=\"utf-8\"><title>送出成功</title><p>已成功送出，此連結已失效</p><p>開發測試：回覆僅暫存於記憶體，服務重啟後清除</p></html>", "text/html; charset=utf-8");
    }
    [HttpGet("/teacher/test-form")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get([FromQuery] string? token)
    {
        if (!environment.IsDevelopment()) return NotFound();
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
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
            <p>開發測試：回覆僅暫存於記憶體，服務重啟後清除</p>
            <p>虛構個案：dev-case-001；問卷版本：dev-questionnaire-v1；填答任務：dev-task-001</p>
            <p>這是固定測試資料，不代表真實家長同意。兩題皆必填。</p>
            <form method="post"><fieldset><legend>1. 測試幼兒是否能在提醒後參與團體活動？</legend>
            <label><input type="radio" required name="question1" value="yes">可以</label>
            <label><input type="radio" required name="question1" value="sometimes">有時可以</label>
            <label><input type="radio" required name="question1" value="no">尚未觀察到</label></fieldset>
            <fieldset><legend>2. 測試幼兒是否能用簡單語句表達需求？</legend>
            <label><input type="radio" required name="question2" value="yes">可以</label>
            <label><input type="radio" required name="question2" value="sometimes">有時可以</label>
            <label><input type="radio" required name="question2" value="no">尚未觀察到</label></fieldset>
            <button type="submit">提交</button></form></body></html>
            """, "text/html; charset=utf-8");
    }
}
