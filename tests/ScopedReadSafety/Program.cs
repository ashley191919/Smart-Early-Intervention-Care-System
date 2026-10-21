using System.Security.Claims;
using EarlyInterventionCare.Api.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Text.Encodings.Web;
using System.Net;
using System.Text.Json;

int passed = 0, failed = 0;
void Check(bool value, string name) { if (value) { passed++; Console.WriteLine("PASS " + name); } else { failed++; Console.WriteLine("FAIL " + name); } }
ServiceCollection Services() { var s = new ServiceCollection(); s.AddLogging(); s.AddRouting(); return s; }
foreach (var name in new[] { "case.read", "questionnaire.task.read" })
foreach (var late in new[] { false, true })
foreach (var kind in new[] { "ordinary", "wrong-permission", "wrong-case", "no-authenticated", "extra-permission", "scheme" })
{
    var s = Services();
    void Collision() => s.AddAuthorization(o => o.AddPolicy("Permission:" + name, b =>
    {
        if (kind != "no-authenticated") b.RequireAuthenticatedUser();
        if (kind != "ordinary") b.AddRequirements(new PermissionRequirement(kind == "wrong-permission" ? "other.read" : kind == "wrong-case" ? name.ToUpperInvariant() : name));
        if (kind == "extra-permission") b.AddRequirements(new PermissionRequirement("other.read"));
        if (kind == "scheme") b.AuthenticationSchemes.Add("unreviewed-scheme");
    }));
    if (!late) Collision();
    s.AddScopedCaseTaskReads();
    if (late) Collision();
    using var p = s.BuildServiceProvider();
    var rejected = false;
    try { _ = p.GetRequiredService<IOptions<AuthorizationOptions>>().Value; }
    catch (Exception e) when (e is InvalidOperationException or OptionsValidationException) { rejected = true; }
    Check(rejected, $"reject {name}/{kind}/late={late}");
}
foreach (var name in new[] { "case.read", "questionnaire.task.read" })
{
    var s = Services(); s.AddScopedCaseTaskReads();
    s.PostConfigure<AuthorizationOptions>(o => o.AddPolicy("Permission:" + name, b => b.RequireAuthenticatedUser()));
    using var p = s.BuildServiceProvider(); var rejected = false;
    try { _ = p.GetRequiredService<IOptions<AuthorizationOptions>>().Value; }
    catch (Exception e) when (e is InvalidOperationException or OptionsValidationException) { rejected = true; }
    Check(rejected, "late PostConfigure cannot replace " + name);
}
{
    var s = Services(); s.AddScopedCaseTaskReads(); s.AddScopedCaseTaskReads();
    s.AddSingleton<IPermissionService>(new Permissions());
    using var p = s.BuildServiceProvider();
    var options = p.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
    foreach (var name in new[] { "case.read", "questionnaire.task.read" })
    {
        var policy = options.GetPolicy("Permission:" + name)!;
        Check(policy.Requirements.OfType<PermissionRequirement>().Single().Permission == name &&
            policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Count() == 1, "idempotent exact policy " + name);
    }
    using var scope = p.CreateScope();
    Check(scope.ServiceProvider.GetServices<IAuthorizationHandler>().OfType<PermissionAuthorizationHandler>().Count() == 1, "one original handler");
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", Guid.NewGuid().ToString()), new Claim("role", "ADMIN")], "test"));
    Check(!await scope.ServiceProvider.GetRequiredService<ScopedReadAuthorization>().CanReadCaseAsync(principal, Guid.NewGuid()), "missing scope defaults deny despite permission");
}
{
    var s = Services(); var permissions = new Permissions(); var resource = new Scope();
    s.AddSingleton<IPermissionService>(permissions); s.AddSingleton<ICaseTaskReadScope>(resource); s.AddScopedCaseTaskReads();
    using var p = s.BuildServiceProvider(); using var request = p.CreateScope();
    var reader = request.ServiceProvider.GetRequiredService<ScopedReadAuthorization>();
    var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", Guid.NewGuid().ToString())], "test"));
    foreach (var permission in new[] { false, true }) foreach (var allowed in new[] { false, true })
    {
        permissions.Allowed = permission; resource.Allowed = allowed; resource.Calls = 0;
        Check(await reader.CanReadTaskAsync(user, Guid.NewGuid(), Guid.NewGuid()) == (permission && allowed), $"permission AND scope {permission}/{allowed}");
        Check(resource.Calls == (permission ? 1 : 0), "permission denial short-circuits scope");
    }
}
// Test-only loopback endpoints, synthetic authentication and scopes; no business Controller/Program changes.
{
    var caseId = Guid.NewGuid(); var taskId = Guid.NewGuid(); var permissions = new Permissions();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddAuthentication("synthetic").AddScheme<AuthenticationSchemeOptions, SyntheticAuthentication>("synthetic", _ => { });
    builder.Services.AddSingleton<IPermissionService>(permissions);
    builder.Services.AddSingleton<ICaseTaskReadScope>(new HttpScope(caseId, taskId));
    builder.Services.AddScopedCaseTaskReads();
    await using var app = builder.Build();
    app.UseMiddleware<PermissionLookupExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization();
    app.MapGet("/scope/{caseId:guid}/{taskId:guid}", async (HttpContext http, Guid caseId, Guid taskId, ScopedReadAuthorization reads) =>
        await reads.CanReadTaskAsync(http.User, caseId, taskId, http.RequestAborted) ? Results.Ok() : Results.StatusCode(403)).RequireAuthorization();
    await app.StartAsync();
    try
    {
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        async Task<HttpStatusCode> Status(Guid c, Guid t) { using var response = await client.GetAsync($"/scope/{c}/{t}"); return response.StatusCode; }
        Check(await Status(caseId, taskId) == HttpStatusCode.Unauthorized, "HTTP anonymous 401");
        client.DefaultRequestHeaders.Add("Authorization", "Synthetic test-only");
        permissions.Allowed = false;
        Check(await Status(caseId, taskId) == HttpStatusCode.Forbidden, "HTTP scope alone cannot allow");
        permissions.Allowed = true;
        Check(await Status(Guid.NewGuid(), taskId) == HttpStatusCode.Forbidden, "HTTP cross Case denied");
        Check(await Status(caseId, Guid.NewGuid()) == HttpStatusCode.Forbidden, "HTTP cross Task denied");
        Check(await Status(caseId, taskId) == HttpStatusCode.OK, "HTTP permission plus scope allows");
        permissions.Fail = true;
        using var fault = await client.GetAsync($"/scope/{caseId}/{taskId}");
        using var json = JsonDocument.Parse(await fault.Content.ReadAsStringAsync());
        Check(fault.StatusCode == HttpStatusCode.ServiceUnavailable && json.RootElement.GetProperty("code").GetString() == "AUTHORIZATION_UNAVAILABLE", "HTTP permission failure safe 503");
    }
    finally { await app.StopAsync(); }
}
Console.WriteLine($"ScopedReadSafety: {passed} passed, {failed} failed; 38 unit/DI and 6 loopback HTTP checks; no DB, synthetic identity/scope.");
return failed == 0 ? 0 : 1;

sealed class Permissions : IPermissionService
{
    public bool Allowed = true, Fail;
    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Allowed ? ["case.read", "questionnaire.task.read"] : []);
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    { if (Fail) throw new PermissionLookupException(); return Task.FromResult(Allowed); }
}
sealed class HttpScope(Guid allowedCase, Guid allowedTask) : ICaseTaskReadScope
{
    public Task<bool> CanReadCaseAsync(ClaimsPrincipal principal, Guid caseId, CancellationToken ct) => Task.FromResult(caseId == allowedCase);
    public Task<bool> CanReadTaskAsync(ClaimsPrincipal principal, Guid caseId, Guid taskId, CancellationToken ct) => Task.FromResult(caseId == allowedCase && taskId == allowedTask);
}
sealed class SyntheticAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(Request.Headers.Authorization.ToString() == "Synthetic test-only"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
}
sealed class Scope : ICaseTaskReadScope
{
    public bool Allowed; public int Calls;
    public Task<bool> CanReadCaseAsync(ClaimsPrincipal principal, Guid caseId, CancellationToken ct) { Calls++; return Task.FromResult(Allowed); }
    public Task<bool> CanReadTaskAsync(ClaimsPrincipal principal, Guid caseId, Guid taskId, CancellationToken ct) { Calls++; return Task.FromResult(Allowed); }
}
