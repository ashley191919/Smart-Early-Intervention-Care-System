namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class QuestionnaireResponse
{
    public Guid ResponseId { get; set; }
    public Guid TaskId { get; set; }
    public Guid QuestionnaireVersionId { get; set; }
    public string RespondentName { get; set; } = null!;
    public DateOnly FilledOn { get; set; }
    public string AnswersJson { get; set; } = null!;
    public string? Observation { get; set; }
    public Guid? SubmittedByGrantId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public byte[] PayloadHash { get; set; } = null!;
    public DateTime SubmittedAtUtc { get; set; }
}
