namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class QuestionnaireVersion
{
    public Guid QuestionnaireVersionId { get; set; }
    public Guid QuestionnaireId { get; set; }
    public string RespondentRole { get; set; } = null!;
    public string VersionNumber { get; set; } = null!;
    public string VersionStatus { get; set; } = null!;
    public string DefinitionSnapshot { get; set; } = null!;
    public string? ScoringDefinition { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}
