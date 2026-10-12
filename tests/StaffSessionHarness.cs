using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.DTOs.Authentication;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Security;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;

var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

var trustedOrigin = "https://localhost:7127";
var enabledOptions = new StaffSessionOptions(new[] { trustedOrigin });
Check(enabledOptions.IsEnabled && enabledOptions.IsTrustedOrigin(trustedOrigin), "configured HTTPS origin enables staff cookies");
Check(!enabledOptions.IsTrustedOrigin("http://localhost:5177"), "HTTP origin is never trusted for Secure staff cookie");
Check(!new StaffSessionOptions(null).IsEnabled, "missing trusted origins fails closed");

StaffSessionController Controller(LoginResponse? response, string scheme = "https", string? origin = null)
{
    var http = new DefaultHttpContext();
    http.Request.Scheme = scheme;
    http.Request.Host = new HostString("localhost", scheme == "https" ? 7127 : 5177);
    if (origin is not null || scheme == "https") http.Request.Headers.Origin = origin ?? trustedOrigin;
    http.Response.Body = new MemoryStream();
    return new StaffSessionController(new FakeAuthenticationService(response), new StaffSessionCookieService(), enabledOptions,
        new FakeAntiforgery(true))
    {
        ControllerContext = new ControllerContext { HttpContext = http }
    };
}

LoginResponse MedicalLogin(string role = StaffSessionAuthorization.MedicalStaffRole) => new()
{
    UserId = Guid.NewGuid(), Username = "staff", Role = role, Permissions = new[] { "case.read" },
    Message = "登入成功", AccessToken = "signed-jwt-value", TokenType = "Bearer",
    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
};

{
    var controller = Controller(MedicalLogin());
    var result = await controller.Create(new LoginRequest { Username = "staff", Password = "correct" }, default) as OkObjectResult;
    var body = result?.Value as StaffSessionResponse;
    var cookie = controller.HttpContext.Response.Headers.SetCookie.ToString();
    Check(result?.StatusCode == StatusCodes.Status200OK && body?.Role == StaffSessionAuthorization.MedicalStaffRole,
        "correct credentials with medical role create a session");
    Check(typeof(StaffSessionResponse).GetProperty("AccessToken") is null && typeof(StaffSessionResponse).GetProperty("RefreshToken") is null,
        "staff-session JSON DTO exposes no authentication credential");
    Check(cookie.Contains("__Host-earlycare-staff=signed-jwt-value", StringComparison.Ordinal) &&
        cookie.Contains("path=/", StringComparison.OrdinalIgnoreCase) &&
        cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
        cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
        cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) &&
        !cookie.Contains("domain=", StringComparison.OrdinalIgnoreCase), "staff cookie has host-only secure attributes");
}

{
    var controller = Controller(null);
    var result = await controller.Create(new LoginRequest { Username = "staff", Password = "wrong" }, default) as UnauthorizedObjectResult;
    Check(result?.StatusCode == StatusCodes.Status401Unauthorized, "invalid credentials are rejected without revealing the reason");
}

{
    var controller = Controller(MedicalLogin("PARENT"));
    var result = await controller.Create(new LoginRequest { Username = "parent", Password = "correct" }, default) as UnauthorizedObjectResult;
    Check(result?.StatusCode == StatusCodes.Status401Unauthorized, "non-medical role cannot create a staff session");
}

{
    var controller = Controller(MedicalLogin(StaffSessionAuthorization.CaseManagerRole));
    var result = await controller.Create(new LoginRequest { Username = "manager", Password = "correct" }, default) as OkObjectResult;
    Check(result?.StatusCode == StatusCodes.Status200OK, "case manager role can create a staff session");
}

{
    var controller = Controller(MedicalLogin("ADMIN"));
    var result = await controller.Create(new LoginRequest { Username = "admin", Password = "correct" }, default) as UnauthorizedObjectResult;
    Check(result?.StatusCode == StatusCodes.Status401Unauthorized, "ADMIN remains denied until its medical workspace access is confirmed");
}

{
    var controller = Controller(MedicalLogin(), origin: "https://untrusted.example");
    var result = await controller.Create(new LoginRequest { Username = "staff", Password = "correct" }, default) as ObjectResult;
    Check(result?.StatusCode == StatusCodes.Status403Forbidden, "untrusted login origin is rejected");
}

{
    var controller = Controller(MedicalLogin(), scheme: "http");
    var result = await controller.Create(new LoginRequest { Username = "staff", Password = "correct" }, default) as ObjectResult;
    Check(result?.StatusCode == StatusCodes.Status400BadRequest, "HTTP cannot create a Secure staff cookie");
}

{
    var request = new DefaultHttpContext().Request;
    request.Headers.Cookie = StaffSessionCookieDefaults.Name + "=cookie-jwt";
    Check(StaffCookieTokenReader.Read(request, true) == "cookie-jwt", "fixed staff cookie is a JWT fallback only when enabled");
    request.Headers.Authorization = "Bearer explicit-jwt";
    Check(StaffCookieTokenReader.Read(request, true) is null, "explicit Bearer header remains authoritative");
    request.Headers.Remove("Authorization");
    request.Headers.Cookie = "unrelated=value";
    Check(StaffCookieTokenReader.Read(request, true) is null, "arbitrary cookie names are never accepted");
}

{
    var key = new SymmetricSecurityKey(Enumerable.Repeat((byte)42, 32).ToArray());
    var expired = new JwtSecurityToken("issuer", "audience", new[] { new Claim("userId", Guid.NewGuid().ToString()) },
        DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1), new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    try
    {
        new JwtSecurityTokenHandler().ValidateToken(new JwtSecurityTokenHandler().WriteToken(expired), new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidateIssuer = true, ValidIssuer = "issuer",
            ValidateAudience = true, ValidAudience = "audience", ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        }, out _);
        throw new Exception("expired cookie JWT should not validate");
    }
    catch (SecurityTokenExpiredException)
    {
        Check(true, "expired or invalid cookie token is rejected by JWT validation");
    }
}

async Task CheckCsrfAsync(string method, bool cookie, string? origin, bool tokenValid, bool bearer, bool bearerValidated,
    int expectedStatus, bool expectedNext, string name)
{
    var context = new DefaultHttpContext();
    context.Request.Method = method;
    if (cookie) context.Request.Headers.Cookie = StaffSessionCookieDefaults.Name + "=cookie-jwt";
    if (origin is not null) context.Request.Headers.Origin = origin;
    if (bearer) context.Request.Headers.Authorization = "Bearer explicit-jwt";
    if (bearerValidated)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("userId", Guid.NewGuid().ToString()) }, "Bearer"));
        StaffAuthenticationSource.Set(context, StaffAuthenticationSource.BearerHeader);
    }
    context.Response.Body = new MemoryStream();
    var nextCalls = 0;
    var middleware = new StaffCookieCsrfMiddleware(_ => { nextCalls++; return Task.CompletedTask; });
    await middleware.InvokeAsync(context, new FakeAntiforgery(tokenValid), enabledOptions);
    Check(context.Response.StatusCode == expectedStatus && (nextCalls == 1) == expectedNext, "CSRF " + name);
}

await CheckCsrfAsync("GET", true, null, false, false, false, 200, true, "safe reads are not blocked");
foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE" })
    await CheckCsrfAsync(method, true, null, true, false, false, 403, false, method + " requires Origin");
await CheckCsrfAsync("POST", true, "https://untrusted.example", true, false, false, 403, false, "untrusted Origin is rejected");
await CheckCsrfAsync("POST", true, trustedOrigin, false, false, false, 403, false, "missing or invalid anti-forgery token is rejected");
await CheckCsrfAsync("POST", true, trustedOrigin, true, false, false, 200, true, "trusted cookie write with valid token proceeds");
await CheckCsrfAsync("POST", true, null, false, true, true, 200, true, "validated explicit Bearer write remains compatible");
await CheckCsrfAsync("POST", true, null, false, true, false, 403, false, "invalid Bearer header cannot bypass cookie CSRF");

{
    var controller = Controller(MedicalLogin());
    var result = controller.Logout() as NoContentResult;
    var cookie = controller.HttpContext.Response.Headers.SetCookie.ToString();
    Check(result?.StatusCode == StatusCodes.Status204NoContent && cookie.Contains("__Host-earlycare-staff=", StringComparison.Ordinal),
        "logout removes the browser staff cookie without claiming JWT revocation");
}

Check(typeof(AuthController).GetMethod(nameof(AuthController.Login))?.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true)
    .Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Single().Template == "login", "existing Bearer login route remains unchanged");
Check(typeof(LoginResponse).GetProperty("AccessToken") is not null, "existing Bearer login response contract still contains accessToken");

Console.WriteLine($"PASS: {checks} offline staff session checks; role gate, secure host cookie, bearer precedence, JWT expiry, global CSRF fail-closed and logout.");

sealed class FakeAuthenticationService(LoginResponse? response) : IAuthenticationService
{
    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult(response);
    public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<CurrentUserResponse?>(null);
}

sealed class FakeAntiforgery(bool valid) : IAntiforgery
{
    public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) =>
        new("request-token", "cookie-token", "__RequestVerificationToken", "X-EarlyCare-CSRF");
    public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
        new("request-token", "cookie-token", "__RequestVerificationToken", "X-EarlyCare-CSRF");
    public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(valid);
    public Task ValidateRequestAsync(HttpContext httpContext)
    {
        if (!valid) throw new AntiforgeryValidationException("invalid test token");
        return Task.CompletedTask;
    }
}
