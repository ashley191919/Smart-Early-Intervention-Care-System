namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class QuestionnaireTask
{
    public Guid TaskId { get; set; }
    public Guid CaseId { get; set; }
    public Guid QuestionnaireId { get; set; }
    public Guid QuestionnaireVersionId { get; set; }
    public string RespondentRole { get; set; } = null!;
    public uint AssignmentRound { get; set; }
    public bool IsRequired { get; set; } = true;
    public string TaskStatus { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
