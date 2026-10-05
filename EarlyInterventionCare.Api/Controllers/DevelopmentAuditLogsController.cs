using System.ComponentModel.DataAnnotations;
using EarlyInterventionCare.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Route("api/dev/audit-logs")]
public sealed class DevelopmentAuditLogsController(IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Local Development metadata only; formal role-protected access is not yet integrated.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get([FromQuery] Guid? grantId, [FromQuery, Range(1, 200)] int limit = 50)
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (HttpContext.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address) ||
            !Uri.TryCreate($"{Request.Scheme}://{Request.Host}", UriKind.Absolute, out var origin) || !origin.IsLoopback)
            return StatusCode(403, new { code = "LOCAL_AUDIT_ONLY" });
        var audit = HttpContext.RequestServices.GetRequiredService<IAuditLogService>();
        try
        {
            // grantId filters both the legacy fixture and the MySQL workspace, which use distinct resource types.
            return Ok(new {
                notice = "本機開發測試：操作紀錄已保存至 MySQL；尚未串接正式醫護查詢權限。",
                events = audit.Query(resourceId: grantId?.ToString(), limit: limit)
            });
        }
        catch (Exception)
        {
            return StatusCode(503, new { code = "AUDIT_UNAVAILABLE", message = "無法讀取操作紀錄，請確認資料庫連線及 migration。" });
        }
    }
}
