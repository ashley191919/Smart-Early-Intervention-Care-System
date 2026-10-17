namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// Pure state transitions with an injected clock; no account lookup, BCrypt, persistence or DI hookup.
/// Only a trusted future coordinator may supply state and credential outcomes. It must serialize
/// check/verification/update, deduplicate attempts, handle store failures and audit unlocks separately.
/// Anonymous responses must not reveal these decisions, counters, timestamps or account existence.
/// </summary>
public sealed class StaffLoginLockoutService
{
    private readonly StaffLoginLockoutPolicy _policy;
    private readonly TimeProvider _clock;

    public StaffLoginLockoutService(StaffLoginLockoutPolicy policy, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(clock);
        _policy = policy;
        _clock = clock;
    }

    public StaffLoginLockoutResult CheckAttempt(StaffLoginLockoutState state) =>
        Evaluate(state, _clock.GetUtcNow().ToUniversalTime());

    public StaffLoginLockoutResult RecordFailure(StaffLoginLockoutState state)
    {
        var now = _clock.GetUtcNow().ToUniversalTime();
        var evaluated = Evaluate(state, now);
        if (!evaluated.CanAttempt) return evaluated;

        var next = new StaffLoginLockoutState(evaluated.State.FailedAtUtc.Append(now));
        return next.FailedAtUtc.Count >= _policy.FailureLimit
            ? Lock(next, now)
            : new(StaffLoginLockoutStatus.FailureRecorded, next);
    }

    /// <summary>
    /// Call only after a real successful credential check under the future atomic-update protocol.
    /// A stale/injected success event cannot clear an active lock. This method is not admin unlock.
    /// </summary>
    public StaffLoginLockoutResult RecordSuccess(StaffLoginLockoutState state)
    {
        var evaluated = Evaluate(state, _clock.GetUtcNow().ToUniversalTime());
        return evaluated.CanAttempt
            ? new(StaffLoginLockoutStatus.SuccessReset, new StaffLoginLockoutState())
            : evaluated;
    }

    private StaffLoginLockoutResult Evaluate(StaffLoginLockoutState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FailedAtUtc.Any(time => time > now))
            throw new InvalidOperationException("Failure timestamps cannot be ahead of the trusted clock.");

        if (state.LockedUntilUtc is DateTimeOffset end)
        {
            if (now < end)
                return new(StaffLoginLockoutStatus.Locked, state);

            // At exact expiry, start a fresh attempt sequence even if the window is longer than the lock.
            return new(StaffLoginLockoutStatus.LockoutExpired, new StaffLoginLockoutState());
        }

        // Sliding window: (now - FailureWindow, now]. The exact lower boundary has expired.
        var retained = new StaffLoginLockoutState(state.FailedAtUtc.Where(time => now - time < _policy.FailureWindow));
        // Also handles a restored snapshot evaluated under a newly lowered threshold.
        return retained.FailedAtUtc.Count >= _policy.FailureLimit
            ? Lock(retained, now)
            : new(StaffLoginLockoutStatus.AttemptAllowed, retained);
    }

    private StaffLoginLockoutResult Lock(StaffLoginLockoutState state, DateTimeOffset now)
    {
        if (_policy.LockoutDuration > DateTimeOffset.MaxValue - now)
            throw new InvalidOperationException("Lockout end exceeds the supported UTC range.");

        // Normal output retains at most FailureLimit timestamps. Rejected attempts never extend the lock.
        var locked = new StaffLoginLockoutState(state.FailedAtUtc.TakeLast(_policy.FailureLimit),
            now + _policy.LockoutDuration);
        return new(StaffLoginLockoutStatus.Locked, locked);
    }
}
