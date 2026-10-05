namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class TeacherGrant
{
    public Guid GrantId { get; set; }
    public Guid CaseId { get; set; }
    public string RespondentRole { get; set; } = null!;
    public byte[] CodeHash { get; set; } = null!;
    public string GrantStatus { get; set; } = null!;
    public Guid? IssuedByUserId { get; set; }
    public Guid? ConsentReferenceId { get; set; }
    public bool IsDevelopment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
