using System.Data.Common;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Services.Authentication;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cases")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[CaseReadErrors]
public sealed class MedicalCaseReadController(CaseAccessService access) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CaseDto[]>> List(int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        var cases = await ReadableCasesAsync(ct);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            return BadRequest(new { code = "INVALID_PAGE", message = "請確認頁碼。" });

        var rows = await cases.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.CaseId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(rows.Select(ToDto).ToArray());
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<CaseDto>> Detail(Guid caseId, CancellationToken ct = default)
    {
        var cases = await ReadableCasesAsync(ct);
        var row = await cases.SingleOrDefaultAsync(c => c.CaseId == caseId, ct);
        if (row is null)
            return NotFound(new { code = "CASE_NOT_FOUND", message = "找不到可存取的個案。" });

        return Ok(ToDto(row));
    }

    private async Task<IQueryable<CaseRecord>> ReadableCasesAsync(CancellationToken ct)
    {
        var actor = await access.ActorAsync(User, ct);
        if (!StaffSessionAuthorization.CanCreateStaffSession(actor.Role))
            throw new CaseAccessException("FORBIDDEN", StatusCodes.Status403Forbidden, "無法執行此操作。");

        return access.ReadableCases(actor);
    }

    private static CaseDto ToDto(CaseRecord row) => new(row.CaseId, row.CaseCode, row.ChildName,
        row.BirthDate, row.Sex, row.NationalId, row.Address, row.CaseStatus, row.Revision,
        DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));
}

[AttributeUsage(AttributeTargets.Class)]
internal sealed class CaseReadErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is CaseAccessException error)
            context.Result = new ObjectResult(new { code = error.Code, message = error.Message })
                { StatusCode = error.Status };
        else if (context.Exception is DbException or TimeoutException)
            context.Result = new ObjectResult(new { code = "SERVICE_UNAVAILABLE", message = "個案服務暫時無法使用。" })
                { StatusCode = StatusCodes.Status503ServiceUnavailable };
        else
            return;

        context.ExceptionHandled = true;
    }
}
