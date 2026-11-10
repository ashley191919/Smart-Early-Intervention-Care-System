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

[ApiController, Authorize, MedicalCaseReadErrors, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MedicalCaseReadController(CaseAccessService access) : ControllerBase
{
    [HttpGet("api/cases")]
    public async Task<ActionResult<CaseDto[]>> List(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var cases = await ReadableCasesAsync(ct);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw new CaseAccessException("INVALID_PAGE", 400, "請確認頁碼。");

        var rows = await cases
            .OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.CaseId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(rows.Select(Dto).ToArray());
    }

    [HttpGet("api/cases/{caseId:guid}")]
    public async Task<ActionResult<CaseDto>> Detail(Guid caseId, CancellationToken ct = default)
    {
        var cases = await ReadableCasesAsync(ct);
        var row = await cases.SingleOrDefaultAsync(c => c.CaseId == caseId, ct)
            ?? throw new CaseAccessException("CASE_NOT_FOUND", 404, "找不到可存取的個案。");
        return Ok(Dto(row));
    }

    private async Task<IQueryable<CaseRecord>> ReadableCasesAsync(CancellationToken ct)
    {
        var actor = await access.ActorAsync(User, ct);
        if (!StaffSessionAuthorization.CanCreateStaffSession(actor.Role))
            throw new CaseAccessException("FORBIDDEN", StatusCodes.Status403Forbidden, "無法執行此操作。");

        return access.ReadableCases(actor);
    }

    private static CaseDto Dto(CaseRecord row) => new(row.CaseId, row.CaseCode, row.ChildName,
        row.BirthDate, row.Sex, row.NationalId, row.Address, row.CaseStatus, row.Revision,
        DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));
}

public sealed class MedicalCaseReadErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is CaseAccessException access)
            context.Result = new ObjectResult(new { code = access.Code, message = access.Message }) { StatusCode = access.Status };
        else if (context.Exception is DbException or TimeoutException)
            context.Result = new ObjectResult(new { code = "SERVICE_UNAVAILABLE", message = "個案服務暫時無法使用。" }) { StatusCode = 503 };
        else
            return;

        context.ExceptionHandled = true;
    }
}
