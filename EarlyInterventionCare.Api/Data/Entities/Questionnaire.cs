namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class Questionnaire
{
    public Guid QuestionnaireId { get; set; }
    public string QuestionnaireCode { get; set; } = null!;
    public string Title { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
