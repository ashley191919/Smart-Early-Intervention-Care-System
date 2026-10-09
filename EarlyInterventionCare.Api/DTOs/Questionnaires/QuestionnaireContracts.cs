using System.ComponentModel.DataAnnotations;

namespace EarlyInterventionCare.Api.DTOs.Questionnaires;

public sealed class AssignQuestionnaireRequest
{
    [Required] public Guid? QuestionnaireVersionId { get; init; }
    [Required, RegularExpression("^(PARENT|TEACHER)$")]
    public string RespondentRole { get; init; } = string.Empty;
    [Required, Range(1, int.MaxValue)] public int? AssignmentRound { get; init; }
    [Required] public bool? IsRequired { get; init; }
}

public sealed record QuestionnaireVersionSummary(Guid QuestionnaireVersionId, string RespondentRole,
    string VersionNumber, string VersionStatus);
public sealed record QuestionnaireSummary(Guid QuestionnaireId, string QuestionnaireCode, string Title,
    IReadOnlyList<QuestionnaireVersionSummary> Versions);
public sealed record CaseTaskResponse(Guid TaskId, Guid CaseId, Guid QuestionnaireId,
    Guid QuestionnaireVersionId, string QuestionnaireTitle, string VersionNumber, string RespondentRole,
    uint AssignmentRound, bool IsRequired, string TaskStatus, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, DateTime? SubmittedAtUtc);
