using System.ComponentModel.DataAnnotations;

namespace EarlyInterventionCare.Api.DTOs.Questionnaires;

public sealed record QuestionnaireAnswer(string? QuestionId, string? OptionValue);
public sealed record QuestionnaireSubmission(Guid QuestionnaireVersionId, string? RespondentName,
    string? FilledOn, QuestionnaireAnswer?[]? Answers, string? Observation);
public sealed record SaveQuestionnaireDraftRequest(
    Guid QuestionnaireVersionId, [property: Required] ulong? ExpectedRevision,
    string? RespondentName, string? FilledOn, QuestionnaireAnswer?[]? Answers, string? Observation);
public sealed record QuestionnaireDraftResponse(Guid TaskId, Guid QuestionnaireVersionId, ulong Revision,
    string? RespondentName, string? FilledOn, IReadOnlyList<QuestionnaireAnswer> Answers,
    string? Observation, DateTime? UpdatedAtUtc, string TaskStatus);

// Deliberately excludes answers, identity fields, hashes, keys and observation.
public sealed record QuestionnaireReceipt(Guid ResponseId, Guid TaskId, Guid QuestionnaireVersionId,
    DateTime SubmittedAtUtc, string GrantStatus, bool Replayed);
