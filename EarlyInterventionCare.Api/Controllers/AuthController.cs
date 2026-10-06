using System.Data.Common;
using EarlyInterventionCare.Api.DTOs.Authentication;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace EarlyInterventionCare.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthenticationService _authenticationService;

    public AuthController(AuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authenticationService.LoginAsync(request, cancellationToken);
            return response is null
                ? Unauthorized(new { Message = "帳號或密碼錯誤" })
                : Ok(response);
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

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var ids = User.FindAll("userId").ToArray();
        if (ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var userId) || userId == Guid.Empty)
            return Unauthorized(new { Message = "登入憑證無效" });

        try
        {
            var response = await _authenticationService.GetCurrentUserAsync(userId, cancellationToken);
            return response is null
                ? Unauthorized(new { Message = "登入憑證無效" })
                : Ok(response);
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
}
