using System.Data.Common;
using EarlyInterventionCare.Api.DTOs.Authentication;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EarlyInterventionCare.Api.Security;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffSessionController(IAuthenticationService authenticationService,
    StaffSessionCookieService cookies, StaffSessionOptions options, IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("staff-session")]
    [AllowAnonymous]
    [EnableRateLimiting(StaffLoginRateLimitPolicy.Name)]
    [ProducesResponseType(typeof(StaffSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (!options.IsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "STAFF_SESSION_NOT_CONFIGURED", message = "醫療端登入 Session 尚未設定可信來源。" });

        if (!Request.IsHttps)
            return BadRequest(new { code = "HTTPS_REQUIRED", message = "醫療端 Cookie 登入必須使用 HTTPS。" });

        var origins = Request.Headers.Origin;
        if (origins.Count != 1 || string.IsNullOrWhiteSpace(origins[0]))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = "ORIGIN_REQUIRED", message = "登入請求必須提供可信 Origin。" });

        if (!options.IsTrustedOrigin(origins[0]))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = "INVALID_ORIGIN", message = "登入請求來源不受信任。" });

        // Prevent an old browser session from surviving a failed or unauthorized re-login attempt.
        cookies.Delete(Response);

        try
        {
            var login = await authenticationService.LoginAsync(request, cancellationToken);
            if (login is null || !StaffSessionAuthorization.CanCreateStaffSession(login.Role))
                return Unauthorized(new { Message = "帳號或密碼錯誤" });

            cookies.Set(Response, login.AccessToken, login.ExpiresAt);
            return Ok(new StaffSessionResponse
            {
                UserId = login.UserId,
                Username = login.Username,
                Role = login.Role,
                Permissions = login.Permissions,
                Message = login.Message,
                ExpiresAt = login.ExpiresAt
            });
        }
        catch (DbException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Message = "登入服務暫時無法使用" });
        }
        catch (TimeoutException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Message = "登入服務暫時無法使用" });
        }
    }

    [HttpGet("csrf")]
    [Authorize]
    [ProducesResponseType(typeof(AntiforgeryTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult CreateCsrfToken()
    {
        if (!options.IsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "STAFF_SESSION_NOT_CONFIGURED", message = "醫療端登入 Session 尚未設定可信來源。" });

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken ?? string.Empty));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Logout()
    {
        // This only removes the browser cookie. The stateless JWT remains valid until it expires.
        cookies.Delete(Response);
        return NoContent();
    }
}
