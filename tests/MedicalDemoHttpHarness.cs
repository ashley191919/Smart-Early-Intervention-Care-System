using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.DTOs.Authentication;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using IAuthenticationService = EarlyInterventionCare.Api.Services.Authentication.IAuthenticationService;

// Offline HTTP only: actual demo routes, environment boundary and static middleware;
// test-only identity/current-account substitutes. No DbContext, secrets, JWT key or MySQL.
var root = Path.GetFullPath("EarlyInterventionCare.Api/wwwroot");
var paths = new[] { "/api/medical-demo/availability", "/api/medical-demo/cases/DEMO-001/tasks",
    "/api/medical-demo/cases/DEMO-001/tasks/" + MedicalDemoEndpoints.TaskId };
var checks = 0;
foreach (var environment in new[] { Environments.Development, Environments.Production })
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, WebRootPath = root });
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddAuthentication("isolated").AddScheme<AuthenticationSchemeOptions, IsolatedIdentity>("isolated", _ => { });
    builder.Services.AddAuthorization();
    var accounts = new IsolatedAccounts();
    builder.Services.AddSingleton<IAuthenticationService>(accounts);
    var app = builder.Build();
    app.UseMedicalDemoBoundary();
    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMedicalDemoEndpoints();
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        async Task<string> Check(string name, string path, string? identity, HttpStatusCode expected, HttpMethod? method = null)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
            if (identity is not null) request.Headers.Add("X-Isolated-Identity", identity);
            request.Headers.Add("X-Role", "CASE_MANAGER"); // Must never grant access.
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != expected) throw new Exception($"{environment} {name}: {response.StatusCode}, {body}");
            if (path.StartsWith("/api/medical-demo") || path.StartsWith("/js/medical-demo-fixtures"))
                if (response.Headers.CacheControl?.NoStore != true) throw new Exception("Demo responses must not be cached");
            if (expected != HttpStatusCode.OK && body.Contains("questions", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Rejected response exposed answers");
            checks++; Console.WriteLine($"PASS {environment}: {name}"); return body;
        }
        foreach (var path in paths)
        {
            foreach (var identity in new string?[] { null, "expired", "inactive", "parent", "teacher", "admin", "manager", "staff" })
            {
                var expected = environment != Environments.Development ? HttpStatusCode.NotFound : identity switch
                {
                    null or "expired" or "inactive" => HttpStatusCode.Unauthorized,
                    "parent" or "teacher" or "admin" => HttpStatusCode.Forbidden,
                    _ => HttpStatusCode.OK
                };
                await Check($"{path} / {identity ?? "anonymous"}", path + "?medicalDemo=1&role=CASE_MANAGER", identity, expected);
            }
        }
        foreach (var identity in new string?[] { null, "manager" })
        {
            await Check("retired public fixture", "/js/medical-demo-fixtures.js?v=old", identity, HttpStatusCode.NotFound);
            await Check("private fixture path", "/Development/MedicalDemoFixture.json", identity, HttpStatusCode.NotFound);
        }
        if (environment == Environments.Production)
        {
            if (accounts.Reads != 0) throw new Exception("Production must reject before account lookup");
            checks++;
        }
        else
        {
            var detail = await Check("complete synthetic response", paths[2], "manager", HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(detail);
            var answer = doc.RootElement;
            if (answer.GetProperty("caseId").GetString() != "DEMO-001" || !answer.GetProperty("isDemo").GetBoolean() ||
                answer.GetProperty("draft").GetProperty("questions").GetArrayLength() != 26 ||
                answer.GetProperty("draft").GetProperty("answers").EnumerateObject().Count() != 26 ||
                answer.TryGetProperty("scoring", out _)) throw new Exception("Unexpected synthetic fixture");
            checks++;
            var list = await Check("list omits answers", paths[1], "staff", HttpStatusCode.OK);
            if (list.Contains("draft") || list.Contains("answers")) throw new Exception("List contains answers");
            checks++;
            if (await Check("empty case", "/api/medical-demo/cases/DEMO-024/tasks", "manager", HttpStatusCode.OK) != "[]")
                throw new Exception("DEMO-024 must be empty");
            await Check("foreign case", "/api/medical-demo/cases/DEMO-024/tasks/" + MedicalDemoEndpoints.TaskId, "manager", HttpStatusCode.NotFound);
            await Check("unknown task", "/api/medical-demo/cases/DEMO-001/tasks/unknown", "manager", HttpStatusCode.NotFound);
            await Check("lookup failure safe 503", paths[2], "fault", HttpStatusCode.ServiceUnavailable);
            await Check("malformed identity", paths[2], "malformed", HttpStatusCode.Unauthorized);
            accounts.ManagerRole = "PARENT";
            await Check("current role wins over stale medical claim", paths[2], "manager", HttpStatusCode.Forbidden);
            accounts.ManagerRole = "CASE_MANAGER";
            await Check("no write endpoint", paths[2], "manager", HttpStatusCode.MethodNotAllowed, HttpMethod.Post);
        }
    }
    finally { await app.StopAsync(); await app.DisposeAsync(); }
}
Console.WriteLine($"PASS: {checks} medical demo HTTP checks; Development/Production; no database.");

sealed class IsolatedIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers["X-Isolated-Identity"].ToString();
        if (string.IsNullOrEmpty(identity)) return Task.FromResult(AuthenticateResult.NoResult());
        if (identity == "expired") return Task.FromResult(AuthenticateResult.Fail("Expired isolated session"));
        var id = IsolatedAccounts.Id(identity);
        var claims = new[] { new Claim("userId", identity == "malformed" ? "invalid" : id.ToString()), new Claim("role", "CASE_MANAGER") };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
sealed class IsolatedAccounts : IAuthenticationService
{
    public int Reads { get; private set; }
    public string ManagerRole { get; set; } = "CASE_MANAGER";
    public static Guid Id(string name) => Guid.Parse("00000000-0000-0000-0000-" + (name switch
    { "manager" => "000000000001", "staff" => "000000000002", "parent" => "000000000003", "teacher" => "000000000004",
        "admin" => "000000000005", "inactive" => "000000000006", "fault" => "000000000007", _ => "000000000008" }));
    public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid id, CancellationToken ct = default)
    {
        Reads++;
        if (id == Id("fault")) throw new TimeoutException("isolated lookup failure");
        if (id == Id("inactive")) return Task.FromResult<CurrentUserResponse?>(null);
        var role = id == Id("manager") ? ManagerRole : id == Id("staff") ? "MEDICAL_STAFF" :
            id == Id("parent") ? "PARENT" : id == Id("teacher") ? "TEACHER" : "ADMIN";
        return Task.FromResult<CurrentUserResponse?>(new() { UserId = id, Username = "isolated", Role = role });
    }
    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<LoginResponse?> LoginStaffAsync(StaffLoginRequest request, CancellationToken ct = default) => throw new NotSupportedException();
}
