namespace EarlyInterventionCare.Api.Services.Authentication;

public enum SessionValidationStatus
{
    InvalidCredential = 0,
    Valid = 1,
    SecurityStateUnavailable = 2
}

/// <summary>Fixed validation metadata only; HTTP mapping belongs to the future integration layer.</summary>
public sealed class SessionValidationResult
{
    private SessionValidationResult(SessionValidationStatus status, string code)
    {
        Status = status;
        Code = code;
    }

    public SessionValidationStatus Status { get; }
    public bool IsValid => Status == SessionValidationStatus.Valid;
    public string Code { get; }

    // Generic invalid-credential metadata does not reveal account existence, status or versions.
    internal static SessionValidationResult Invalid { get; } =
        new(SessionValidationStatus.InvalidCredential, "SESSION_INVALID");
    internal static SessionValidationResult Allowed { get; } =
        new(SessionValidationStatus.Valid, "SESSION_VALID");
    internal static SessionValidationResult Unavailable { get; } =
        new(SessionValidationStatus.SecurityStateUnavailable, "SESSION_SECURITY_STATE_UNAVAILABLE");
}
