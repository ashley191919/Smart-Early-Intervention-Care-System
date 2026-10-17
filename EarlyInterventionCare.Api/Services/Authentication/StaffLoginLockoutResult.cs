namespace EarlyInterventionCare.Api.Services.Authentication;

public enum StaffLoginLockoutStatus
{
    Locked = 0,
    AttemptAllowed = 1,
    FailureRecorded = 2,
    LockoutExpired = 3,
    SuccessReset = 4
}

/// <summary>
/// Internal policy decision and proposed next state only, never an anonymous API error body.
/// CanAttempt means a subsequent credential check is permitted, not that credentials are correct.
/// The returned state is not persisted or concurrency-protected by this core.
/// </summary>
public sealed class StaffLoginLockoutResult
{
    internal StaffLoginLockoutResult(StaffLoginLockoutStatus status, StaffLoginLockoutState state)
    {
        Status = status;
        State = state;
    }

    public StaffLoginLockoutStatus Status { get; }
    public bool CanAttempt => Status != StaffLoginLockoutStatus.Locked;
    public StaffLoginLockoutState State { get; }
}
