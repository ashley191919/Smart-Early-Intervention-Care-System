using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.Services.Authentication;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

// These settings are scoped to the harness process and are present before Program reads configuration.
const string environment = "G4BVerification";
const string origin = "https://127.0.0.1";
const string issuer = "g4b-offline";
const string audience = "g4b-staff";
const string offlineConnection = "Server=127.0.0.1;Port=1;Database=g4b_offline;User=offline;Pooling=False;";
var isolatedRoot = Path.Combine(AppContext.BaseDirectory, "g4b-empty-content");
Directory.CreateDirectory(isolatedRoot);
if (Directory.EnumerateFileSystemEntries(isolatedRoot).Any())
    throw new InvalidOperationException("The harness content root must be empty.");
foreach (string name in Environment.GetEnvironmentVariables().Keys)
{
    if (Regex.IsMatch(name, @"^(ASPNETCORE_|DOTNET_|Jwt__|ConnectionStrings__|StaffSession__|Kestrel__|StaffLoginRateLimit__)",
        RegexOptions.IgnoreCase))
        Environment.SetEnvironmentVariable(name, null);
}
Directory.SetCurrentDirectory(isolatedRoot);
Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
Environment.SetEnvironmentVariable("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");
Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", isolatedRoot);
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_EARLYINTERVENTIONCARE_API", isolatedRoot);
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", offlineConnection);
var key = RandomNumberGenerator.GetBytes(32);
Environment.SetEnvironmentVariable("Jwt__SigningKey", Convert.ToBase64String(key));
Environment.SetEnvironmentVariable("Jwt__Issuer", issuer);
Environment.SetEnvironmentVariable("Jwt__Audience", audience);
Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "30");
Environment.SetEnvironmentVariable("StaffSession__TrustedOrigins__0", origin);

var rows = new IdentityRows(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
await using var factory = new ProductFactory(environment, isolatedRoot, offlineConnection, rows);
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}

HttpClient CreateClient()
{
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(origin),
        AllowAutoRedirect = false
    });
    client.DefaultRequestHeaders.Add("Origin", origin);
    return client;
}

using var anonymous = CreateClient();
var hostEnvironment = factory.Services.GetRequiredService<IHostEnvironment>();
Check(hostEnvironment.EnvironmentName == environment && hostEnvironment.ContentRootPath == isolatedRoot,
    "actual Program starts in a dedicated environment under TestServer");
var rootResolutionRejected = false;
try { _ = factory.Services.GetRequiredService<IMedicalTaskCaseReadScope>(); }
catch (InvalidOperationException) { rootResolutionRejected = true; }
Check(rootResolutionRejected, "actual provider rejects resolving the scoped adapter from the root");
using (var scope = factory.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    var adapter = services.GetRequiredService<IMedicalTaskCaseReadScope>();
    Check(adapter is CaseAccessMedicalTaskCaseReadScope &&
        ReferenceEquals(adapter, services.GetRequiredService<IMedicalTaskCaseReadScope>()) &&
        ReferenceEquals(db, services.GetRequiredService<ApplicationDbContext>()) &&
        services.GetRequiredService<CaseAccessService>() is not null &&
        services.GetRequiredService<IQuestionnaireStore>() is EfQuestionnaireStore &&
        services.GetRequiredService<ICaseStatusAccessAuthorizer>() is DenyAllCaseStatusAccessAuthorizer &&
        services.GetRequiredService<ICaseStatusHistoryAccessAuthorizer>() is DenyAllCaseStatusHistoryAccessAuthorizer,
        "actual Scoped registrations resolve alongside both DenyAll authorizers");
    foreach (var type in new[] { typeof(AuthController), typeof(StaffSessionController),
        typeof(QuestionnairesController), typeof(CaseStatusController), typeof(CaseStatusHistoryController) })
        _ = ActivatorUtilities.CreateInstance(services, type);
    Check(db.Database.GetDbConnection().State == ConnectionState.Closed,
        "medical controllers activate with the offline DbContext still closed");
}

using (var response = await anonymous.GetAsync("/api/auth/me"))
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "anonymous me returns 401");

async Task Login(HttpClient client, FixtureUser user)
{
    using var response = await client.PostAsJsonAsync("/api/auth/staff-session",
        new { organizationCode = IdentityRows.OrganizationCode, username = user.Username, password = rows.Password });
    Check(response.StatusCode == HttpStatusCode.OK, $"{user.Role} staff-session returns 200");
    using var json = await ReadJson(response);
    Check(json.RootElement.GetProperty("role").GetString() == user.Role &&
        !json.RootElement.TryGetProperty("accessToken", out _), $"{user.Role} login body contains identity without JWT");
    var cookie = SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
        .Last(item => item.Name == StaffSessionCookieDefaults.Name);
    Check(cookie.Value.Length > 0 && cookie.Secure && cookie.HttpOnly && cookie.Path == "/" &&
        !cookie.Domain.HasValue && cookie.SameSite == Microsoft.Net.Http.Headers.SameSiteMode.Strict,
        $"{user.Role} gets a Secure HttpOnly host-only Strict cookie");
    using var me = await client.GetAsync("/api/auth/me");
    using var profile = await ReadJson(me);
    Check(me.StatusCode == HttpStatusCode.OK &&
        profile.RootElement.GetProperty("userId").GetGuid() == user.Id &&
        profile.RootElement.GetProperty("role").GetString() == user.Role, $"{user.Role} cookie resolves current identity via me");
}

using var manager = CreateClient();
using var staff = CreateClient();
await Login(manager, rows.Manager);
await Login(staff, rows.Staff);

async Task<HttpResponseMessage> MeWithBearer(HttpClient client, string token)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return await client.SendAsync(request);
}

using (var response = await MeWithBearer(manager, "invalid"))
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "invalid explicit Bearer never falls back to a valid cookie");
var expired = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience,
    [new Claim("userId", rows.Manager.Id.ToString("D"))],
    DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-2),
    new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)));
using (var response = await MeWithBearer(anonymous, expired))
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "correctly signed expired JWT returns 401");

var casePath = $"/api/cases/{CoreDevelopmentSeed.CaseId:D}";
var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
Check(!endpoints.Any(endpoint => endpoint.RoutePattern.RawText?.Trim('/') is
        "api/cases" or "api/cases/{caseId}" or "api/cases/{caseId:guid}"),
    "Case collection and detail routes are absent from the product endpoint inventory");
foreach (var path in new[] { "/api/cases?page=1&pageSize=20", casePath })
{
    using var response = await manager.GetAsync(path);
    Check(response.StatusCode == HttpStatusCode.NotFound, $"unintegrated Case route returns 404 for {path.Split('?')[0].Replace(casePath, "case detail")}");
}
foreach (var suffix in new[] { "tasks", "tasks/completion" })
{
    using var response = await manager.GetAsync($"{casePath}/{suffix}");
    using var json = await ReadJson(response);
    Check(response.StatusCode == HttpStatusCode.Forbidden &&
        json.RootElement.GetProperty("code").GetString() == "CASE_ACCESS_NOT_INTEGRATED",
        $"{suffix} retains its existing gate in the actual Program");
}

using (var response = await manager.GetAsync(casePath + "/status-history"))
{
    using var json = await ReadJson(response);
    Check(response.StatusCode == HttpStatusCode.Forbidden &&
        json.RootElement.GetProperty("code").GetString() == "CASE_ACCESS_NOT_INTEGRATED",
        "CaseStatusHistory still denies access with the current permission");
}
rows.FailPermissionLookup = true;
using (var response = await manager.GetAsync(casePath + "/status-history"))
{
    using var json = await ReadJson(response);
    Check(response.StatusCode == HttpStatusCode.ServiceUnavailable &&
        json.RootElement.GetProperty("code").GetString() == "AUTHORIZATION_UNAVAILABLE",
        "permission lookup failure is mapped to a safe 503 by the actual pipeline");
}
rows.FailPermissionLookup = false;

using var csrf = await manager.GetAsync("/api/auth/csrf");
using var csrfJson = await ReadJson(csrf);
var requestToken = csrfJson.RootElement.GetProperty("requestToken").GetString();
Check(csrf.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(requestToken),
    "authenticated csrf GET issues a request token");
foreach (var candidate in new[] { null, "invalid" })
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
    if (candidate is not null) request.Headers.Add("X-EarlyCare-CSRF", candidate);
    using var response = await manager.SendAsync(request);
    using var json = await ReadJson(response);
    Check(response.StatusCode == HttpStatusCode.Forbidden &&
        json.RootElement.GetProperty("code").GetString() == "CSRF_VALIDATION_FAILED",
        $"logout rejects {(candidate is null ? "missing" : "invalid")} CSRF token");
}
using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
{
    request.Headers.Add("X-EarlyCare-CSRF", requestToken);
    using var response = await manager.SendAsync(request);
    var removed = SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
        .Single(item => item.Name == StaffSessionCookieDefaults.Name);
    Check(response.StatusCode == HttpStatusCode.NoContent && removed.Value.Length == 0 &&
        removed.Expires <= DateTimeOffset.UtcNow, "valid CSRF logout expires the staff cookie");
}
using (var response = await manager.GetAsync("/api/auth/me"))
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "same client is unauthorized after logout");
Console.WriteLine($"PASS: {checks} G4-B checks using actual Program and TestServer with offline ADO.");

static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
    JsonDocument.Parse(await response.Content.ReadAsStringAsync());

sealed class ProductFactory(string environment, string contentRoot, string connectionString, IdentityRows rows)
    : WebApplicationFactory<StaffSessionController>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(environment).UseContentRoot(contentRoot);
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment).UseContentRoot(contentRoot);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            foreach (var type in new[] { typeof(ApplicationDbContext), typeof(AuthenticationService),
                typeof(IAuthenticationService), typeof(IPermissionService), typeof(CaseAccessService),
                typeof(IMedicalTaskCaseReadScope), typeof(QuestionnaireService), typeof(IQuestionnaireStore) })
            {
                if (services.Single(service => service.ServiceType == type).Lifetime != ServiceLifetime.Scoped)
                    throw new InvalidOperationException($"Expected one scoped {type.Name} registration.");
            }
            if (services.Single(service => service.ImplementationType == typeof(PermissionAuthorizationHandler))
                .Lifetime != ServiceLifetime.Scoped)
                throw new InvalidOperationException("Permission handler must be scoped.");

            // Keep the actual service graph. Replace only provider options and the persisted key ring.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddScoped(_ => new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 46)))
                .AddInterceptors(new NoNetwork(), rows).Options);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }
}

sealed record FixtureUser(Guid Id, Guid RoleId, string Username, string Role);

sealed class IdentityRows(string password) : DbCommandInterceptor
{
    public const string OrganizationCode = "G4B_FIXTURE";
    private readonly Guid organizationId = Guid.NewGuid();
    private readonly string hash = BCrypt.Net.BCrypt.HashPassword(password);
    public string Password { get; } = password;
    public FixtureUser Manager { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "g4b-manager", "CASE_MANAGER");
    public FixtureUser Staff { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "g4b-staff", "MEDICAL_STAFF");
    public bool FailPermissionLookup { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Connection?.State != ConnectionState.Closed)
            throw new InvalidOperationException("The offline connection must remain closed.");
        var sql = command.CommandText;
        var table = Regex.Match(sql, @"\bFROM\s+`(?<name>[^`]+)`").Groups["name"].Value;
        var parameter = command.Parameters.Cast<DbParameter>().Single().Value;
        var user = new[] { Manager, Staff }.SingleOrDefault(user =>
            Equals(parameter, user.Id) || Equals(parameter, user.RoleId) || Equals(parameter, user.Username));
        Dictionary<string, object?> row;
        if (table == "users" && sql.Contains("JOIN `permissions`"))
        {
            if (FailPermissionLookup) throw new SyntheticPermissionFailure();
            row = new() { ["status"] = "ACTIVE", ["name"] = "case.status_history.read" };
        }
        else if (table == "users" && user is not null)
            row = new()
            {
                ["user_id"] = user.Id, ["role_id"] = user.RoleId, ["organization_id"] = organizationId,
                ["username"] = user.Username, ["password_hash"] = hash, ["status"] = "ACTIVE", ["name"] = user.Role,
                ["created_at_utc"] = DateTime.UnixEpoch, ["updated_at_utc"] = DateTime.UnixEpoch,
                ["last_login_at_utc"] = null, ["phone"] = null, ["phone_verified_at_utc"] = null,
                ["email"] = null, ["normalized_email"] = null, ["email_verified_at_utc"] = null
            };
        else if (table == "roles" && user is not null)
            row = new() { ["role_id"] = user.RoleId, ["name"] = user.Role, ["description"] = null };
        else if (table == "organizations" && Equals(parameter, organizationId))
            row = new() { ["code"] = OrganizationCode, ["status"] = "ACTIVE" };
        else if (table == "role_permissions" && user is not null)
            row = new() { ["name"] = "case.status_history.read" };
        else throw new InvalidOperationException("Unexpected command in the G4-B offline fixture.");

        var columns = Regex.Matches(sql[..sql.IndexOf("FROM", StringComparison.Ordinal)], @"`[^`]+`\.`(?<column>[^`]+)`")
            .Select(match => match.Groups["column"].Value).ToArray();
        var data = new DataTable();
        foreach (var column in columns)
            data.Columns.Add(column, column.EndsWith("_id", StringComparison.Ordinal) ? typeof(Guid) :
                column.EndsWith("_utc", StringComparison.Ordinal) ? typeof(DateTime) : typeof(string));
        data.Rows.Add(columns.Select(column => row[column] ?? DBNull.Value).ToArray());
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(data.CreateDataReader()));
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Writes are forbidden in G4-B.");
}

sealed class SyntheticPermissionFailure() : DbException("Synthetic permission reader failure.");

sealed class NoNetwork : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result) => throw new InvalidOperationException("Synchronous database opening is forbidden.");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
