using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// Real loopback HTTP/JWT/authorization pipeline. No application Program startup,
// secrets or database connection. SQL command results are an explicitly offline double.
var allowedId = Guid.NewGuid();
var faultId = Guid.NewGuid();
var rows = new HttpRows(allowedId, faultId);
var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    EnvironmentName = "Production", Args = Array.Empty<string>()
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection();
builder.Logging.ClearProviders(); // Do not log JWTs or synthetic database diagnostics.
builder.WebHost.UseUrls("http://127.0.0.1:0");
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
app.UseAuthorization();
app.MapGet("/test/protected", () => Results.Ok(new { value = "allowed" })).RequireAuthorization("TestOnly:read");
app.MapGet("/test/auth-only", () => Results.Ok()).RequireAuthorization();
app.MapGet("/test/public", () => Results.Ok());
var firstBatchCodes = new[] { "case.status.update", "case.status_history.read", "case.read", "questionnaire.task.read" };
for (var index = 0; index < firstBatchCodes.Length; index++)
    app.MapGet($"/test/policies/{index}", () => Results.Ok()).RequireAuthorization("Permission:" + firstBatchCodes[index]);
app.MapControllers();
await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
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
string Token(Guid id, bool expired = false)
{
    var now = DateTime.UtcNow;
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("offline-test", "offline-test",
        new[] { new Claim("userId", id.ToString()), new Claim("role", "ADMIN"), new Claim("permission", "case.read") },
        notBefore: now.AddMinutes(-10), expires: expired ? now.AddMinutes(-1) : now.AddMinutes(5),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
}
async Task<HttpResponseMessage> Get(string path, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await client.SendAsync(request);
}
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
    var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
    var limiter = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
    return program.Contains("builder.Services.AddPermissionAuthorization();") &&
        !program.Contains("TestOnly:") && program.Contains("builder.Services.AddAuthorization(options => options.AddFirstBatchPermissionPolicies());") &&
        boundary >= 0 && boundary < authentication && authentication < authorization && authorization < limiter;
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
        if (!command.CommandText.Contains("FROM `users`") || !command.CommandText.Contains("INNER JOIN `permissions`"))
            throw new InvalidOperationException("Unexpected offline command");
        var table = new DataTable();
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("name", typeof(string));
        if (id == allowed) table.Rows.Add("ACTIVE", "case.read");
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }
}
