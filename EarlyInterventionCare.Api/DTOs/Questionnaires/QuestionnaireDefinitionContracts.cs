namespace EarlyInterventionCare.Api.DTOs.Questionnaires;

public sealed record QuestionnaireDefinitionResponse(Guid TaskId, Guid QuestionnaireId,
    Guid QuestionnaireVersionId, string Title, string VersionNumber, string? Instructions,
    IReadOnlyList<QuestionnaireDefinitionQuestion> Questions);

public sealed record QuestionnaireDefinitionQuestion(string QuestionId, int Order, string Text,
    string Type, bool? SnapshotRequired, bool RequiredOnSubmit,
    IReadOnlyList<QuestionnaireDefinitionOption> Options);

public sealed record QuestionnaireDefinitionOption(string Value, string Label);
