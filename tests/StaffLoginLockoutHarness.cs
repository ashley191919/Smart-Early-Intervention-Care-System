using System.Text.Json;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

// Offline state transitions only. No host, account lookup, database, persistence or concurrency protocol.
var checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Throws<T>(Action action, string description) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, description); return; }
    Check(false, description);
}
void Decision(StaffLoginLockoutResult result, StaffLoginLockoutStatus expected, string description)
{
    Check(result.Status == expected && result.CanAttempt == (expected != StaffLoginLockoutStatus.Locked), description);
}

var start = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
var clock = new ManualClock(start);
var policy = new StaffLoginLockoutPolicy();
var service = new StaffLoginLockoutService(policy, clock);
Check(policy.FailureLimit == 5 && policy.FailureWindow == TimeSpan.FromMinutes(15) &&
    policy.LockoutDuration == TimeSpan.FromMinutes(15), "default policy is five failures in fifteen minutes with fifteen-minute lock");
var empty = new StaffLoginLockoutState();
Decision(service.CheckAttempt(empty), StaffLoginLockoutStatus.AttemptAllowed, "empty state permits credential verification");
var state = empty;
for (var attempt = 1; attempt <= 4; attempt++)
{
    clock.Now = start.AddMinutes(attempt - 1);
    var result = service.RecordFailure(state);
    Decision(result, StaffLoginLockoutStatus.FailureRecorded, "failure below threshold is recorded without locking");
    Check(result.State.FailedAtUtc.Count == attempt && result.State.LockedUntilUtc is null,
        "each permitted failure adds exactly one timestamp");
    Check(state.FailedAtUtc.Count == attempt - 1, "input state is not modified");
    state = result.State;
}
Decision(service.CheckAttempt(state), StaffLoginLockoutStatus.AttemptAllowed, "four failures still permit a credential attempt");
clock.Now = start.AddMinutes(4);
var locked = service.RecordFailure(state);
Decision(locked, StaffLoginLockoutStatus.Locked, "fifth failure locks the account state");
Check(locked.State.FailedAtUtc.Count == 5 && locked.State.LockedUntilUtc == start.AddMinutes(19),
    "lock duration begins at the threshold-crossing failure");
var end = locked.State.LockedUntilUtc!.Value;
clock.Now = end.AddTicks(-1);
Decision(service.CheckAttempt(locked.State), StaffLoginLockoutStatus.Locked, "one tick before expiry remains locked");
var deniedFailure = service.RecordFailure(locked.State);
Decision(deniedFailure, StaffLoginLockoutStatus.Locked, "failures during lock are rejected");
Check(ReferenceEquals(deniedFailure.State, locked.State) && deniedFailure.State.LockedUntilUtc == end &&
    deniedFailure.State.FailedAtUtc.Count == 5, "blocked attempts neither add failures nor extend the lock");
var deniedSuccess = service.RecordSuccess(locked.State);
Decision(deniedSuccess, StaffLoginLockoutStatus.Locked, "success event cannot bypass an active lock");
Check(ReferenceEquals(deniedSuccess.State, locked.State), "active lock survives a stale success event");
clock.Now = end;
var expired = service.CheckAttempt(locked.State);
Decision(expired, StaffLoginLockoutStatus.LockoutExpired, "exact lock expiry permits a fresh attempt");
Check(expired.State.FailedAtUtc.Count == 0 && expired.State.LockedUntilUtc is null,
    "expired lock clears the previous failure sequence");
Decision(service.CheckAttempt(expired.State), StaffLoginLockoutStatus.AttemptAllowed, "persisted expiry transition becomes ordinary allowed state");
var afterExpiry = service.RecordFailure(locked.State);
Decision(afterExpiry, StaffLoginLockoutStatus.FailureRecorded, "first failure at expiry starts a fresh sequence");
Check(afterExpiry.State.FailedAtUtc.Count == 1, "expired lock does not immediately relock on one failure");
clock.Now = end.AddDays(1);
Decision(service.CheckAttempt(locked.State), StaffLoginLockoutStatus.LockoutExpired, "long-expired lock also recovers");

clock.Now = start.AddMinutes(15).AddTicks(-1);
var singleFailure = new StaffLoginLockoutState(new[] { start });
Check(service.CheckAttempt(singleFailure).State.FailedAtUtc.Count == 1, "failure one tick inside window remains counted");
clock.Now = start.AddMinutes(15);
Check(service.CheckAttempt(singleFailure).State.FailedAtUtc.Count == 0, "failure exactly at lower window boundary expires");
Check(service.RecordFailure(singleFailure).State.FailedAtUtc.Count == 1, "failure at a fresh window counts only the new event");
clock.Now = start.AddMinutes(15).AddTicks(1);
Check(service.CheckAttempt(singleFailure).State.FailedAtUtc.Count == 0, "failure beyond window remains expired");

// A rolling window must retain later failures after the oldest has expired.
var rolling = new StaffLoginLockoutState(new[] { start, start.AddMinutes(5), start.AddMinutes(10), start.AddMinutes(14) });
clock.Now = start.AddMinutes(15);
var rollingFourth = service.RecordFailure(rolling);
Decision(rollingFourth, StaffLoginLockoutStatus.FailureRecorded, "expired oldest failure does not discard the later history");
Check(rollingFourth.State.FailedAtUtc.Count == 4 && rollingFourth.State.FailedAtUtc[0] == start.AddMinutes(5),
    "sliding window retains all four in-window failures");
clock.Now = clock.Now.AddTicks(1);
Decision(service.RecordFailure(rollingFourth.State), StaffLoginLockoutStatus.Locked,
    "fifth recent failure locks even across an earlier fixed-window boundary");

clock.Now = start.AddMinutes(3);
var success = service.RecordSuccess(state);
Decision(success, StaffLoginLockoutStatus.SuccessReset, "verified success below threshold resets failures");
Check(success.State.FailedAtUtc.Count == 0 && success.State.LockedUntilUtc is null && state.FailedAtUtc.Count == 4,
    "success proposes empty state without modifying historical input");
Check(service.RecordFailure(success.State).State.FailedAtUtc.Count == 1, "next failure after success begins at one");
Decision(service.RecordSuccess(empty), StaffLoginLockoutStatus.SuccessReset, "success on empty history is safe");

// Equal timestamps still represent separate failures; operations read the clock only once each.
clock.Now = start;
var sameInstant = empty;
for (var i = 0; i < 5; i++) sameInstant = service.RecordFailure(sameInstant).State;
Decision(service.CheckAttempt(sameInstant), StaffLoginLockoutStatus.Locked, "separate attempts at the same instant count independently");
var readsBefore = clock.Reads;
service.RecordFailure(empty);
Check(clock.Reads == readsBefore + 1, "one transition uses one time snapshot");

var shortService = new StaffLoginLockoutService(new StaffLoginLockoutPolicy(2, TimeSpan.FromHours(1), TimeSpan.FromSeconds(10)), clock);
var shortLock = shortService.RecordFailure(shortService.RecordFailure(empty).State);
clock.Now = start.AddSeconds(10);
var shortExpired = shortService.CheckAttempt(shortLock.State);
Decision(shortExpired, StaffLoginLockoutStatus.LockoutExpired, "configurable shorter lock expires independently of the longer window");
Check(shortService.RecordFailure(shortLock.State).State.FailedAtUtc.Count == 1,
    "short-lock expiry clears still-young failures to avoid immediate relock");
clock.Now = start;
var oneFailureService = new StaffLoginLockoutService(new StaffLoginLockoutPolicy(1, TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)), clock);
var tickLock = oneFailureService.RecordFailure(empty);
Decision(tickLock, StaffLoginLockoutStatus.Locked, "threshold one and one-tick intervals are supported");
clock.Now = start.AddTicks(1);
Decision(oneFailureService.CheckAttempt(tickLock.State), StaffLoginLockoutStatus.LockoutExpired, "smallest interval honors exact expiry");

var lowered = new StaffLoginLockoutService(new StaffLoginLockoutPolicy(2), clock);
clock.Now = start.AddMinutes(3);
var loweredLock = lowered.CheckAttempt(state);
Decision(loweredLock, StaffLoginLockoutStatus.Locked, "lowered policy threshold does not allow an over-limit restored state");
Check(loweredLock.State.FailedAtUtc.Count == 2, "threshold change bounds the retained lock history");

var sourceTimes = new List<DateTimeOffset> { start.AddMinutes(1), start };
var copied = new StaffLoginLockoutState(sourceTimes);
sourceTimes.Clear();
Check(copied.FailedAtUtc.Count == 2 && copied.FailedAtUtc[0] == start, "state copies and orders supplied history");
Throws<NotSupportedException>(() => ((IList<DateTimeOffset>)copied.FailedAtUtc).Add(start), "failure history is externally read-only");
clock.Now = start.AddTicks(-1);
Throws<InvalidOperationException>(() => service.CheckAttempt(singleFailure), "future failure timestamps or clock rollback fail closed");
clock.Now = DateTimeOffset.MaxValue;
Throws<InvalidOperationException>(() => oneFailureService.RecordFailure(empty), "unrepresentable lock end fails instead of wrapping");

// Midnight and UTC offsets are compared as instants, not machine-local clock values.
var midnight = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero).AddTicks(-1);
clock.Now = midnight;
var midnightLock = oneFailureService.RecordFailure(empty);
Check(midnightLock.State.LockedUntilUtc == midnight.AddTicks(1), "UTC day boundary uses exact elapsed time");
clock.Now = midnight.AddDays(1);
Decision(oneFailureService.CheckAttempt(midnightLock.State), StaffLoginLockoutStatus.LockoutExpired, "next UTC day does not preserve an expired lock");
clock.Now = start.ToOffset(TimeSpan.FromHours(8));
var offsetLock = oneFailureService.RecordFailure(empty);
Check(offsetLock.State.FailedAtUtc[0] == start && offsetLock.State.FailedAtUtc[0].Offset == TimeSpan.Zero &&
    offsetLock.State.LockedUntilUtc!.Value.Offset == TimeSpan.Zero, "clock offsets normalize to UTC in resulting state");
var offsetState = new StaffLoginLockoutState(new[] { start.ToOffset(TimeSpan.FromHours(-4)) },
    start.AddMinutes(15).ToOffset(TimeSpan.FromHours(8)));
Check(offsetState.FailedAtUtc[0].Offset == TimeSpan.Zero && offsetState.LockedUntilUtc!.Value.Offset == TimeSpan.Zero,
    "restored state also normalizes offsets to UTC");
clock.Now = start.AddMinutes(15).ToOffset(TimeSpan.FromHours(-4));
Decision(service.CheckAttempt(offsetState), StaffLoginLockoutStatus.LockoutExpired, "equivalent instants honor expiry across offsets");

foreach (var limit in new[] { 0, -1, int.MinValue })
    Throws<ArgumentOutOfRangeException>(() => new StaffLoginLockoutPolicy(limit), "nonpositive thresholds are rejected");
foreach (var interval in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.MaxValue })
{
    Throws<ArgumentOutOfRangeException>(() => new StaffLoginLockoutPolicy(failureWindow: interval), "invalid window parameter is rejected");
    Throws<ArgumentOutOfRangeException>(() => new StaffLoginLockoutPolicy(lockoutDuration: interval), "invalid lock duration parameter is rejected");
}
Throws<ArgumentNullException>(() => new StaffLoginLockoutService(null!, clock), "missing policy is rejected");
Throws<ArgumentNullException>(() => new StaffLoginLockoutService(policy, null!), "missing clock is rejected");
Throws<ArgumentNullException>(() => service.CheckAttempt(null!), "missing state is rejected");

Check(typeof(StaffLoginLockoutState).GetProperties().Select(p => p.Name).ToHashSet()
    .SetEquals(new[] { "FailedAtUtc", "LockedUntilUtc" }), "state has only timestamps, no password/hash, identity or account-existence flag");
Check(typeof(StaffLoginLockoutResult).GetProperties().Select(p => p.Name).ToHashSet()
    .SetEquals(new[] { "Status", "CanAttempt", "State" }), "result has only internal decision and next state");
var decisionJson = JsonSerializer.Serialize(locked);
Check(!decisionJson.Contains("password", StringComparison.OrdinalIgnoreCase) &&
    !decisionJson.Contains("username", StringComparison.OrdinalIgnoreCase), "serialized internal decision contains no credential fields");
Check(typeof(StaffLoginLockoutService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
    .SequenceEqual(new[] { typeof(StaffLoginLockoutPolicy), typeof(TimeProvider) }), "core requires only policy and clock, no store/EF/MySQL dependency");
Check(typeof(StaffLoginLockoutService).GetMethods().Where(m => m.DeclaringType == typeof(StaffLoginLockoutService))
    .All(m => m.GetParameters().All(p => p.ParameterType == typeof(StaffLoginLockoutState))),
    "public transition methods cannot accept or retain a password or client-declared identity");

var assembly = typeof(StaffLoginLockoutService).Assembly;
var parts = new ApplicationPartManager();
parts.ApplicationParts.Add(new AssemblyPart(assembly));
parts.FeatureProviders.Add(new ControllerFeatureProvider());
var controllers = new ControllerFeature();
parts.PopulateFeature(controllers);
Check(controllers.Controllers.Count > 0, "integration guard inspects actual API controllers");
var coreTypes = new[] { typeof(StaffLoginLockoutService), typeof(StaffLoginLockoutPolicy), typeof(StaffLoginLockoutState), typeof(StaffLoginLockoutResult) };
bool UsesCore(Type type) => coreTypes.Contains(type) || (type.IsGenericType && type.GetGenericArguments().Any(UsesCore));
Check(!controllers.Controllers.Any(c => c.Name.Contains("Lockout", StringComparison.OrdinalIgnoreCase) ||
    c.DeclaredMethods.Any(m => UsesCore(m.ReturnType) || m.GetParameters().Any(p => UsesCore(p.ParameterType))) ||
    c.DeclaredConstructors.Any(ctor => ctor.GetParameters().Any(p => UsesCore(p.ParameterType)))),
    "no existing controller exposes or consumes lockout logic");

var directory = new DirectoryInfo(AppContext.BaseDirectory);
while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EarlyInterventionCare.Api", "Program.cs")))
    directory = directory.Parent;
Check(directory is not null, "integration guard locates sources without starting a host");
foreach (var file in new[] { "Program.cs", "Services/Authentication/AuthenticationService.cs", "Controllers/AuthController.cs", "Controllers/StaffSessionController.cs" })
{
    var source = File.ReadAllText(Path.Combine(directory!.FullName, "EarlyInterventionCare.Api", file));
    Check(coreTypes.All(t => !source.Contains(t.Name, StringComparison.Ordinal)), "runtime login and DI sources do not reference the lockout core");
}

Console.WriteLine($"PASS: {checks} offline staff login lockout checks; no persistence, concurrency or production login integration tested.");

sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public int Reads { get; private set; }
    public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
}
