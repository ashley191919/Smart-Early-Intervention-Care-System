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

HttpClient CreateClient(ProductFactory host)
{
    var client = host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(origin),
        AllowAutoRedirect = false
    });
    client.DefaultRequestHeaders.Add("Origin", origin);
    return client;
}

using var anonymous = CreateClient(factory);
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

using var manager = CreateClient(factory);
using var staff = CreateClient(factory);
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
using (var request = new HttpRequestMessage(HttpMethod.Patch, casePath + "/status"))
{
    request.Headers.Add("X-EarlyCare-CSRF", requestToken);
    request.Content = JsonContent.Create(new { newStatus = "TO_CONTACT" });
    using var response = await manager.SendAsync(request);
    await CheckError(response, HttpStatusCode.Forbidden, "CASE_ACCESS_NOT_INTEGRATED",
        "CaseStatus PATCH still denies a permitted Cookie user with valid CSRF");
}
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

// Exercise the existing Development fixture gate through Program without reading local User Secrets.
var emptyProfile = Path.Combine(isolatedRoot, "..", "g4b-empty-profile");
Directory.CreateDirectory(emptyProfile);
if (Directory.EnumerateFileSystemEntries(emptyProfile).Any())
    throw new InvalidOperationException("The harness User Secrets profile must be empty.");
Environment.SetEnvironmentVariable("APPDATA", emptyProfile);
Environment.SetEnvironmentVariable("DOTNET_USER_SECRETS_FALLBACK_DIR", emptyProfile);
Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
const string developmentConnection = "Server=127.0.0.1;Port=1;Database=earlycare_dev;User=offline;Pooling=False;";
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", developmentConnection);
await using var development = new ProductFactory(Environments.Development, isolatedRoot, developmentConnection, rows);
using var developmentManager = CreateClient(development);
using var developmentStaff = CreateClient(development);
Check(development.Services.GetRequiredService<IHostEnvironment>().IsDevelopment(),
    "actual Program starts in isolated Development under TestServer");
await Login(developmentManager, rows.Manager);
await Login(developmentStaff, rows.Staff);

foreach (var suffix in new[] { "tasks", "tasks/completion" })
{
    var path = $"{casePath}/{suffix}";
    using (var request = new HttpRequestMessage(HttpMethod.Get, path))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        using var response = await developmentManager.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Unauthorized, $"{suffix} invalid JWT rejects even with a valid Cookie");
    }
    using (var response = await developmentStaff.GetAsync(path))
        Check(response.StatusCode == HttpStatusCode.Forbidden, $"{suffix} keeps MEDICAL_STAFF role gate");

    rows.AllowTaskRead = false;
    var counts = rows.ReadCounts;
    using (var response = await developmentManager.GetAsync(path))
        await CheckError(response, HttpStatusCode.Forbidden, "FORBIDDEN", $"{suffix} missing Task Permission");
    Check(counts == rows.ReadCounts, $"{suffix} missing permission skips Scope and Task Store");
    rows.AllowTaskRead = true;

    rows.CaseInActorOrganization = false;
    counts = rows.ReadCounts;
    using (var response = await developmentManager.GetAsync(path))
        await CheckError(response, HttpStatusCode.NotFound, "CASE_NOT_FOUND", $"{suffix} Scope false");
    Check(rows.ReadCounts == (counts.Actors + 1, counts.Scopes + 1, counts.Store),
        $"{suffix} Scope false skips Task Store");
    rows.CaseInActorOrganization = true;

    rows.ScopeActorOrganizationActive = false;
    using (var response = await developmentManager.GetAsync(path))
        await CheckError(response, HttpStatusCode.Unauthorized, "UNAUTHENTICATED",
            $"{suffix} CaseAccessException for inactive organization");
    rows.ScopeActorOrganizationActive = true;

    using (var response = await developmentManager.GetAsync(path))
    using (var json = await ReadJson(response))
        Check(response.StatusCode == HttpStatusCode.OK && (suffix == "tasks"
                ? json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() == 0
                : json.RootElement.GetProperty("totalTaskCount").GetInt32() == 0 &&
                    !json.RootElement.GetProperty("allRequiredSubmitted").GetBoolean()),
            $"{suffix} scoped Development Cookie request preserves the empty contract");
}

using (var csrfResponse = await developmentStaff.GetAsync("/api/auth/csrf"))
using (var json = await ReadJson(csrfResponse))
using (var request = new HttpRequestMessage(HttpMethod.Post, casePath + "/tasks"))
{
    request.Headers.Add("X-EarlyCare-CSRF", json.RootElement.GetProperty("requestToken").GetString());
    request.Content = JsonContent.Create(new
    {
        questionnaireVersionId = Guid.NewGuid(),
        respondentRole = "TEACHER",
        assignmentRound = 1,
        isRequired = true
    });
    using var response = await developmentStaff.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.Forbidden && string.IsNullOrEmpty(await response.Content.ReadAsStringAsync()),
        $"POST Assign retains its MEDICAL_STAFF role rejection with valid input and CSRF (HTTP {(int)response.StatusCode})");
}
Console.WriteLine($"PASS: {checks} G4-B checks using actual Program and TestServer with offline ADO.");

async Task CheckError(HttpResponseMessage response, HttpStatusCode status, string code, string label)
{
    using var json = await ReadJson(response);
    Check(response.StatusCode == status && json.RootElement.GetProperty("code").GetString() == code, label);
}

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
    public bool AllowTaskRead { get; set; } = true;
    public bool ScopeActorOrganizationActive { get; set; } = true;
    public bool CaseInActorOrganization { get; set; } = true;
    private int actorReads;
    private int scopeReads;
    private int storeReads;
    public (int Actors, int Scopes, int Store) ReadCounts => (actorReads, scopeReads, storeReads);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Connection?.State != ConnectionState.Closed)
            throw new InvalidOperationException("The offline connection must remain closed.");
        var sql = command.CommandText;
        var table = Regex.Match(sql, @"\bFROM\s+`(?<name>[^`]+)`").Groups["name"].Value;
        var parameters = command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray();
        var parameter = parameters.FirstOrDefault();
        if (table == "cases")
        {
            var scoped = sql.Contains("`organization_id`");
            if (!parameters.Contains(CoreDevelopmentSeed.CaseId) || (scoped && !parameters.Contains(organizationId)))
                throw new InvalidOperationException("Expected the fixture Case and, for Scope, the actor organization.");
            if (scoped) scopeReads++;
            else storeReads++;
            var cases = new DataTable();
            cases.Columns.Add("exists", typeof(bool));
            cases.Rows.Add(!scoped || CaseInActorOrganization);
            return Read(cases);
        }
        var columns = Regex.Matches(sql[..sql.IndexOf("FROM", StringComparison.Ordinal)], @"`[^`]+`\.`(?<column>[^`]+)`")
            .Select(match => match.Groups["column"].Value).ToArray();
        if (table == "case_questionnaires")
        {
            if (!parameters.Contains(CoreDevelopmentSeed.CaseId) || !sql.Contains("WHERE"))
                throw new InvalidOperationException("Expected a Case-scoped task query.");
            storeReads++;
            var tasks = new DataTable();
            // No rows are materialized. Retain the result's ordinal layout.
            foreach (var _ in columns) tasks.Columns.Add();
            return Read(tasks);
        }
        var user = new[] { Manager, Staff }.SingleOrDefault(user =>
            Equals(parameter, user.Id) || Equals(parameter, user.RoleId) || Equals(parameter, user.Username));
        Dictionary<string, object?> row;
        var permissionLookup = table == "users" && sql.Contains("JOIN `permissions`");
        var scopeActorLookup = table == "users" && sql.Contains("JOIN `organizations`");
        if (scopeActorLookup) actorReads++;
        if (permissionLookup)
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

        var data = new DataTable();
        foreach (var column in columns)
            data.Columns.Add(column, column.EndsWith("_id", StringComparison.Ordinal) ? typeof(Guid) :
                column.EndsWith("_utc", StringComparison.Ordinal) ? typeof(DateTime) : typeof(string));
        if (!scopeActorLookup || ScopeActorOrganizationActive)
            data.Rows.Add(columns.Select(column => row[column] ?? DBNull.Value).ToArray());
        if (permissionLookup)
        {
            data.Rows.Add("ACTIVE", "case.status.update");
            if (AllowTaskRead) data.Rows.Add("ACTIVE", "questionnaire.task.read");
        }
        return Read(data);
    }

    private static ValueTask<InterceptionResult<DbDataReader>> Read(DataTable data)
        => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(data.CreateDataReader()));

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
