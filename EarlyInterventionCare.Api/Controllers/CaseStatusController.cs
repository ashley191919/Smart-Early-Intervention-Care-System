using System.Data.Common;
using EarlyInterventionCare.Api.DTOs.Cases;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Controllers;

public sealed class CaseStatusErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is CaseStatusOperationException operation)
            context.Result = Error(context, operation.Status, operation.Code, operation.Message);
        else if (context.Exception is CaseStatusHistoryQueryOperationException historyOperation)
            context.Result = Error(context, historyOperation.Status, historyOperation.Code, historyOperation.Message);
        else if (context.Exception is DbException or DbUpdateException or TimeoutException)
            context.Result = Error(context, StatusCodes.Status503ServiceUnavailable, "SAVE_UNAVAILABLE",
                "暫時無法儲存個案狀態，請稍後再試。");
        else return;

        context.ExceptionHandled = true;
    }

    private static ObjectResult Error(ExceptionContext context, int status, string code, string message) =>
        new(new { code, message, traceId = context.HttpContext.TraceIdentifier }) { StatusCode = status };
}

[ApiController]
[Authorize]
[Route("api/cases")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[CaseStatusErrors]
public sealed class CaseStatusController(CaseStatusService service,
    ICaseStatusAccessAuthorizer accessAuthorizer) : ControllerBase
{
    [HttpPatch("{caseId:guid}/status")]
    [ProducesResponseType(typeof(ChangeCaseStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChangeCaseStatusResponse>> ChangeStatus(Guid caseId,
        [FromBody] ChangeCaseStatusRequest request, CancellationToken ct)
    {
        // The default implementation denies every request until a trusted Case scope is available.
        var access = await accessAuthorizer.AuthorizeChangeAsync(User, caseId, ct);
        if (!access.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = access.Code,
                message = access.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }

        var result = await service.ChangeManuallyAsync(caseId, request.NewStatus, request.Reason, ct);
        return Ok(new ChangeCaseStatusResponse(result.CaseId, result.OldStatus, result.NewStatus,
            result.ChangedAtUtc, result.HistoryId, result.Reason));
    }
}
