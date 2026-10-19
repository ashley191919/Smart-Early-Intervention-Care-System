namespace EarlyInterventionCare.Api.Services.Authentication;

public enum SessionRevocationStatus
{
    InvalidCredential = 0,
    Revoked = 1,
    AlreadyRevoked = 2,
    SecurityStateUnavailable = 3
}

/// <summary>Fixed metadata only: never includes account, version, token or provider exception data.</summary>
public sealed class SessionRevocationResult
{
    private SessionRevocationResult(SessionRevocationStatus status, string code)
    {
        Status = status;
        Code = code;
    }

    public SessionRevocationStatus Status { get; }
    public bool IsRevoked => Status is SessionRevocationStatus.Revoked or SessionRevocationStatus.AlreadyRevoked;
    public string Code { get; }

    internal static SessionRevocationResult Invalid { get; } =
        new(SessionRevocationStatus.InvalidCredential, "SESSION_INVALID");
    internal static SessionRevocationResult Revoked { get; } =
        new(SessionRevocationStatus.Revoked, "SESSION_REVOKED");
    internal static SessionRevocationResult AlreadyRevoked { get; } =
        new(SessionRevocationStatus.AlreadyRevoked, "SESSION_ALREADY_REVOKED");
    internal static SessionRevocationResult Unavailable { get; } =
        new(SessionRevocationStatus.SecurityStateUnavailable, "SESSION_SECURITY_STATE_UNAVAILABLE");
}
