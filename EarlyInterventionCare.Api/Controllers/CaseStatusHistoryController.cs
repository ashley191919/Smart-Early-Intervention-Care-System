using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cases")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[CaseStatusErrors]
public sealed class CaseStatusHistoryController(CaseStatusHistoryQueryService service,
    ICaseStatusHistoryAccessAuthorizer accessAuthorizer) : ControllerBase
{
    [HttpGet("{caseId}/status-history")]
    [ProducesResponseType(typeof(CaseStatusHistoryPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CaseStatusHistoryPageResponse>> GetHistory(Guid caseId, int page = 1,
        int pageSize = CaseStatusHistoryQueryService.DefaultPageSize, CancellationToken ct = default)
    {
        if (caseId == Guid.Empty)
        {
            return BadRequest(new
            {
                code = "INVALID_INPUT",
                message = "個案識別碼無效。",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var access = await accessAuthorizer.AuthorizeReadAsync(User, caseId, ct);
        if (!access.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = access.Code,
                message = access.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }

        var result = await service.GetPageAsync(caseId, page, pageSize, ct);
        var items = result.Items.Select(item => new CaseStatusHistoryItemResponse(item.HistoryId, item.CaseId,
            item.OldStatus, item.NewStatus, item.ChangedAtUtc, item.TriggerSource,
            access.CanViewReason ? item.Reason : null)).ToArray();
        return Ok(new CaseStatusHistoryPageResponse(result.CaseId, result.Page, result.PageSize,
            result.TotalCount, items));
    }
}
