namespace EarlyInterventionCare.Api.Development;

// Development fixture only: this is not evidence of parental consent.
public sealed record TestTeacherGrant(
    Guid GrantId,
    string CaseId,
    string QuestionnaireVersionId,
    string TaskId,
    DateTimeOffset ExpiresAtUtc);
