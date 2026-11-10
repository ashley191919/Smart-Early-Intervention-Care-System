using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Authentication;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Security;
using EarlyInterventionCare.Api.Services.Authentication;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// Real loopback HTTPS/JWT/authorization pipeline. No application Program startup,
// secrets or database connection. SQL command results are an explicitly offline double.
var allowedId = Guid.NewGuid();
var faultId = Guid.NewGuid();
var rows = new HttpRows(allowedId, faultId);
var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
const string trustedOrigin = "https://127.0.0.1";
var staffOptions = new StaffSessionOptions([trustedOrigin]);
using var tlsKey = RSA.Create(2048);
var certificateRequest = new CertificateRequest("CN=127.0.0.1", tlsKey,
    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var subjectNames = new SubjectAlternativeNameBuilder();
subjectNames.AddIpAddress(IPAddress.Loopback);
certificateRequest.CertificateExtensions.Add(subjectNames.Build());
using var generatedCertificate = certificateRequest.CreateSelfSigned(
    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
// Schannel needs a key handle it can open for TLS on Windows. Import the
// temporary certificate without installing it in a certificate store.
var pfx = generatedCertificate.Export(X509ContentType.Pfx);
using var certificate = new X509Certificate2(pfx);
CryptographicOperations.ZeroMemory(pfx);
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    EnvironmentName = "Production", Args = Array.Empty<string>()
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection();
builder.Logging.ClearProviders(); // Do not log JWTs or synthetic database diagnostics.
builder.WebHost.ConfigureKestrel(options =>
    options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(certificate)));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton(staffOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StaffSessionCookieService>();
builder.Services.AddSingleton<IAuthenticationService, UnusedStaffLogin>();
builder.Services.AddScoped<IQuestionnaireStore, EfQuestionnaireStore>();
builder.Services.AddScoped<QuestionnaireService>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-EarlyCare-CSRF";
    options.Cookie.Name = "__Host-earlycare-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.SaveToken = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        ValidateIssuer = true, ValidIssuer = "offline-test",
        ValidateAudience = true, ValidAudience = "offline-test",
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ClockSkew = TimeSpan.Zero, NameClaimType = "username", RoleClaimType = "role"
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            context.Token = StaffCookieTokenReader.Read(context.Request, staffOptions.IsEnabled);
            if (!string.IsNullOrWhiteSpace(context.Token))
                StaffAuthenticationSource.Set(context.HttpContext, StaffAuthenticationSource.StaffCookie);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            if (context.HttpContext.Request.Headers.ContainsKey("Authorization"))
                StaffAuthenticationSource.Set(context.HttpContext, StaffAuthenticationSource.BearerHeader);
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql("Server=invalid.invalid;Database=offline_rbac;User=offline;Pooling=False;",
        new MySqlServerVersion(new Version(8, 0, 46)))
    .AddInterceptors(new HttpNoNetwork(), rows));
builder.Services.AddPermissionAuthorization();
builder.Services.AddAuthorization(options => options.AddFirstBatchPermissionPolicies());
// Policies/endpoints exist only in this test executable, never in API Program.cs.
builder.Services.AddAuthorization(options => options.AddPermissionPolicy("TestOnly:read", "case.read"));
builder.Services.AddControllers().AddApplicationPart(typeof(WeatherForecastController).Assembly);
await using var app = builder.Build();
app.UseMiddleware<PermissionLookupExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<StaffCookieCsrfMiddleware>();
app.UseAuthorization();
app.MapGet("/test/protected", () => Results.Ok(new { value = "allowed" })).RequireAuthorization("TestOnly:read");
app.MapGet("/test/auth-only", () => Results.Ok()).RequireAuthorization();
app.MapGet("/test/public", () => Results.Ok());
var firstBatchCodes = new[] { "case.status.update", "case.status_history.read", "case.read", "questionnaire.task.read" };
for (var index = 0; index < firstBatchCodes.Length; index++)
    app.MapGet($"/test/policies/{index}", () => Results.Ok()).RequireAuthorization("Permission:" + firstBatchCodes[index]);
app.MapControllers();
await app.StartAsync();
using var handler = new HttpClientHandler
{
    UseProxy = false,
    UseCookies = false,
    AllowAutoRedirect = false,
    ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
        presented?.Thumbprint == certificate.Thumbprint
};
using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
int passed = 0, failed = 0;
async Task Test(string label, Func<Task<bool>> check)
{
    try
    {
        if (!await check()) throw new InvalidOperationException();
        passed++;
        Console.WriteLine("PASS " + label);
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"FAIL {label}: {error.GetType().Name}");
    }
}
string Token(Guid id, bool expired = false, string issuer = "offline-test",
    string audience = "offline-test", SecurityKey? signingKey = null)
{
    var now = DateTime.UtcNow;
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
        new[] { new Claim("userId", id.ToString()), new Claim("role", "ADMIN"), new Claim("permission", "case.read") },
        notBefore: now.AddMinutes(-10), expires: expired ? now.AddMinutes(-1) : now.AddMinutes(5),
        signingCredentials: new SigningCredentials(signingKey ?? key, SecurityAlgorithms.HmacSha256)));
}
Task<HttpResponseMessage> Get(string path, string? token = null) =>
    Send(HttpMethod.Get, path, token is null ? null : $"Bearer {token}");

async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? authorization = null,
    string? cookie = null, string? origin = null, string? csrf = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
    if (cookie is not null) request.Headers.Add("Cookie", cookie);
    if (origin is not null) request.Headers.Add("Origin", origin);
    if (csrf is not null) request.Headers.Add("X-EarlyCare-CSRF", csrf);
    return await client.SendAsync(request);
}

string StaffCookie(string token) => $"{StaffSessionCookieDefaults.Name}={token}";

await Test("valid staff cookie authenticates through the Bearer handler", async () =>
{
    using var response = await Send(HttpMethod.Get, "/test/protected", cookie: StaffCookie(Token(allowedId)));
    return response.StatusCode == HttpStatusCode.OK;
});
foreach (var header in new[] { "Bearer malformed", "Basic unsupported" })
{
    await Test($"explicit {header.Split(' ')[0]} header cannot fall back to valid staff cookie", async () =>
    {
        var before = rows.Reads;
        using var response = await Send(HttpMethod.Get, "/test/protected", header, StaffCookie(Token(allowedId)));
        return response.StatusCode == HttpStatusCode.Unauthorized && rows.Reads == before;
    });
}
await Test("valid explicit Bearer identity takes precedence over cookie permission", async () =>
{
    using var response = await Send(HttpMethod.Get, "/test/protected",
        $"Bearer {Token(Guid.NewGuid())}", StaffCookie(Token(allowedId)));
    return response.StatusCode == HttpStatusCode.Forbidden;
});
foreach (var (label, token) in new[]
{
    ("expired", Token(allowedId, expired: true)),
    ("wrong issuer", Token(allowedId, issuer: "different-issuer")),
    ("wrong audience", Token(allowedId, audience: "different-audience")),
    ("wrong signature", Token(allowedId, signingKey: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))))
})
{
    await Test($"{label} staff cookie JWT returns 401 without querying permissions", async () =>
    {
        var before = rows.Reads;
        using var response = await Send(HttpMethod.Get, "/test/protected", cookie: StaffCookie(token));
        return response.StatusCode == HttpStatusCode.Unauthorized && rows.Reads == before;
    });
}
await Test("anonymous CSRF token and logout endpoints require authentication", async () =>
{
    using var csrf = await Get("/api/auth/csrf");
    using var logout = await Send(HttpMethod.Post, "/api/auth/logout");
    return csrf.StatusCode == HttpStatusCode.Unauthorized && logout.StatusCode == HttpStatusCode.Unauthorized;
});
await Test("real antiforgery rejects missing/wrong token and accepts matching logout token", async () =>
{
    var staffCookie = StaffCookie(Token(allowedId));
    using var csrfResponse = await Send(HttpMethod.Get, "/api/auth/csrf", cookie: staffCookie);
    if (csrfResponse.StatusCode != HttpStatusCode.OK) return false;
    using var json = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
    var requestToken = json.RootElement.GetProperty("requestToken").GetString();
    if (string.IsNullOrWhiteSpace(requestToken)) return false;
    var csrfCookie = ReadCookie(csrfResponse, "__Host-earlycare-csrf");
    if (csrfCookie is null) return false;
    var cookies = $"{staffCookie}; {csrfCookie.Name}={csrfCookie.Value}";
    foreach (var candidate in new[] { null, "invalid-token" })
    {
        using var denied = await Send(HttpMethod.Post, "/api/auth/logout",
            cookie: cookies, origin: trustedOrigin, csrf: candidate);
        if (denied.StatusCode != HttpStatusCode.Forbidden || denied.Headers.Contains("Set-Cookie"))
            return false;
        using var error = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        if (error.RootElement.GetProperty("code").GetString() != "CSRF_VALIDATION_FAILED")
            return false;
    }
    using var logout = await Send(HttpMethod.Post, "/api/auth/logout",
        cookie: cookies, origin: trustedOrigin, csrf: requestToken);
    var deleted = ReadCookie(logout, StaffSessionCookieDefaults.Name);
    return logout.StatusCode == HttpStatusCode.NoContent && deleted is not null &&
        deleted.Value.Length == 0 && deleted.Expires <= DateTimeOffset.UtcNow &&
        deleted.Secure && deleted.HttpOnly && deleted.SameSite == Microsoft.Net.Http.Headers.SameSiteMode.Strict &&
        deleted.Path == "/" && !deleted.Domain.HasValue;
});
await Test("invalid explicit header with staff cookie cannot bypass logout CSRF", async () =>
{
    using var response = await Send(HttpMethod.Post, "/api/auth/logout",
        "Bearer malformed", StaffCookie(Token(allowedId)), trustedOrigin);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return response.StatusCode == HttpStatusCode.Forbidden && !response.Headers.Contains("Set-Cookie") &&
        json.RootElement.GetProperty("code").GetString() == "CSRF_VALIDATION_FAILED";
});
await Test("validated explicit Bearer remains compatible with logout without a CSRF token", async () =>
{
    using var response = await Send(HttpMethod.Post, "/api/auth/logout",
        $"Bearer {Token(allowedId)}", StaffCookie(Token(Guid.NewGuid())));
    return response.StatusCode == HttpStatusCode.NoContent;
});
await Test("fixture has the current Task read permission", async () =>
{
    var policyIndex = Array.IndexOf(firstBatchCodes, "questionnaire.task.read");
    using var response = await Get($"/test/policies/{policyIndex}", Token(allowedId));
    return response.StatusCode == HttpStatusCode.OK;
});
foreach (var suffix in new[] { "tasks", "tasks/completion" })
{
    await Test($"{suffix} denies outside the Development gate before task query", async () =>
    {
        var before = rows.Reads;
        var caseId = EarlyInterventionCare.Api.Development.CoreDevelopmentSeed.CaseId;
        using var response = await Send(HttpMethod.Get, $"/api/cases/{caseId:D}/{suffix}",
            cookie: StaffCookie(Token(allowedId)));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return response.StatusCode == HttpStatusCode.Forbidden && rows.Reads == before + 1 &&
            json.RootElement.GetProperty("code").GetString() == "CASE_ACCESS_NOT_INTEGRATED";
    });
}

static Microsoft.Net.Http.Headers.SetCookieHeaderValue? ReadCookie(HttpResponseMessage response, string name) =>
    response.Headers.TryGetValues("Set-Cookie", out var values)
        ? Microsoft.Net.Http.Headers.SetCookieHeaderValue.ParseList(values.ToList()).SingleOrDefault(cookie => cookie.Name == name)
        : null;
await Test("unauthenticated returns 401 with Bearer challenge and no query", async () =>
{
    var before = rows.Reads;
    using var response = await Get("/test/protected");
    return response.StatusCode == HttpStatusCode.Unauthorized && response.Headers.WwwAuthenticate.Any(x => x.Scheme == "Bearer") && rows.Reads == before;
});
await Test("authenticated without permission returns 403 despite ADMIN/permission claims", async () =>
{
    using var response = await Get("/test/protected", Token(Guid.NewGuid()));
    return response.StatusCode == HttpStatusCode.Forbidden;
});
await Test("authenticated with current permission returns 200", async () =>
{
    using var response = await Get("/test/protected", Token(allowedId));
    return response.StatusCode == HttpStatusCode.OK;
});
await Test("raw database failure becomes fixed safe JSON 503", async () =>
{
    using var response = await Get("/test/protected", Token(faultId));
    var body = await response.Content.ReadAsStringAsync();
    using var json = JsonDocument.Parse(body);
    return response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.CacheControl?.NoStore == true &&
        response.Content.Headers.ContentType?.MediaType == "application/json" &&
        json.RootElement.EnumerateObject().Count() == 2 &&
        json.RootElement.GetProperty("code").GetString() == "AUTHORIZATION_UNAVAILABLE" &&
        json.RootElement.GetProperty("message").GetString() == "授權服務暫時無法使用。" &&
        !body.Contains("SYNTHETIC_PRIVATE") && !body.Contains("SELECT") && !body.Contains("StackTrace") &&
        !body.Contains("invalid.invalid");
});
await Test("public endpoint unchanged with no permission query", async () =>
{
    var before = rows.Reads;
    using var response = await Get("/test/public");
    return response.StatusCode == HttpStatusCode.OK && rows.Reads == before;
});
await Test("existing WeatherForecast controller remains public", async () =>
{
    var before = rows.Reads;
    using var response = await Get("/WeatherForecast");
    return response.StatusCode == HttpStatusCode.OK && rows.Reads == before;
});
await Test("plain Authorize stays authenticated-only without permission lookup", async () =>
{
    var before = rows.Reads;
    using var response = await Get("/test/auth-only", Token(faultId));
    return response.StatusCode == HttpStatusCode.OK && rows.Reads == before;
});
await Test("plain Authorize still returns 401 for anonymous", async () =>
{
    using var response = await Get("/test/auth-only");
    return response.StatusCode == HttpStatusCode.Unauthorized;
});
await Test("expired JWT returns 401 without querying", async () =>
{
    var before = rows.Reads;
    using var response = await Get("/test/protected", Token(allowedId, true));
    return response.StatusCode == HttpStatusCode.Unauthorized && rows.Reads == before;
});
await Test("DefaultPolicy and FallbackPolicy retain framework defaults", () =>
{
    var options = app.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
    return Task.FromResult(options.FallbackPolicy is null &&
        options.DefaultPolicy.Requirements.Count == 1 &&
        !options.DefaultPolicy.Requirements.OfType<PermissionRequirement>().Any());
});
await Test("unrelated exception propagates without a 503 rewrite", async () =>
{
    var context = new DefaultHttpContext();
    var original = new InvalidOperationException("synthetic unrelated fault");
    var middleware = new PermissionLookupExceptionMiddleware(_ => throw original);
    try { await middleware.InvokeAsync(context); return false; }
    catch (InvalidOperationException error) { return ReferenceEquals(error, original) && context.Response.StatusCode != 503; }
});
await Test("aborted request is not rewritten to 503", async () =>
{
    var context = new DefaultHttpContext();
    context.RequestAborted = new CancellationToken(true);
    var middleware = new PermissionLookupExceptionMiddleware(_ => throw new PermissionLookupException());
    try { await middleware.InvokeAsync(context); return false; }
    catch (PermissionLookupException) { return context.Response.StatusCode != 503; }
});
await Test("OperationCanceledException remains cancellation", async () =>
{
    var context = new DefaultHttpContext();
    var middleware = new PermissionLookupExceptionMiddleware(_ => throw new OperationCanceledException());
    try { await middleware.InvokeAsync(context); return false; }
    catch (OperationCanceledException) { return context.Response.StatusCode != 503; }
});
await Test("started response is not cleared or rewritten", async () =>
{
    var context = new DefaultHttpContext();
    context.Features.Set<IHttpResponseFeature>(new StartedResponse());
    var middleware = new PermissionLookupExceptionMiddleware(_ => throw new PermissionLookupException());
    try { await middleware.InvokeAsync(context); return false; }
    catch (PermissionLookupException) { return context.Response.StatusCode == 202; }
});
await Test("actual Program registers RBAC and preserves middleware order without test policy", async () =>
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EarlyInterventionCare.Api", "Program.cs")))
        directory = directory.Parent;
    if (directory is null) return false;
    var program = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "EarlyInterventionCare.Api", "Program.cs"));
    var boundary = program.IndexOf("app.UseMiddleware<PermissionLookupExceptionMiddleware>();", StringComparison.Ordinal);
    var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
    var csrf = program.IndexOf("app.UseMiddleware<StaffCookieCsrfMiddleware>();", StringComparison.Ordinal);
    var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
    var limiter = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
    return program.Contains("builder.Services.AddPermissionAuthorization();") &&
        !program.Contains("TestOnly:") && program.Contains("builder.Services.AddAuthorization(options => options.AddFirstBatchPermissionPolicies());") &&
        boundary >= 0 && boundary < authentication && authentication < csrf && csrf < authorization && authorization < limiter;
});
for (var index = 0; index < firstBatchCodes.Length; index++)
{
    var routeIndex = index;
    await Test($"first batch {firstBatchCodes[index]} lookup failure remains safe HTTP 503", async () =>
    {
        using var response = await Get($"/test/policies/{routeIndex}", Token(faultId));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        return response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.CacheControl?.NoStore == true &&
            json.RootElement.GetProperty("code").GetString() == "AUTHORIZATION_UNAVAILABLE" &&
            !body.Contains("SYNTHETIC_PRIVATE") && !body.Contains("SELECT") && !body.Contains("StackTrace");
    });
}
await app.StopAsync();
Console.WriteLine($"RBAC HTTP: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class StartedResponse : HttpResponseFeature
{
    public StartedResponse() { StatusCode = 202; }
    public override bool HasStarted => true;
}
sealed class UnusedStaffLogin : IAuthenticationService
{
    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected login in HTTP authorization harness.");
    public Task<LoginResponse?> LoginStaffAsync(StaffLoginRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected staff login in HTTP authorization harness.");
    public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected user lookup in HTTP authorization harness.");
}
sealed class HttpNoNetwork : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result) => throw new InvalidOperationException("Network forbidden");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
}
sealed class HttpDatabaseFault : DbException
{
    public HttpDatabaseFault() : base("SYNTHETIC_PRIVATE SELECT sensitive_statement") { }
}
sealed class HttpRows(Guid allowed, Guid fault) : DbCommandInterceptor
{
    public int Reads { get; private set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Reads++;
        var id = Guid.Parse(command.Parameters.Cast<DbParameter>().Single().Value!.ToString()!);
        if (id == fault) throw new HttpDatabaseFault();
        if (!command.CommandText.Contains("FROM `users`"))
            throw new InvalidOperationException("Unexpected offline command");
        var table = new DataTable();
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("name", typeof(string));
        if (command.CommandText.Contains("INNER JOIN `permissions`"))
        {
            if (id == allowed)
            {
                table.Rows.Add("ACTIVE", "case.read");
                table.Rows.Add("ACTIVE", "questionnaire.task.read");
            }
        }
        else if (command.CommandText.Contains("INNER JOIN `roles`"))
        {
            // Current account/role check in the questionnaire controller, before
            // the Development gate. Task or Case queries remain unexpected here.
            if (id == allowed) table.Rows.Add("ACTIVE", "CASE_MANAGER");
        }
        else throw new InvalidOperationException("Unexpected offline user query");
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }
}
