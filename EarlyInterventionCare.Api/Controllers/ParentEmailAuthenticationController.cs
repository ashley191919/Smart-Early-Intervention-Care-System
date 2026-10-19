using System.ComponentModel.DataAnnotations;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EarlyInterventionCare.Api.Controllers;

public sealed class ParentEmailLoginOptions
{
    // Enable only after 16's migration/backfill and single-process deployment review.
    public bool Enabled { get; set; }
}
public sealed class ParentEmailOtpRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
}

[ApiController, AllowAnonymous, CaseErrors, Route("api/parent/auth/email"), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ParentEmailAuthenticationController(ParentEmailAuthenticationService service, IOptions<ParentEmailLoginOptions> settings) : ControllerBase
{
    [HttpPost("request"), EnableRateLimiting("parent-otp-request")]
    public async Task<IActionResult> RequestCode(ParentEmailOtpRequest input, CancellationToken ct)
        => Guard() ?? Ok(await service.RequestAsync(input.Email, ct));

    [HttpPost("verify"), EnableRateLimiting("parent-otp-verify")]
    public async Task<IActionResult> Verify(ParentOtpVerifyRequest input, CancellationToken ct)
        => Guard() ?? Ok(await service.VerifyAsync(input.ChallengeId, input.Code, ct));

    private IActionResult? Guard()
    {
        if (!settings.Value.Enabled)
            return StatusCode(503,new {code="EMAIL_LOGIN_UNAVAILABLE",message="登入服務暫時無法使用，請稍後重試。"});
        var origin = Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !string.Equals(origin, $"{Request.Scheme}://{Request.Host}",StringComparison.OrdinalIgnoreCase))
            return StatusCode(403,new {code="REQUEST_ORIGIN_REJECTED",message="無法完成此操作，請重新開啟登入頁。"});
        return null;
    }
}
