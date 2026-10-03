using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Route("api/dev/teacher-grants")]
public sealed class DevelopmentTeacherGrantsController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("{grantId}/revoke")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(RevokeTeacherGrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RevokeTeacherGrantResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<RevokeTeacherGrantResponse> Revoke([FromRoute] Guid grantId)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var service = HttpContext.RequestServices.GetRequiredService<ITeacherGrantService>();
        var result = service.Revoke(grantId, HttpContext.TraceIdentifier);
        if (result is null) return NotFound();
        return result.Status == "REVOKED" ? Ok(result) : Conflict(result);
    }

    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<CreateTeacherGrantResponse> Create(CreateTeacherGrantRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        var service = HttpContext.RequestServices.GetRequiredService<ITeacherGrantService>();
        return Ok(service.Create(request.ExpiresInSeconds, HttpContext.TraceIdentifier));
    }
}
