using System.ComponentModel.DataAnnotations;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EarlyInterventionCare.Api.Controllers;

public sealed class ParentOtpRequest
{
    [Required, StringLength(24)] public string Phone { get; init; } = "";
}
public sealed class ParentOtpVerifyRequest
{
    [Required, StringLength(200)] public string ChallengeId { get; init; } = "";
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; init; } = "";
}

[ApiController, AllowAnonymous, CaseErrors, Route("api/parent/auth"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ParentAuthenticationController(ParentOtpAuthenticationService service) : ControllerBase
{
    [HttpPost("otp/request"), EnableRateLimiting("parent-otp-request")]
    public async Task<IActionResult> RequestOtp(ParentOtpRequest input, CancellationToken ct)
        => Ok(await service.RequestAsync(input.Phone, ct));

    [HttpPost("otp/verify"), EnableRateLimiting("parent-otp-verify")]
    public async Task<IActionResult> Verify(ParentOtpVerifyRequest input, CancellationToken ct)
        => Ok(await service.VerifyAsync(input.ChallengeId, input.Code, ct));
}
