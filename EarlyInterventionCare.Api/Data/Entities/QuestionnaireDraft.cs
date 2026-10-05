namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class QuestionnaireDraft
{
    public Guid TaskId { get; set; }
    public Guid QuestionnaireVersionId { get; set; }
    public string? RespondentName { get; set; }
    public DateOnly? FilledOn { get; set; }
    public string AnswersJson { get; set; } = null!;
    public string? Observation { get; set; }
    public ulong Revision { get; set; }
    public Guid? LastSavedByGrantId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
