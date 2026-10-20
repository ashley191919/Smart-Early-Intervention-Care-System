using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Security;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

// This host has no database registration or access. Fakes are confined to this executable.
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddControllers().AddApplicationPart(typeof(CaseStatusController).Assembly);
builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
builder.Services.AddSingleton<IPermissionService, FakePermission>();
builder.Services.AddPermissionAuthorization();
builder.Services.AddAuthorization(o => o.AddFirstBatchPermissionPolicies());
builder.Services.AddSingleton(new StaffSessionOptions(new[] { "https://trusted.example" }));
builder.Services.AddAntiforgery(o => o.HeaderName = "X-EarlyCare-CSRF");
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<FakeStore>();
builder.Services.AddSingleton<ICaseStatusStore>(s => s.GetRequiredService<FakeStore>());
builder.Services.AddSingleton<ICaseStatusHistoryStore>(s => s.GetRequiredService<FakeStore>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICaseStatusActorContext, HttpContextCaseStatusActorContext>();
builder.Services.AddSingleton<ICaseStatusAccessAuthorizer, FakeCaseAccess>();
builder.Services.AddSingleton<ICaseStatusHistoryAccessAuthorizer, FakeCaseAccess>();
builder.Services.AddScoped<CaseStatusService>();
builder.Services.AddScoped<CaseStatusHistoryQueryService>();
var app = builder.Build();
app.UseMiddleware<PermissionLookupExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<StaffCookieCsrfMiddleware>();
app.UseAuthorization();
app.MapControllers();
await app.StartAsync();
try
{
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    var store = app.Services.GetRequiredService<FakeStore>();
    var allowed = FakeCaseAccess.Allowed;
    var denied = Guid.NewGuid();
    var checks = 0;
    async Task Check(string label, HttpMethod method, Guid id, string? identity, HttpStatusCode expected,
        string? cookie = null, string? origin = null, string? csrf = null, string? expectedCode = null)
    {
        using var request = new HttpRequestMessage(method, $"/api/cases/{id}/" + (method == HttpMethod.Get ? "status-history" : "status"));
        if (method != HttpMethod.Get) request.Content = JsonContent.Create(new { newStatus = "TO_CONTACT", reason = "test" });
        if (identity is not null) request.Headers.TryAddWithoutValidation("X-Test-Identity", identity);
        if (cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (csrf is not null) request.Headers.TryAddWithoutValidation("X-EarlyCare-CSRF", csrf);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != expected || (expectedCode is not null && !body.Contains(expectedCode, StringComparison.Ordinal)))
            throw new Exception($"{label}: expected {expected}/{expectedCode}, got {response.StatusCode}: {body}");
        checks++; Console.WriteLine("PASS " + label);
    }
    foreach (var method in new[] { HttpMethod.Get, HttpMethod.Patch })
    {
        await Check($"{method} anonymous", method, allowed, null, HttpStatusCode.Unauthorized);
        await Check($"{method} missing permission", method, allowed, "none", HttpStatusCode.Forbidden);
        await Check($"{method} identity A history-read only", method, allowed, "A", method == HttpMethod.Get ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await Check($"{method} identity B case denied", method, denied, "B", HttpStatusCode.Forbidden);
        await Check($"{method} permission failure", method, allowed, "fault", HttpStatusCode.ServiceUnavailable, expectedCode: "AUTHORIZATION_UNAVAILABLE");
    }
    if (store.Case.CaseStatus != "NEW" || store.Writes != 0) throw new Exception("Denied PATCH must not write case or history");
    checks++;
    await Check("PATCH identity B test scope allowed", HttpMethod.Patch, allowed, "B", HttpStatusCode.OK);
    if (store.Case.CaseStatus != "TO_CONTACT" || store.Writes != 1) throw new Exception("Expected exactly one authorized write");
    checks++;
    await Check("cookie missing origin", HttpMethod.Patch, allowed, "B", HttpStatusCode.Forbidden, "__Host-earlycare-staff=test", expectedCode: "ORIGIN_REQUIRED");
    await Check("cookie wrong origin", HttpMethod.Patch, allowed, "B", HttpStatusCode.Forbidden, "__Host-earlycare-staff=test", "https://evil.example", expectedCode: "INVALID_ORIGIN");
    await Check("cookie missing csrf", HttpMethod.Patch, allowed, "B", HttpStatusCode.Forbidden, "__Host-earlycare-staff=test", "https://trusted.example", expectedCode: "CSRF_VALIDATION_FAILED");
    await Check("cookie invalid csrf", HttpMethod.Patch, allowed, "B", HttpStatusCode.Forbidden, "__Host-earlycare-staff=test", "https://trusted.example", "invalid", "CSRF_VALIDATION_FAILED");
    Console.WriteLine($"PASS: {checks} medical RBAC HTTP checks; no database.");
}
finally { await app.StopAsync(); await app.DisposeAsync(); }

sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers["X-Test-Identity"].ToString();
        if (string.IsNullOrEmpty(value)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new[] { new Claim("userId", FakePermission.Id(value).ToString()) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
sealed class FakePermission : IPermissionService
{
    public static Guid Id(string value) => value switch { "A" => Guid.Parse("00000000-0000-0000-0000-000000000001"), "B" => Guid.Parse("00000000-0000-0000-0000-000000000002"), "fault" => Guid.Parse("00000000-0000-0000-0000-000000000003"), _ => Guid.Parse("00000000-0000-0000-0000-000000000004") };
    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<bool> HasPermissionAsync(Guid id, string permission, CancellationToken ct = default)
    {
        if (id == Id("fault")) throw new PermissionLookupException();
        return Task.FromResult((id == Id("A") && permission == "case.status_history.read") ||
            (id == Id("B") && permission is "case.status_history.read" or "case.status.update"));
    }
}
sealed class FakeCaseAccess : ICaseStatusAccessAuthorizer, ICaseStatusHistoryAccessAuthorizer
{
    public static readonly Guid Allowed = Guid.Parse("10000000-0000-4000-8000-000000000001");
    public Task<CaseStatusAccessDecision> AuthorizeChangeAsync(ClaimsPrincipal user, Guid caseId, CancellationToken ct) => Task.FromResult(caseId == Allowed ? CaseStatusAccessDecision.Granted() : CaseStatusAccessDecision.Denied("DENIED", "Denied"));
    public Task<CaseStatusHistoryAccessDecision> AuthorizeReadAsync(ClaimsPrincipal user, Guid caseId, CancellationToken ct) => Task.FromResult(caseId == Allowed ? CaseStatusHistoryAccessDecision.Granted(false) : CaseStatusHistoryAccessDecision.Denied("DENIED", "Denied"));
}
sealed class FakeStore : ICaseStatusStore, ICaseStatusHistoryStore
{
    public CaseRecord Case { get; } = new() { CaseId = FakeCaseAccess.Allowed, CaseStatus = "NEW" };
    public int Writes { get; private set; }
    public Task<bool> IsActiveUserAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
    public Task<ICaseStatusTransaction?> OpenAsync(Guid id, CancellationToken ct) => Task.FromResult<ICaseStatusTransaction?>(id == Case.CaseId ? new Transaction(this) : null);
    public Task<CaseStatusHistoryPageData?> GetPageAsync(Guid id, int page, int size, CancellationToken ct) => Task.FromResult<CaseStatusHistoryPageData?>(new(id, 0, Array.Empty<CaseStatusHistoryData>()));
    sealed class Transaction(FakeStore store) : ICaseStatusTransaction
    {
        public CaseRecord Case => store.Case;
        public void AddHistory(CaseStatusHistory history) => store.Writes++;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
