namespace EarlyInterventionCare.Api.DTOs.Authentication;

public class LoginResponse
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;
}
