namespace EarlyInterventionCare.Api.DTOs.Authentication;

/// <summary>Browser-session metadata only. It must never expose an authentication credential.</summary>
public sealed class StaffSessionResponse
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed record AntiforgeryTokenResponse(string RequestToken);
