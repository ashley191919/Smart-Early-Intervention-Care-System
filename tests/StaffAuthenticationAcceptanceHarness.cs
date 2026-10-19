using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Authentication;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

// Offline acceptance of the REAL AuthenticationService, BCrypt, JWT and controllers.
// EF connections are suppressed before Open; every reader is synthetic. No User Secrets,
// real MySQL, database writes, HTTP authorization pipeline or browser are used in this harness.
var checks = 0;
void Check(bool value, string description)
{
    checks++;
    if (!value) throw new Exception(description);
}
const string fixturePassword = "  offline acceptance pass phrase  ";
const string fixtureOrganizationCode = "OFFLINE_ORG";
var fixtureHash = BCrypt.Net.BCrypt.HashPassword(fixturePassword, workFactor: 12);
var guard = new OfflineConnectionGuard();
var rows = new SyntheticIdentityReader(fixtureHash);
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseMySql("Server=127.0.0.1;Port=1;Database=offline_auth_fixture;User ID=offline;Password=offline;Pooling=false",
        new MySqlServerVersion(new Version(8, 0, 46)))
    .AddInterceptors(guard, rows)
    .Options;
await using var db = new ApplicationDbContext(options);
var jwtOptions = new JwtOptions { Issuer = "offline-issuer", Audience = "offline-audience", ExpirationMinutes = 30 };
var key = new SymmetricSecurityKey(Enumerable.Range(1, 32).Select(n => (byte)n).ToArray());
var tokens = new JwtTokenService(jwtOptions, key);
var authentication = new AuthenticationService(db, tokens);
var auth = new AuthController(authentication);
var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };

ClaimsPrincipal ValidateToken(string token) => tokenHandler.ValidateToken(token, new TokenValidationParameters
{
    ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
    ValidateIssuer = true, ValidIssuer = jwtOptions.Issuer, ValidateAudience = true, ValidAudience = jwtOptions.Audience,
    ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.FromSeconds(30),
    NameClaimType = "username", RoleClaimType = "role"
}, out _);

const string origin = "https://localhost:7127";
StaffSessionController StaffController()
{
    var context = new DefaultHttpContext();
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("localhost", 7127);
    context.Request.Headers.Origin = origin;
    return new StaffSessionController(authentication, new StaffSessionCookieService(),
        new StaffSessionOptions(new[] { origin }), new UnusedAntiforgery())
    {
        ControllerContext = new ControllerContext { HttpContext = context }
    };
}

StaffLoginRequest StaffRequest(LoginRequest request) => new()
{
    Username = request.Username, Password = request.Password,
    OrganizationCode = request.OrganizationCode ?? fixtureOrganizationCode
};

foreach (var role in new[] { "CASE_MANAGER", "MEDICAL_STAFF", "ADMIN", "PARENT", "TEACHER" })
{
    var fixture = rows.Users.Single(u => u.Username == "fixture-" + role.ToLowerInvariant());
    var medicalRole = role is "CASE_MANAGER" or "MEDICAL_STAFF";
    var request = new LoginRequest { Username = fixture.Username, Password = fixturePassword,
        OrganizationCode = medicalRole ? fixtureOrganizationCode : null };
    rows.Queries.Clear();
    var response = await auth.Login(request, default) as OkObjectResult;
    var login = response?.Value as LoginResponse;
    Check(response?.StatusCode == 200 && login?.UserId == fixture.Id && login.Role == role,
        "real JWT login accepts an ACTIVE fixture with the correct BCrypt password");
    Check(rows.Queries.SequenceEqual(medicalRole
            ? new[] { "users", "roles", "organizations", "role_permissions" }
            : new[] { "users", "roles", "role_permissions" }),
        "medical login verifies the referenced organization; code-less non-medical login retains its contract");
    var principal = ValidateToken(login!.AccessToken);
    Check(principal.FindAll("userId").Single().Value == fixture.Id.ToString("D") && principal.IsInRole(role),
        "issued JWT validates signature and trusted userId/role");
    Check(principal.FindFirst("sub")?.Value == fixture.Id.ToString("D") && principal.FindFirst("sv") is null,
        "current claims are retained without pretending version revocation is enabled");
    Check(login.Permissions.SequenceEqual(new[] { "fixture.read" }), "current permission metadata comes from the query result");
    auth.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
    var me = await auth.Me(default) as OkObjectResult;
    var current = me?.Value as CurrentUserResponse;
    Check(current?.UserId == fixture.Id && current.Role == role, "me resolves the signed-in fixture and current role");
    Check(!JsonSerializer.Serialize(current).Contains("password", StringComparison.OrdinalIgnoreCase),
        "me response does not expose credential fields");

    var staff = StaffController();
    var staffResult = await staff.Create(StaffRequest(request), default);
    var cookies = SetCookieHeaderValue.ParseList(staff.Response.Headers.SetCookie.Select(value => value!).ToArray());
    // Create first expires the old cookie, then appends the new one; the final value wins.
    var cookie = cookies.Last(c => c.Name == StaffSessionCookieDefaults.Name);
    if (role is "CASE_MANAGER" or "MEDICAL_STAFF")
    {
        var session = (staffResult as OkObjectResult)?.Value as StaffSessionResponse;
        Check(session?.UserId == fixture.Id && session.Role == role, "medical role creates staff session through real authentication");
        Check(cookie.Value.Length > 0 && cookie.HttpOnly && cookie.Secure && cookie.SameSite == Microsoft.Net.Http.Headers.SameSiteMode.Strict &&
            cookie.Path == "/" && !cookie.Domain.HasValue, "staff cookie retains secure host-only attributes");
        var cookiePrincipal = ValidateToken(cookie.Value.ToString());
        auth.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = cookiePrincipal } };
        Check(((await auth.Me(default) as OkObjectResult)?.Value as CurrentUserResponse)?.UserId == fixture.Id,
            "JWT carried by a staff cookie resolves through the real me service");
        Check(!JsonSerializer.Serialize(session).Contains("AccessToken", StringComparison.OrdinalIgnoreCase),
            "staff-session response does not return its token");
    }
    else
    {
        Check(staffResult is UnauthorizedObjectResult, "non-medical role remains denied for staff cookie login");
        Check(cookie.Value.Length == 0, "denied role receives no usable staff credential");
    }
}

var medical = rows.Users.Single(u => u.Username == "fixture-medical_staff");
var inactive = rows.Users.Single(u => u.Status == "INACTIVE");
string? genericError = null;
foreach (var request in new[]
{
    new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = medical.Username, Password = "synthetic wrong password" },
    new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = "fixture-does-not-exist", Password = fixturePassword },
    new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = inactive.Username, Password = fixturePassword }
})
{
    rows.Queries.Clear();
    var rejected = await auth.Login(request, default) as UnauthorizedObjectResult;
    Check(rejected?.StatusCode == 401, "wrong, absent and inactive fixtures are all rejected without a JWT");
    var error = JsonSerializer.Serialize(rejected!.Value);
    genericError ??= error;
    Check(error == genericError && !error.Contains(request.Username, StringComparison.Ordinal) &&
        !error.Contains(request.Password, StringComparison.Ordinal), "login errors disclose neither account existence nor credentials");
    Check(rows.Queries.SequenceEqual(new[] { "users" }), "failed user/password gate does not fetch roles or sign a response");
    var staff = StaffController();
    var staffRejected = await staff.Create(StaffRequest(request), default) as UnauthorizedObjectResult;
    Check(staffRejected is not null && JsonSerializer.Serialize(staffRejected.Value) == genericError,
        "both endpoints return the same safe failed-login response");
    Check(SetCookieHeaderValue.ParseList(staff.Response.Headers.SetCookie.Select(value => value!).ToArray()).Single().Value.Length == 0,
        "failed staff login leaves no usable session cookie");
}

Check(await authentication.LoginAsync(new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = "fixture-bad-hash", Password = fixturePassword }) is null,
    "malformed synthetic BCrypt hash fails closed");
Check(await authentication.LoginAsync(new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = "fixture-missing-role", Password = fixturePassword }) is null,
    "missing role does not issue a token");
rows.FailureTable = "role_permissions";
Check(await auth.Login(new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = medical.Username, Password = fixturePassword }, default) is ObjectResult { StatusCode: 503 },
    "permission metadata query failure does not issue a token or leak provider errors");
rows.FailureTable = null;
// Organization gates use the real service and BCrypt, but only synthetic SQL readers.
async Task DeniedByBoth(LoginRequest request, string description)
{
    rows.Queries.Clear();
    var generic = await auth.Login(request, default) as UnauthorizedObjectResult;
    Check(generic is not null && JsonSerializer.Serialize(generic.Value) == genericError, description + ": generic 401");
    Check(!rows.Queries.Contains("role_permissions"), description + ": no permissions or token issuance");
    var staff = StaffController();
    // Do not fill missing organization codes: direct service/controller calls must also fail closed.
    var rejected = await staff.Create(new StaffLoginRequest
    {
        OrganizationCode = request.OrganizationCode, Username = request.Username, Password = request.Password
    }, default) as UnauthorizedObjectResult;
    Check(rejected is not null && JsonSerializer.Serialize(rejected.Value) == genericError, description + ": staff 401");
    Check(SetCookieHeaderValue.ParseList(staff.Response.Headers.SetCookie.Select(value => value!).ToArray())
        .All(cookie => cookie.Value.Length == 0), description + ": no usable staff cookie");
}
LoginRequest MedicalRequest(string? code = fixtureOrganizationCode, string password = fixturePassword) => new()
{
    Username = medical.Username, Password = password, OrganizationCode = code
};

foreach (var code in new[] { "DOES_NOT_EXIST", "OTHER_ORG", "OFFLINE_ÓRG" })
    await DeniedByBoth(MedicalRequest(code), "wrong, unrelated or accent-different organization");
foreach (var code in new string?[] { null, "", "   " })
    foreach (var role in new[] { "CASE_MANAGER", "MEDICAL_STAFF" })
    {
        var fixture = rows.Users.Single(u => u.Role == role && u.Status == "ACTIVE" && u.Username.StartsWith("fixture-" + role.ToLowerInvariant()));
        await DeniedByBoth(new LoginRequest { Username = fixture.Username, Password = fixturePassword, OrganizationCode = code },
            "medical roles cannot bypass organization checks via generic login");
    }
foreach (var code in new[] { "offline_org", "  OfFlInE_oRg  " })
{
    Check(await auth.Login(MedicalRequest(code), default) is OkObjectResult, "generic medical login trims and compares code ignoring case");
    Check(await StaffController().Create(StaffRequest(MedicalRequest(code)), default) is OkObjectResult,
        "staff login uses the same organization comparison rule");
}
await DeniedByBoth(MedicalRequest(password: fixturePassword.Trim()), "password must not be trimmed");
var organization = rows.Organizations.Single(o => o.Id == medical.OrganizationId);
foreach (var status in new[] { "INACTIVE", "active", " ACTIVE " })
{
    rows.Organizations[rows.Organizations.IndexOf(rows.Organizations.Single(o => o.Id == organization.Id))] = organization with { Status = status };
    await DeniedByBoth(MedicalRequest(), "non-ACTIVE organization is rejected exactly");
}
rows.Organizations.RemoveAll(o => o.Id == organization.Id);
await DeniedByBoth(MedicalRequest(), "missing referenced organization is rejected");
rows.Organizations.Add(organization);
rows.Users[rows.Users.IndexOf(medical)] = medical with { OrganizationId = rows.Organizations.Single(o => o.Code == "OTHER_ORG").Id };
await DeniedByBoth(MedicalRequest(), "matching code belonging to another organization is insufficient");
rows.Users[rows.Users.FindIndex(u => u.Id == medical.Id)] = medical;
foreach (var role in new[] { "ADMIN", "PARENT", "TEACHER" })
{
    var fixture = rows.Users.Single(u => u.Role == role);
    Check(await auth.Login(new LoginRequest { Username = fixture.Username, Password = fixturePassword }, default) is OkObjectResult,
        "non-medical generic login still accepts an omitted organization code");
    foreach (var code in new[] { "WRONG_ORG", "", "  " })
    {
        var rejected = await auth.Login(new LoginRequest { Username = fixture.Username, Password = fixturePassword, OrganizationCode = code }, default)
            as UnauthorizedObjectResult;
        Check(rejected is not null && JsonSerializer.Serialize(rejected.Value) == genericError,
            "non-medical generic login never ignores an explicitly supplied invalid code");
    }
    Check(await auth.Login(new LoginRequest { Username = fixture.Username, Password = fixturePassword, OrganizationCode = "offline_org" }, default)
        is OkObjectResult, "non-medical supplied valid organization is accepted");
}
rows.FailureTable = "organizations";
Check(await auth.Login(MedicalRequest(), default) is ObjectResult { StatusCode: 503 }, "organization query failure maps to safe generic 503");
Check(await StaffController().Create(StaffRequest(MedicalRequest()), default) is ObjectResult { StatusCode: 503 },
    "staff organization query failure never creates a cookie");
rows.FailureTable = null;

bool ValidDto(object dto) => Validator.TryValidateObject(dto, new ValidationContext(dto), new List<ValidationResult>(), validateAllProperties: true);
Check(ValidDto(new LoginRequest { Username = "fixture", Password = fixturePassword }), "generic DTO retains optional organization code");
foreach (var code in new string?[] { null, "", "  ", new string('a', 51) })
    Check(!ValidDto(new StaffLoginRequest { Username = "fixture", Password = fixturePassword, OrganizationCode = code }),
        "staff DTO requires nonblank organization code of at most 50 characters");
Check(ValidDto(new StaffLoginRequest { Username = new string('a', 100), Password = fixturePassword, OrganizationCode = new string('a', 50) }),
    "staff DTO accepts exact mapped length boundaries");
Check(!ValidDto(new StaffLoginRequest { Username = new string('a', 101), Password = fixturePassword, OrganizationCode = fixtureOrganizationCode }),
    "staff DTO inherits username maximum");
Check(!ValidDto(new StaffLoginRequest { Username = "fixture", Password = new string('a', 73), OrganizationCode = fixtureOrganizationCode }),
    "staff DTO inherits BCrypt byte length validation");
Check(!ValidDto(new LoginRequest { Username = "fixture", Password = fixturePassword, OrganizationCode = new string('a', 51) }),
    "optional generic code still has a maximum length");

var mePrincipal = ValidateToken((await authentication.LoginAsync(new LoginRequest { OrganizationCode = fixtureOrganizationCode, Username = medical.Username, Password = fixturePassword }))!.AccessToken);
auth.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = mePrincipal } };
rows.Users[rows.Users.IndexOf(medical)] = medical with { Status = "INACTIVE" };
Check(await auth.Me(default) is UnauthorizedObjectResult, "real me lookup rejects a fixture disabled after token issuance");
Check(guard.SuppressedOpens > 0 && rows.Readers > 0 && db.Database.GetDbConnection().State == ConnectionState.Closed,
    "all real service queries used synthetic readers while the MySQL connection stayed closed");
Check(!db.ChangeTracker.HasChanges(), "acceptance checks staged no persistent changes");
Console.WriteLine($"PASS: {checks} offline real AuthenticationService acceptance checks; synthetic SQL readers only, no MySQL/HTTP/browser acceptance.");

sealed record FixtureUser(Guid Id, string Username, string Status, Guid RoleId, string Role, Guid OrganizationId, string Hash);
sealed record FixtureOrganization(Guid Id, string Code, string Status);

sealed class OfflineConnectionGuard : DbConnectionInterceptor
{
    public int SuppressedOpens { get; private set; }
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        SuppressedOpens++;
        return InterceptionResult.Suppress();
    }
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
}

sealed class SyntheticIdentityReader : DbCommandInterceptor
{
    public List<FixtureUser> Users { get; } = new();
    public List<FixtureOrganization> Organizations { get; } = new();
    public List<string> Queries { get; } = new();
    public int Readers { get; private set; }
    public string? FailureTable { get; set; }

    public SyntheticIdentityReader(string hash)
    {
        var organizationId = Guid.NewGuid();
        Organizations.Add(new(organizationId, "OFFLINE_ORG", "ACTIVE"));
        Organizations.Add(new(Guid.NewGuid(), "OTHER_ORG", "ACTIVE"));
        foreach (var role in new[] { "CASE_MANAGER", "MEDICAL_STAFF", "ADMIN", "PARENT", "TEACHER" })
            Users.Add(new(Guid.NewGuid(), "fixture-" + role.ToLowerInvariant(), "ACTIVE", Guid.NewGuid(), role, organizationId, hash));
        var medical = Users.Single(u => u.Role == "MEDICAL_STAFF");
        Users.Add(medical with { Id = Guid.NewGuid(), Username = "fixture-inactive", Status = "INACTIVE" });
        Users.Add(medical with { Id = Guid.NewGuid(), Username = "fixture-bad-hash", Hash = "invalid-synthetic-hash" });
        Users.Add(medical with { Id = Guid.NewGuid(), Username = "fixture-missing-role", RoleId = Guid.NewGuid(), Role = "MISSING" });
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Connection?.State != ConnectionState.Closed) throw new InvalidOperationException("Offline connection guard was bypassed.");
        var match = Regex.Match(command.CommandText, @"\bFROM\s+`(?<table>[^`]+)`");
        var tableName = match.Groups["table"].Value;
        if (!new[] { "users", "roles", "organizations", "role_permissions" }.Contains(tableName))
            throw new InvalidOperationException("Unexpected offline query.");
        Queries.Add(tableName);
        if (FailureTable == tableName) throw new SyntheticDbException();
        // Pomelo can wrap DISTINCT/ORDER BY in a subquery. Read only the outer SELECT list.
        var selectEnd = Regex.Match(command.CommandText, @"\bFROM\b").Index;
        var fields = Regex.Matches(command.CommandText[..selectEnd], @"`[^`]+`\.`(?<column>[^`]+)`")
            .Select(m => m.Groups["column"].Value).ToArray();
        var parameter = command.Parameters.Cast<DbParameter>().Single().Value;
        IEnumerable<Dictionary<string, object?>> values = tableName switch
        {
            "users" => Users.Where(u => parameter is string username ? u.Username == username : u.Id == (Guid)parameter!)
                .Select(u => new Dictionary<string, object?>
                {
                    ["user_id"] = u.Id, ["username"] = u.Username, ["password_hash"] = u.Hash,
                    ["organization_id"] = u.OrganizationId, ["role_id"] = u.RoleId, ["phone"] = null,
                    ["status"] = u.Status, ["last_login_at_utc"] = null,
                    ["created_at_utc"] = DateTime.UnixEpoch, ["updated_at_utc"] = DateTime.UnixEpoch
                }),
            "roles" => Users.Where(u => u.RoleId == (Guid)parameter! && u.Role != "MISSING").DistinctBy(u => u.RoleId)
                .Select(u => new Dictionary<string, object?> { ["role_id"] = u.RoleId, ["name"] = u.Role, ["description"] = null }),
            "organizations" => Organizations.Where(o => o.Id == (Guid)parameter!)
                .Select(o => new Dictionary<string, object?> { ["organization_id"] = o.Id, ["code"] = o.Code, ["status"] = o.Status }),
            _ => new[] { new Dictionary<string, object?> { ["name"] = "fixture.read" } }
        };
        var table = new DataTable();
        foreach (var field in fields)
            table.Columns.Add(field, field.EndsWith("_id", StringComparison.Ordinal) ? typeof(Guid) :
                field.EndsWith("_utc", StringComparison.Ordinal) ? typeof(DateTime) : typeof(string));
        foreach (var value in values) table.Rows.Add(fields.Select(field => value[field] ?? DBNull.Value).ToArray());
        Readers++;
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Database writes are forbidden in this harness.");
}

sealed class SyntheticDbException() : DbException("Synthetic fixture provider failure.");

// Create does not use IAntiforgery directly; real CSRF behavior remains covered by StaffSessionHarness.
sealed class UnusedAntiforgery : IAntiforgery
{
    public void SetCookieTokenAndHeader(HttpContext context) => throw new InvalidOperationException("Unexpected antiforgery call.");
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => throw new InvalidOperationException("Unexpected antiforgery call.");
    public AntiforgeryTokenSet GetTokens(HttpContext context) => throw new InvalidOperationException("Unexpected antiforgery call.");
    public Task<bool> IsRequestValidAsync(HttpContext context) => throw new InvalidOperationException("Unexpected antiforgery call.");
    public Task ValidateRequestAsync(HttpContext context) => throw new InvalidOperationException("Unexpected antiforgery call.");
}
