using EarlyInterventionCare.Api.Models.Authorization;

namespace EarlyInterventionCare.Api.Models.Authentication;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string? Phone { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
