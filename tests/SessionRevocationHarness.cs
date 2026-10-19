using System.Data.Common;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

// Offline only: synthetic keys/principals, real JwtBearerHandler and an in-test atomic fake store.
// No application host, socket listener, MySQL, User Secrets or production DI activation.
var checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Validation(SessionValidationResult result, SessionValidationStatus expected)
{
    Check(result.Status == expected && result.IsValid == (expected == SessionValidationStatus.Valid),
        "principal validation has the expected fail-closed result");
}
var results = new List<SessionRevocationResult>();
void Revocation(SessionRevocationResult result, SessionRevocationStatus expected)
{
    results.Add(result);
    Check(result.Status == expected && result.IsRevoked ==
        (expected is SessionRevocationStatus.Revoked or SessionRevocationStatus.AlreadyRevoked),
        "revocation has the expected decision, without sensitive metadata");
    Check(result.Code == (expected switch
    {
        SessionRevocationStatus.Revoked => "SESSION_REVOKED",
        SessionRevocationStatus.AlreadyRevoked => "SESSION_ALREADY_REVOKED",
        SessionRevocationStatus.InvalidCredential => "SESSION_INVALID",
        _ => "SESSION_SECURITY_STATE_UNAVAILABLE"
    }), "revocation uses fixed safe codes");
}
async Task Cancelled(Func<Task> operation, CancellationToken token)
{
    try { await operation(); }
    catch (OperationCanceledException exception)
    {
        Check(exception.CancellationToken == token && token.IsCancellationRequested,
            "caller cancellation is propagated, not converted to success");
        return;
    }
    Check(false, "caller cancellation must not be swallowed");
}

var id = Guid.NewGuid();
var otherId = Guid.NewGuid();
ClaimsPrincipal Principal(long version, Guid? userId = null) => new(new ClaimsIdentity(new[]
{
    new Claim("userId", (userId ?? id).ToString("D")),
    new Claim("sub", (userId ?? id).ToString("D")),
    SessionVersionClaims.Create(version)
}, "offline-validated-jwt"));
ClaimsPrincipal WithClaim(string value, string valueType = ClaimValueTypes.Integer64) =>
    new(new ClaimsIdentity(new[] { new Claim("userId", id.ToString("D")), new Claim("sv", value, valueType) }, "offline-validated-jwt"));

Check(SessionVersionClaims.InitialVersion == 1 && SessionVersionClaims.VersionClaimType == "sv" &&
      SessionVersionClaims.UserIdClaimType == "userId", "one explicit future claim contract");
try { _ = new SessionPrincipalValidator(null!); Check(false, "validator dependency must be required"); }
catch (ArgumentNullException) { Check(true, "missing validator cannot silently enable an allow-all fallback"); }
try { _ = new SessionVersionRevocationService(null!); Check(false, "persistent store dependency must be required"); }
catch (ArgumentNullException) { Check(true, "missing persistent writer cannot silently enable an in-memory fallback"); }
foreach (var version in new[] { 1L, 2L, long.MaxValue })
{
    var claim = SessionVersionClaims.Create(version);
    Check(claim.ValueType == ClaimValueTypes.Integer64 && claim.Value == version.ToString(CultureInfo.InvariantCulture),
        "issuer helper creates a canonical positive Int64 claim");
    Check(SessionVersionClaims.TryRead(Principal(version), out var parsedId, out var parsedVersion) &&
          parsedId == id && parsedVersion == version, "canonical authenticated claims round-trip");
}
foreach (var version in new[] { 0L, -1L, long.MinValue })
{
    try { SessionVersionClaims.Create(version); Check(false, "invalid issuer version must fail"); }
    catch (ArgumentOutOfRangeException) { Check(true, "issuer helper rejects invalid persisted version"); }
}

var malformed = new List<ClaimsPrincipal?>
{
    null, new(), new(new ClaimsIdentity(Principal(1).Claims)),
    new(new ClaimsIdentity(new[] { new Claim("userId", id.ToString("D")) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { SessionVersionClaims.Create(1) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { new Claim("userId", Guid.Empty.ToString("D")), SessionVersionClaims.Create(1) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { new Claim("userId", "invalid"), SessionVersionClaims.Create(1) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { new Claim("userId", " " + id.ToString("D")), SessionVersionClaims.Create(1) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { new Claim("USERID", id.ToString("D")), SessionVersionClaims.Create(1) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { new Claim("userId", id.ToString("D")), new Claim("SV", "1", ClaimValueTypes.Integer64) }, "offline-validated-jwt")),
    new(new ClaimsIdentity(Principal(1).Claims.Append(SessionVersionClaims.Create(1)), "offline-validated-jwt")),
    new(new ClaimsIdentity(Principal(1).Claims.Append(new Claim("userId", id.ToString("D"))), "offline-validated-jwt")),
    new(new ClaimsIdentity(Principal(1).Claims.Append(new Claim("sub", id.ToString("D"))), "offline-validated-jwt")),
    new(new ClaimsIdentity(new[] { new Claim("userId", id.ToString("D")), new Claim("sub", otherId.ToString("D")), SessionVersionClaims.Create(1) }, "offline-validated-jwt")),
    new(new[] { Principal(1).Identities.Single(), new ClaimsIdentity() }),
    WithClaim("1", ClaimValueTypes.String), WithClaim("1", ClaimValueTypes.Boolean),
    WithClaim("1", ClaimValueTypes.Double)
};
foreach (var text in new[] { "", "0", "-1", "+1", "01", " 1", "1 ", "1.0", "1e0", "１", "9223372036854775808" })
    malformed.Add(WithClaim(text));

var store = new AtomicFakeStore(new(id, "ACTIVE", 1));
ISessionPrincipalValidator validator = new SessionPrincipalValidator(new SessionVersionValidator(store));
ISessionRevocationService revoker = new SessionVersionRevocationService(store);
foreach (var principal in malformed)
{
    var reads = store.Reads;
    var writes = store.Attempts;
    Check(!SessionVersionClaims.TryRead(principal, out var invalidId, out var invalidVersion) &&
          invalidId == Guid.Empty && invalidVersion == 0, "bad claims leave no usable identity/version");
    Validation(await validator.ValidateAsync(principal), SessionValidationStatus.InvalidCredential);
    Revocation(await revoker.RevokeAllAsync(principal), SessionRevocationStatus.InvalidCredential);
    Check(store.Reads == reads && store.Attempts == writes, "bad or unauthenticated claims never reach the store");
}
Validation(await validator.ValidateAsync(WithClaim("1", ClaimValueTypes.Integer32)), SessionValidationStatus.Valid);
Validation(await validator.ValidateAsync(WithClaim("1", ClaimValueTypes.Integer)), SessionValidationStatus.Valid);
Validation(await validator.ValidateAsync(Principal(1)), SessionValidationStatus.Valid);
Validation(await validator.ValidateAsync(Principal(2)), SessionValidationStatus.InvalidCredential);

Revocation(await revoker.RevokeAllAsync(Principal(1)), SessionRevocationStatus.Revoked);
Check(store.State?.SessionVersion == 2 && store.Commits == 1, "logout advances the authoritative fake state once");
Validation(await validator.ValidateAsync(Principal(1)), SessionValidationStatus.InvalidCredential);
Validation(await validator.ValidateAsync(Principal(2)), SessionValidationStatus.Valid);
Revocation(await revoker.RevokeAllAsync(Principal(1)), SessionRevocationStatus.AlreadyRevoked);
Check(store.State?.SessionVersion == 2 && store.Commits == 1, "repeated old logout does not revoke a new login again");
Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.Revoked);
Revocation(await revoker.RevokeAllAsync(Principal(1)), SessionRevocationStatus.AlreadyRevoked);
Check(store.State?.SessionVersion == 3 && store.Commits == 2, "new authenticated version can revoke; much older retry stays harmless");
Revocation(await revoker.RevokeAllAsync(Principal(4)), SessionRevocationStatus.InvalidCredential);

foreach (var status in new[] { "INACTIVE", "active", "ACTIVE ", "", null })
{
    store.State = new(id, status!, 3);
    Validation(await validator.ValidateAsync(Principal(3)), SessionValidationStatus.InvalidCredential);
    Revocation(await revoker.RevokeAllAsync(Principal(3)), SessionRevocationStatus.InvalidCredential);
}
store.State = null;
Revocation(await revoker.RevokeAllAsync(Principal(1)), SessionRevocationStatus.InvalidCredential);
Validation(await validator.ValidateAsync(Principal(1)), SessionValidationStatus.InvalidCredential);

store.State = new(id, "ACTIVE", long.MaxValue - 1);
Revocation(await revoker.RevokeAllAsync(Principal(long.MaxValue - 1)), SessionRevocationStatus.Revoked);
Check(store.State.SessionVersion == long.MaxValue, "last possible increment does not wrap");
var attempts = store.Attempts;
Revocation(await revoker.RevokeAllAsync(Principal(long.MaxValue)), SessionRevocationStatus.SecurityStateUnavailable);
Check(store.Attempts == attempts && store.State.SessionVersion == long.MaxValue, "overflow is unavailable with no write or reset");
Validation(await validator.ValidateAsync(Principal(long.MaxValue)), SessionValidationStatus.Valid);

// Force all 32 admitted requests to reach the atomic store before releasing any update.
store = new AtomicFakeStore(new(id, "ACTIVE", 1));
revoker = new SessionVersionRevocationService(store);
validator = new SessionPrincipalValidator(new SessionVersionValidator(store));
var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var entered = 0;
store.AdvanceOverride = async (user, expected, ct) =>
{
    Interlocked.Increment(ref entered);
    await gate.Task.WaitAsync(ct);
    return store.Commit(user, expected);
};
var pending = Enumerable.Range(0, 32).Select(_ => revoker.RevokeAllAsync(Principal(1))).ToArray();
Check(entered == 32 && store.Commits == 0, "concurrent test admits all old-version requests before any update");
gate.SetResult();
var completed = await Task.WhenAll(pending);
Check(completed.Count(r => r.Status == SessionRevocationStatus.Revoked) == 1 &&
      completed.Count(r => r.Status == SessionRevocationStatus.AlreadyRevoked) == 31,
    "one concurrent logout commits and the other admitted logouts observe it idempotently");
Check(store.Commits == 1 && store.State?.SessionVersion == 2, "concurrent fake CAS loses no update or over-increments");
foreach (var result in completed)
    Revocation(result, result.Status);
store.AdvanceOverride = null;
Validation(await validator.ValidateAsync(Principal(1)), SessionValidationStatus.InvalidCredential);
Validation(await validator.ValidateAsync(Principal(2)), SessionValidationStatus.Valid);

const string sensitiveFixture = "synthetic-provider-detail-not-a-real-secret";
foreach (var failure in new Exception[]
{
    new FakeDbException(sensitiveFixture), new TimeoutException(sensitiveFixture),
    new InvalidOperationException(sensitiveFixture), new OperationCanceledException(sensitiveFixture)
})
{
    store.AdvanceOverride = (_, _, _) => Task.FromException<bool>(failure);
    Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.SecurityStateUnavailable);
    store.AdvanceOverride = (_, _, _) => Task.FromResult(false);
    store.ReadOverride = (_, _) => Task.FromException<SessionSecurityState?>(failure);
    Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.SecurityStateUnavailable);
    Validation(await validator.ValidateAsync(Principal(2)), SessionValidationStatus.SecurityStateUnavailable);
    store.ReadOverride = null;
}
store.AdvanceOverride = (_, _, _) => throw new FakeDbException(sensitiveFixture);
Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.SecurityStateUnavailable);
store.AdvanceOverride = (_, _, _) => Task.FromResult(false);
foreach (var bad in new[] { new SessionSecurityState(otherId, "ACTIVE", 2), new(id, "ACTIVE", 0), new(id, "ACTIVE", -1) })
{
    store.ReadOverride = (_, _) => Task.FromResult<SessionSecurityState?>(bad);
    Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.SecurityStateUnavailable);
}
store.ReadOverride = null;
Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.SecurityStateUnavailable);
Check(store.State?.SessionVersion == 2, "false CAS with unchanged ACTIVE row is an unavailable/integrity error, not success");

// Simulate a durable commit whose acknowledgement is lost. Never claim success or retry blindly.
store.AdvanceOverride = (user, version, _) =>
{
    store.Commit(user, version);
    throw new TimeoutException(sensitiveFixture);
};
Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.SecurityStateUnavailable);
Check(store.State?.SessionVersion == 3 && store.Commits == 2, "unknown outcome may have committed; no automatic second increment");
store.AdvanceOverride = null;
Revocation(await revoker.RevokeAllAsync(Principal(2)), SessionRevocationStatus.AlreadyRevoked);
Validation(await validator.ValidateAsync(Principal(2)), SessionValidationStatus.InvalidCredential);
Validation(await validator.ValidateAsync(Principal(3)), SessionValidationStatus.Valid);

using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    var calls = store.Attempts + store.Reads;
    await Cancelled(() => validator.ValidateAsync(Principal(3), cancelled.Token), cancelled.Token);
    await Cancelled(() => revoker.RevokeAllAsync(Principal(3), cancelled.Token), cancelled.Token);
    await Cancelled(() => revoker.RevokeAllAsync(null, cancelled.Token), cancelled.Token);
    Check(store.Attempts + store.Reads == calls, "pre-cancelled operations never call the store");
}
using (var cancellation = new CancellationTokenSource())
{
    store.AdvanceOverride = (_, _, ct) =>
    {
        Check(ct == cancellation.Token, "caller token reaches the atomic writer");
        cancellation.Cancel();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    };
    await Cancelled(() => revoker.RevokeAllAsync(Principal(3), cancellation.Token), cancellation.Token);
}
using (var cancellation = new CancellationTokenSource())
{
    store.AdvanceOverride = (user, version, _) =>
    {
        var changed = store.Commit(user, version);
        cancellation.Cancel();
        return Task.FromResult(changed);
    };
    await Cancelled(() => revoker.RevokeAllAsync(Principal(3), cancellation.Token), cancellation.Token);
    Check(store.State?.SessionVersion == 4, "caller cancellation after commit does not imply rollback");
}
using (var cancellation = new CancellationTokenSource())
{
    store.AdvanceOverride = (_, _, _) => Task.FromResult(false);
    store.ReadOverride = (_, ct) =>
    {
        Check(ct == cancellation.Token, "caller token reaches the committed-state read");
        cancellation.Cancel();
        return Task.FromResult<SessionSecurityState?>(new(id, "ACTIVE", 4));
    };
    await Cancelled(() => revoker.RevokeAllAsync(Principal(3), cancellation.Token), cancellation.Token);
}
store.ReadOverride = null;
store.AdvanceOverride = null;

// Offline transport integration uses the framework JWT handler, not fake authentication.
var key = new SymmetricSecurityKey(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
const string issuer = "offline-session-issuer";
const string audience = "offline-session-audience";
string Token(long? version, bool expired = false, SymmetricSecurityKey? signingKey = null, bool asString = false)
{
    var claims = new List<Claim> { new("userId", id.ToString("D")), new("sub", id.ToString("D")), new("role", "MEDICAL_STAFF") };
    if (version.HasValue)
        claims.Add(asString ? new Claim("sv", version.Value.ToString(CultureInfo.InvariantCulture)) : SessionVersionClaims.Create(version.Value));
    var now = DateTime.UtcNow;
    var jwt = new JwtSecurityToken(issuer, audience, claims, now.AddMinutes(-10),
        expired ? now.AddMinutes(-5) : now.AddMinutes(5), new SigningCredentials(signingKey ?? key, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(jwt);
}
store = new AtomicFakeStore(new(id, "ACTIVE", 4));
validator = new SessionPrincipalValidator(new SessionVersionValidator(store));
revoker = new SessionVersionRevocationService(store);
var services = new ServiceCollection();
services.AddLogging();
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.SaveToken = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (!context.Request.Headers.ContainsKey("Authorization"))
                context.Token = StaffCookieTokenReader.Read(context.Request, true);
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var decision = await validator.ValidateAsync(context.Principal, context.HttpContext.RequestAborted);
            // AuthenticationResult only: a future outer HTTP guard must separately map unavailable
            // to 503 and stop processing. This harness does not claim HTTP 401/503 middleware exists.
            context.HttpContext.Items["offline-session-status"] = decision.Status;
            if (!decision.IsValid) context.Fail(decision.Code);
        }
    };
});
await using var provider = services.BuildServiceProvider();
async Task<AuthenticateResult> Authenticate(string token, bool cookie, string? explicitHeader = null)
{
    await using var scope = provider.CreateAsyncScope();
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    context.Request.Scheme = "https";
    if (cookie) context.Request.Headers.Cookie = StaffSessionCookieDefaults.Name + "=" + token;
    else context.Request.Headers.Authorization = "Bearer " + token;
    if (explicitHeader is not null) context.Request.Headers.Authorization = explicitHeader;
    return await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
}
var currentToken = Token(4);
foreach (var cookie in new[] { false, true })
{
    Check((await Authenticate(currentToken, cookie)).Succeeded, "both transports authenticate with the same version adapter");
    foreach (var bad in new[] { Token(3), Token(5), Token(null), Token(4, asString: true) })
        Check(!(await Authenticate(bad, cookie)).Succeeded, "both transports reject old/future/missing/string version claims");
    foreach (var cryptographicallyBad in new[]
    {
        Token(4, expired: true), Token(4, signingKey: new SymmetricSecurityKey(Enumerable.Repeat((byte)100, 32).ToArray()))
    })
    {
        var reads = store.Reads;
        Check(!(await Authenticate(cryptographicallyBad, cookie)).Succeeded && store.Reads == reads,
            "JWT signature/lifetime rejection happens before the security-state lookup");
    }
    store.ReadOverride = (_, _) => Task.FromException<SessionSecurityState?>(new FakeDbException(sensitiveFixture));
    var outage = await Authenticate(currentToken, cookie);
    Check(!outage.Succeeded && outage.Failure?.Message == "SESSION_SECURITY_STATE_UNAVAILABLE",
        "both transports distinguish an unavailable store without authenticating or leaking its exception");
    store.ReadOverride = null;
}
// Preserve numeric JSON Int64 semantics through the actual framework handler, including its limit.
store.State = new(id, "ACTIVE", long.MaxValue);
Check((await Authenticate(Token(long.MaxValue), false)).Succeeded &&
      (await Authenticate(Token(long.MaxValue), true)).Succeeded,
    "both real JWT handler paths retain a large Int64 without floating point or narrowing");
store.State = new(id, "ACTIVE", 4);
Check(!(await Authenticate(currentToken, true, "Bearer not-a-token")).Succeeded,
    "an invalid explicit Authorization header never falls back to a valid Cookie");
var accepted = await Authenticate(currentToken, false);
Revocation(await revoker.RevokeAllAsync(accepted.Principal), SessionRevocationStatus.Revoked);
Check(!(await Authenticate(currentToken, false)).Succeeded && !(await Authenticate(currentToken, true)).Succeeded,
    "one logout invalidates both previously accepted Bearer and Cookie versions in the shared fake store");
Check((await Authenticate(Token(5), false)).Succeeded && (await Authenticate(Token(5), true)).Succeeded,
    "newly issued persisted version works for both transports");
store.State = new(id, "INACTIVE", 6);
Check(!(await Authenticate(Token(5), false)).Succeeded && !(await Authenticate(Token(5), true)).Succeeded,
    "a disabled account cannot authenticate");
store.State = new(id, "ACTIVE", 7);
Check(!(await Authenticate(Token(5), true)).Succeeded && (await Authenticate(Token(7), true)).Succeeded,
    "re-enable with another version never restores pre-disable credentials");

var oldIssuer = new JwtTokenService(new JwtOptions { Issuer = issuer, Audience = audience, ExpirationMinutes = 30 }, key);
var oldToken = oldIssuer.CreateToken(id, "offline-fixture", "MEDICAL_STAFF").AccessToken;
Check(!new JwtSecurityTokenHandler().ReadJwtToken(oldToken).Claims.Any(c => c.Type == "sv"),
    "existing production issuer remains unchanged and does not emit sv");
Check(!(await Authenticate(oldToken, false)).Succeeded && !(await Authenticate(oldToken, true)).Succeeded,
    "the future adapter rejects legacy tokens, never silently defaulting their version");

foreach (var result in results)
{
    var json = JsonSerializer.Serialize(result);
    using var document = JsonDocument.Parse(json);
    Check(!json.Contains(sensitiveFixture, StringComparison.Ordinal) &&
          document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "Status", "IsRevoked", "Code" }),
        "revocation result contains no account/version/password/hash/token/exception details");
}
Check(typeof(SessionRevocationResult).GetConstructors().Length == 0, "callers cannot construct success or sensitive result metadata");
Check(typeof(User).GetProperty("SessionVersion") is null, "no shared User schema change was made");
var assembly = typeof(SessionVersionRevocationService).Assembly;
Check(!assembly.GetTypes().Any(t => t.IsClass && typeof(ISessionSecurityStateStore).IsAssignableFrom(t)),
    "production assembly has no fake or production store that could accidentally enable revocation");
var parts = new ApplicationPartManager();
parts.ApplicationParts.Add(new AssemblyPart(assembly));
parts.FeatureProviders.Add(new ControllerFeatureProvider());
var controllers = new ControllerFeature();
parts.PopulateFeature(controllers);
var addedTypes = new[] { typeof(SessionVersionClaims), typeof(ISessionPrincipalValidator), typeof(SessionPrincipalValidator),
    typeof(ISessionVersionRevocationStore), typeof(ISessionRevocationService), typeof(SessionVersionRevocationService), typeof(SessionRevocationResult) };
Check(!controllers.Controllers.Any(c => c.DeclaredConstructors.Any(ctor => ctor.GetParameters().Any(p => addedTypes.Contains(p.ParameterType)))),
    "no production controller depends on the unregistered future components");
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "EarlyInterventionCare.Api", "Program.cs"))) root = root.Parent;
Check(root is not null, "find sources without starting the runtime host");
foreach (var path in new[] { "Program.cs", "Controllers/StaffSessionController.cs", "Services/Authentication/AuthenticationService.cs",
    "Services/Authentication/JwtTokenService.cs", "Services/Authentication/StaffSessionCookieService.cs", "Security/StaffCookieCsrfMiddleware.cs" })
{
    var source = File.ReadAllText(Path.Combine(root!.FullName, "EarlyInterventionCare.Api", path));
    Check(addedTypes.All(type => !source.Contains(type.Name, StringComparison.Ordinal)),
        "no production DI, token issuance, logout, Cookie or CSRF integration was enabled");
}
Console.WriteLine($"PASS: {checks} offline session revocation checks; fake atomic store and real in-process JWT handlers only; no MySQL/HTTP/production activation.");

sealed class AtomicFakeStore(SessionSecurityState? initial) : ISessionVersionRevocationStore
{
    private readonly object _gate = new();
    private SessionSecurityState? _state = initial;
    private int _reads, _attempts, _commits;
    public int Reads => _reads;
    public int Attempts => _attempts;
    public int Commits => _commits;
    public SessionSecurityState? State { get { lock (_gate) return _state; } set { lock (_gate) _state = value; } }
    public Func<Guid, CancellationToken, Task<SessionSecurityState?>>? ReadOverride { get; set; }
    public Func<Guid, long, CancellationToken, Task<bool>>? AdvanceOverride { get; set; }
    public Task<SessionSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        cancellationToken.ThrowIfCancellationRequested();
        return ReadOverride is { } read ? read(userId, cancellationToken) :
            Task.FromResult(State is { } state && state.UserId == userId ? state : null);
    }
    public Task<bool> TryAdvanceAsync(Guid userId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);
        cancellationToken.ThrowIfCancellationRequested();
        return AdvanceOverride is { } advance ? advance(userId, expectedVersion, cancellationToken) :
            Task.FromResult(Commit(userId, expectedVersion));
    }
    public bool Commit(Guid userId, long expectedVersion)
    {
        if (userId == Guid.Empty || expectedVersion <= 0 || expectedVersion == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        lock (_gate)
        {
            if (_state is null || _state.UserId != userId || _state.Status != "ACTIVE" || _state.SessionVersion != expectedVersion)
                return false;
            _state = _state with { SessionVersion = checked(expectedVersion + 1) };
            Interlocked.Increment(ref _commits);
            return true;
        }
    }
}
sealed class FakeDbException(string message) : DbException(message);
