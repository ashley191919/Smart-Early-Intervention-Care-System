using System.ComponentModel.DataAnnotations;
using EarlyInterventionCare.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Route("api/dev/audit-logs")]
public sealed class DevelopmentAuditLogsController(IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Development only: records are cleared on restart and are not durable production auditing.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get([FromQuery] Guid? grantId, [FromQuery, Range(1, 200)] int limit = 50)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var audit = HttpContext.RequestServices.GetRequiredService<IAuditLogService>();
        return Ok(new {
            notice = "開發測試：操作紀錄僅暫存於記憶體，服務重啟後清除，尚非正式持久化稽核。",
            events = audit.Query(grantId.HasValue ? "TeacherGrant" : null, grantId?.ToString(), limit)
        });
    }
}
