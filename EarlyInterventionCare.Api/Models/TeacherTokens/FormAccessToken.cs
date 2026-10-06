using EarlyInterventionCare.Api.Models.Authentication;

namespace EarlyInterventionCare.Api.Models.TeacherTokens;

[System.ComponentModel.DataAnnotations.Schema.NotMapped]
[Obsolete("Legacy model only. Use TeacherGrant, TeacherGrantTask and TeacherSession.")]
public class FormAccessToken
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid CaseId { get; set; }
    public Guid QuestionnaireId { get; set; }
    public Guid IssuedByUserId { get; set; }
    public User IssuedByUser { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
}
