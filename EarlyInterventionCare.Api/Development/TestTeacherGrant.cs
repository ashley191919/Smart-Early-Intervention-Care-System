namespace EarlyInterventionCare.Api.Development;

// Development fixture only: this is not evidence of parental consent.
public sealed record TestTeacherGrant(
    Guid GrantId,
    string CaseId,
    string QuestionnaireVersionId,
    string TaskId,
    string TaskVersionId = "dev-task-v1",
    string Status = "ACTIVE",
    DateTimeOffset? RevokedAtUtc = null);

public sealed record TeacherResponse(Guid ResponseId, Guid GrantId, string CaseId,
    string TaskId, string TaskVersionId, string QuestionnaireVersionId,
    string Question1, string Question2, DateTimeOffset SubmittedAtUtc);

public enum SubmissionResult { Success, InvalidGrant, InvalidAnswers, SaveFailed }
