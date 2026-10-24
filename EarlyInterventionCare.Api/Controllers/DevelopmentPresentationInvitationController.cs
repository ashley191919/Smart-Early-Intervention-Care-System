using EarlyInterventionCare.Api.Development;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace EarlyInterventionCare.Api.Controllers;

// Rendering only: never creates or validates a Grant, Session or Consent.
// Accepts browser fixture codes exclusively; no database or identity service.
[ApiController]
[Route("api/dev/presentation-invitation")]
public sealed class DevelopmentPresentationInvitationController(IWebHostEnvironment environment) : ControllerBase
{
    public sealed record RenderRequest(string? Code);

    [HttpPost]
    public IActionResult Render(RenderRequest request)
    {
        if (!environment.IsDevelopment() || !Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && Request.Host.Host != "127.0.0.1" && Request.Host.Host != "[::1]") return NotFound();
        Response.Headers.CacheControl = "no-store";
        if (!Regex.IsMatch(request.Code ?? "", "^DEMO-[A-F0-9]{8}$"))
            return BadRequest(new { code = "INVALID_PRESENTATION_CODE", message = "無法產生邀請圖片。" });
        var invitation = TeacherInvitation.Generate(TeacherInvitation.LocalOrigin(Request), request.Code!);
        return Ok(new { invitationUrl = invitation.Url, qrCodeDataUrl = invitation.QrImage });
    }
}
