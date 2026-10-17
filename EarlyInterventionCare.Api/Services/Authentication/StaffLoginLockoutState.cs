namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// Internal immutable snapshot, not a request/response DTO. Sliding-window history needs individual
/// timestamps, not just a count and window start. No identity, password or account-existence flag is held.
/// </summary>
public sealed class StaffLoginLockoutState
{
    public StaffLoginLockoutState(IEnumerable<DateTimeOffset>? failedAtUtc = null,
        DateTimeOffset? lockedUntilUtc = null)
    {
        // Copy before exposing a read-only view; callers cannot mutate an already-evaluated snapshot.
        FailedAtUtc = Array.AsReadOnly((failedAtUtc ?? Array.Empty<DateTimeOffset>())
            .Select(time => time.ToUniversalTime()).OrderBy(time => time).ToArray());
        LockedUntilUtc = lockedUntilUtc?.ToUniversalTime();
    }

    public IReadOnlyList<DateTimeOffset> FailedAtUtc { get; }
    public DateTimeOffset? LockedUntilUtc { get; }
}
