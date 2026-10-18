using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using EarlyInterventionCare.Api.Options;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.IdentityModel.Tokens;

// Standalone core tests: no runtime host, MySQL, production Store, middleware registration or HTTP mapping.
var checks = 0;
var results = new List<SessionValidationResult>();
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Outcome(SessionValidationResult result, SessionValidationStatus expected, string description)
{
    results.Add(result);
    Check(result.Status == expected && result.IsValid == (expected == SessionValidationStatus.Valid), description);
    var expectedCode = expected switch
    {
        SessionValidationStatus.Valid => "SESSION_VALID",
        SessionValidationStatus.InvalidCredential => "SESSION_INVALID",
        _ => "SESSION_SECURITY_STATE_UNAVAILABLE"
    };
    Check(result.Code == expectedCode, "result uses fixed non-sensitive metadata");
}
async Task Cancelled(Func<Task<SessionValidationResult>> action, CancellationToken token, string description)
{
    try { await action(); }
    catch (OperationCanceledException exception)
    {
        Check(token.IsCancellationRequested && exception.CancellationToken == token, description);
        return;
    }
    Check(false, description);
}

var userId = Guid.NewGuid();
var state = new SessionSecurityState(userId, "ACTIVE", 7);
var store = new FakeStore((_, _) => Task.FromResult<SessionSecurityState?>(state));
ISessionVersionValidator validator = new SessionVersionValidator(store);
Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.Valid, "ACTIVE matching version is allowed");
Outcome(await validator.ValidateAsync(userId, 6), SessionValidationStatus.InvalidCredential, "older token is rejected");
Outcome(await validator.ValidateAsync(userId, 8), SessionValidationStatus.InvalidCredential, "future token is rejected");
Check(store.Calls == 3 && store.LastUserId == userId, "every valid input re-queries the requested user's current state");

foreach (var status in new[] { "INACTIVE", "active", "ACTIVE ", "PENDING", "", null })
{
    state = new SessionSecurityState(userId, status!, 7);
    Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.InvalidCredential,
        "only exact ordinal ACTIVE is accepted");
}
store.Read = (_, _) => Task.FromResult<SessionSecurityState?>(null);
Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.InvalidCredential, "absent user is rejected");
var callsBeforeInvalidInput = store.Calls;
Outcome(await validator.ValidateAsync(Guid.Empty, 7), SessionValidationStatus.InvalidCredential, "empty user ID is rejected");
foreach (var version in new[] { 0L, -1L, long.MinValue })
    Outcome(await validator.ValidateAsync(userId, version), SessionValidationStatus.InvalidCredential, "nonpositive token version is rejected");
Check(store.Calls == callsBeforeInvalidInput, "invalid token values do not query the store");

foreach (var corruptState in new[]
{
    new SessionSecurityState(Guid.NewGuid(), "ACTIVE", 7),
    new SessionSecurityState(Guid.Empty, "ACTIVE", 7),
    new SessionSecurityState(userId, "ACTIVE", 0),
    new SessionSecurityState(userId, "ACTIVE", -1)
})
{
    store.Read = (_, _) => Task.FromResult<SessionSecurityState?>(corruptState);
    Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.SecurityStateUnavailable,
        "wrong-user or malformed store data cannot authorize a request");
}

const string sensitiveFixture = "synthetic-password-hash-connection-detail";
foreach (var exception in new Exception[]
{
    new FakeDbException(sensitiveFixture), new TimeoutException(sensitiveFixture),
    new InvalidOperationException(sensitiveFixture, new FakeDbException(sensitiveFixture)),
    new InvalidOperationException(sensitiveFixture), new OperationCanceledException(sensitiveFixture)
})
{
    store.Read = (_, _) => Task.FromException<SessionSecurityState?>(exception);
    var result = await validator.ValidateAsync(userId, 7);
    Outcome(result, SessionValidationStatus.SecurityStateUnavailable, "store failure is unavailable, never success or invalid credentials");
    Check(!JsonSerializer.Serialize(result).Contains(sensitiveFixture, StringComparison.Ordinal),
        "provider exception details do not appear in the result");
}
store.Read = (_, _) => throw new FakeDbException(sensitiveFixture);
Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.SecurityStateUnavailable,
    "synchronous store failure also fails closed");

using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    var callsBeforeCancellation = store.Calls;
    await Cancelled(() => validator.ValidateAsync(userId, 7, cancellation.Token), cancellation.Token,
        "pre-cancelled caller propagates cancellation");
    await Cancelled(() => validator.ValidateAsync(Guid.Empty, 0, cancellation.Token), cancellation.Token,
        "pre-cancelled invalid input also propagates cancellation");
    Check(store.Calls == callsBeforeCancellation, "pre-cancelled calls never query the store");
}
using (var cancellation = new CancellationTokenSource())
{
    store.Read = (_, ct) =>
    {
        cancellation.Cancel();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SessionSecurityState?>(state);
    };
    await Cancelled(() => validator.ValidateAsync(userId, 7, cancellation.Token), cancellation.Token,
        "store observes caller cancellation without converting it to unavailable");
    Check(store.LastCancellationToken == cancellation.Token, "caller token reaches the store unchanged");
}
using (var cancellation = new CancellationTokenSource())
{
    store.Read = (_, _) =>
    {
        cancellation.Cancel();
        return Task.FromResult<SessionSecurityState?>(new(userId, "ACTIVE", 7));
    };
    await Cancelled(() => validator.ValidateAsync(userId, 7, cancellation.Token), cancellation.Token,
        "a store ignoring cancellation cannot return validation success");
}
using (var cancellation = new CancellationTokenSource())
{
    store.Read = (_, _) =>
    {
        cancellation.Cancel();
        throw new FakeDbException(sensitiveFixture);
    };
    await Cancelled(() => validator.ValidateAsync(userId, 7, cancellation.Token), cancellation.Token,
        "caller cancellation takes precedence over a concurrent store failure");
}
using (var cancellation = new CancellationTokenSource())
{
    state = new(userId, "ACTIVE", long.MaxValue);
    store.Read = (_, _) => Task.FromResult<SessionSecurityState?>(state);
    Outcome(await validator.ValidateAsync(userId, long.MaxValue, cancellation.Token), SessionValidationStatus.Valid,
        "positive Int64 versions are compared without overflow or narrowing");
    Check(store.LastCancellationToken == cancellation.Token, "non-cancelled caller token is forwarded");
}

state = new(userId, "ACTIVE", 7);
store.Read = (_, _) => Task.FromResult<SessionSecurityState?>(state);
Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.Valid, "initial state is accepted");
state = new(userId, "INACTIVE", 8);
Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.InvalidCredential, "subsequent lookup sees disable and version change");
state = new(userId, "ACTIVE", 9);
Outcome(await validator.ValidateAsync(userId, 7), SessionValidationStatus.InvalidCredential, "re-enable does not restore the old token");
Outcome(await validator.ValidateAsync(userId, 9), SessionValidationStatus.Valid, "new version after re-enable is accepted");

var otherUserId = Guid.NewGuid();
store.Read = async (id, ct) =>
{
    await Task.Yield();
    ct.ThrowIfCancellationRequested();
    return new SessionSecurityState(id, "ACTIVE", id == userId ? 7 : 9);
};
var concurrent = await Task.WhenAll(validator.ValidateAsync(userId, 7), validator.ValidateAsync(otherUserId, 7));
Outcome(concurrent[0], SessionValidationStatus.Valid, "concurrent user lookup keeps its own state");
Outcome(concurrent[1], SessionValidationStatus.InvalidCredential, "another user cannot share the first user's version result");

Check(typeof(SessionSecurityState).GetProperties().Select(p => p.Name).ToHashSet()
    .SetEquals(new[] { "UserId", "Status", "SessionVersion" }), "store snapshot contains no password/hash or claimed permissions");
Check(typeof(SessionValidationResult).GetProperties().Select(p => p.Name).ToHashSet()
    .SetEquals(new[] { "Status", "IsValid", "Code" }), "result exposes only decision metadata");
Check(typeof(SessionValidationResult).GetConstructors().Length == 0, "result cannot be initialized with arbitrary sensitive metadata");
foreach (var result in results)
{
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
    Check(json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "Status", "IsValid", "Code" }),
        "serialized result contains neither a password/hash nor internal store data");
}

var tokens = new JwtTokenService(new JwtOptions { Issuer = "fixture-issuer", Audience = "fixture-audience", ExpirationMinutes = 30 },
    new SymmetricSecurityKey(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.CreateToken(userId, "fixture-staff", "MEDICAL_STAFF").AccessToken);
Check(jwt.Claims.Select(c => c.Type).ToHashSet().SetEquals(new[]
{
    "sub", "userId", "username", "role", "jti", "iat", "nbf", "exp", "iss", "aud"
}), "existing JWT issuance retains the exact claim set and does not yet add sv");
Check(jwt.Claims.Single(c => c.Type == "userId").Value == userId.ToString("D") &&
    jwt.Claims.Single(c => c.Type == "role").Value == "MEDICAL_STAFF", "existing userId and role claims retain their values");

var apiAssembly = typeof(SessionVersionValidator).Assembly;
Check(!apiAssembly.GetTypes().Any(t => t.IsClass && typeof(ISessionSecurityStateStore).IsAssignableFrom(t)),
    "production assembly has no real or fake store implementation");
var parts = new ApplicationPartManager();
parts.ApplicationParts.Add(new AssemblyPart(apiAssembly));
parts.FeatureProviders.Add(new ControllerFeatureProvider());
var controllers = new ControllerFeature();
parts.PopulateFeature(controllers);
Check(controllers.Controllers.Count > 0, "endpoint check discovers existing API controllers");
var newTypes = new[] { typeof(ISessionVersionValidator), typeof(SessionVersionValidator), typeof(ISessionSecurityStateStore),
    typeof(SessionSecurityState), typeof(SessionValidationResult) };
bool UsesCore(Type type) => newTypes.Contains(type) || (type.IsGenericType && type.GetGenericArguments().Any(UsesCore));
Check(!controllers.Controllers.Any(c => c.Name.Contains("SessionVersion", StringComparison.OrdinalIgnoreCase) ||
    c.DeclaredMethods.Any(m => UsesCore(m.ReturnType) || m.GetParameters().Any(p => UsesCore(p.ParameterType))) ||
    c.DeclaredConstructors.Any(ctor => ctor.GetParameters().Any(p => UsesCore(p.ParameterType)))),
    "no controller exposes or consumes the session version core");

var directory = new DirectoryInfo(AppContext.BaseDirectory);
while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EarlyInterventionCare.Api", "Program.cs")))
    directory = directory.Parent;
Check(directory is not null, "integration checks locate repository sources without starting the application");
var apiRoot = Path.Combine(directory!.FullName, "EarlyInterventionCare.Api");
foreach (var relativePath in new[]
{
    "Program.cs", "Services/Authentication/AuthenticationService.cs", "Services/Authentication/JwtTokenService.cs",
    "Services/Authentication/StaffSessionCookieService.cs", "Security/StaffCookieCsrfMiddleware.cs"
})
{
    var source = File.ReadAllText(Path.Combine(apiRoot, relativePath));
    Check(newTypes.All(t => !source.Contains(t.Name, StringComparison.Ordinal)),
        "existing runtime files have no core registration or integration");
}

Console.WriteLine($"PASS: {checks} offline session version checks; no DB or production JWT/Cookie integration enabled.");

sealed class FakeStore(Func<Guid, CancellationToken, Task<SessionSecurityState?>> read) : ISessionSecurityStateStore
{
    private int _calls;
    public Func<Guid, CancellationToken, Task<SessionSecurityState?>> Read { get; set; } = read;
    public int Calls => _calls;
    public Guid LastUserId { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public Task<SessionSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        LastUserId = userId;
        LastCancellationToken = cancellationToken;
        return Read(userId, cancellationToken);
    }
}

sealed class FakeDbException(string message) : DbException(message);
