using System.ComponentModel.DataAnnotations;
namespace EarlyInterventionCare.Api.DTOs.Cases;
public class CaseInput
{
    [Required, StringLength(100)] public string ChildName { get; init; } = "";
    [Required, StringLength(10), RegularExpression("^[A-Z][12][0-9]{8}$")] public string NationalId { get; init; } = "";
    [Required, StringLength(300)] public string Address { get; init; } = "";
    public DateOnly BirthDate { get; init; }
    [Required, RegularExpression("^(MALE|FEMALE|UNKNOWN)$")] public string Sex { get; init; } = "UNKNOWN";
}
public sealed class CaseUpdateInput : CaseInput { public uint Revision { get; init; } }
public sealed record CaseDto(Guid CaseId, string CaseCode, string ChildName, DateOnly BirthDate, string Sex, string NationalId, string Address, string CaseStatus, uint Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public class GuardianInput
{
    [Required, StringLength(100)] public string DisplayName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required] public string Phone { get; init; } = "";
    [Required, RegularExpression("^(FATHER|MOTHER|GRANDPARENT|RELATIVE|GUARDIAN|CAREGIVER|OTHER)$")] public string Relationship { get; init; } = "";
    public bool IsPrimaryContact { get; init; } = true;
}
public sealed class GuardianUpdateInput : GuardianInput { public uint Revision { get; init; } }
public sealed record GuardianDto(Guid GuardianId, string DisplayName, string Phone, string Email, string Relationship, bool IsPrimaryContact, string LinkStatus, uint Revision);
public sealed class BindInput { [Required, StringLength(80)] public string InvitationCode { get; init; } = ""; }
public sealed class ProfileInput { [Required, StringLength(100)] public string DisplayName { get; init; } = ""; public uint Revision { get; init; } }
public sealed record ParentProfileDto(Guid GuardianId, string DisplayName, string Phone, string Email, uint Revision);
public sealed record TeacherCompletionDto(Guid TaskId, string QuestionnaireTitle, uint AssignmentRound, bool IsCompleted);

public sealed record ParentCaseRelationshipDto(Guid GuardianId, string Relationship, bool IsPrimaryContact);
public sealed record ParentCaseDto(Guid CaseId, string CaseCode, string ChildName, DateOnly BirthDate, string Sex, string NationalId, string Address, string CaseStatus, uint Revision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<ParentCaseRelationshipDto> Relationships);

public sealed class GuardianLinkInput
{
    [Required, RegularExpression("^(FATHER|MOTHER|GRANDPARENT|RELATIVE|GUARDIAN|CAREGIVER|OTHER)$")] public string Relationship { get; init; } = "";
    public bool IsPrimaryContact { get; init; } = true;
}
