using EarlyInterventionCare.Api.Models.Authentication;

namespace EarlyInterventionCare.Api.Models.TeacherTokens;

public class FormAccessToken
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public int CaseId { get; set; }
    public int QuestionnaireId { get; set; }
    public int IssuedByUserId { get; set; }
    public User IssuedByUser { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
}
