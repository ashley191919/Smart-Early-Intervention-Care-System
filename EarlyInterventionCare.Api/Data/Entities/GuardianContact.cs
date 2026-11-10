namespace EarlyInterventionCare.Api.Data.Entities;
public sealed class GuardianContact
{
    public Guid GuardianId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ParentUserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public uint Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
public sealed class CaseGuardian
{
    public Guid CaseId { get; set; }
    public Guid GuardianId { get; set; }
    public string Relationship { get; set; } = "";
    public bool IsPrimaryContact { get; set; }
    public string LinkStatus { get; set; } = "ACTIVE";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? BoundAtUtc { get; set; }
    public Guid? ActiveCaseId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
public sealed class ParentBindingInvitation
{
    public Guid InvitationId { get; set; }
    public Guid CaseId { get; set; }
    public Guid GuardianId { get; set; }
    public string CodeHash { get; set; } = "";
    public string TargetPhone { get; set; } = "";
    // Null on legacy phone invitations: never infer a verified email from them.
    public string? TargetEmailNormalized { get; set; }
    public Guid IssuedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public Guid? ConsumedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
