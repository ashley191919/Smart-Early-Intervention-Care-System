namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class TeacherSession
{
    public byte[] SessionHash { get; set; } = null!;
    public Guid GrantId { get; set; }
    public string SessionStatus { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime SessionExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
