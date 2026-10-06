namespace EarlyInterventionCare.Api.DTOs.Authentication;

public class CurrentUserResponse
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string[] Permissions { get; init; } = Array.Empty<string>();
}
