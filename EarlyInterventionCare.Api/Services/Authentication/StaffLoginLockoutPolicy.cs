namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Immutable parameters for the offline lockout core; not runtime configuration or DI.</summary>
public sealed class StaffLoginLockoutPolicy
{
    public StaffLoginLockoutPolicy(int failureLimit = 5, TimeSpan? failureWindow = null,
        TimeSpan? lockoutDuration = null)
    {
        if (failureLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureLimit), "Failure limit must be positive.");

        var window = failureWindow ?? TimeSpan.FromMinutes(15);
        var duration = lockoutDuration ?? TimeSpan.FromMinutes(15);
        var utcRange = DateTimeOffset.MaxValue - DateTimeOffset.MinValue;
        if (window <= TimeSpan.Zero || window > utcRange)
            throw new ArgumentOutOfRangeException(nameof(failureWindow), "Failure window is outside the supported range.");
        if (duration <= TimeSpan.Zero || duration > utcRange)
            throw new ArgumentOutOfRangeException(nameof(lockoutDuration), "Lockout duration is outside the supported range.");

        FailureLimit = failureLimit;
        FailureWindow = window;
        LockoutDuration = duration;
    }

    public int FailureLimit { get; }
    public TimeSpan FailureWindow { get; }
    public TimeSpan LockoutDuration { get; }
}
