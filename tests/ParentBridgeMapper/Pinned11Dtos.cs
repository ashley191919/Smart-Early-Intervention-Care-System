// TEST-ONLY verbatim DTO excerpt; never include in the API or production distribution.
// Source: e2affb548bb427cd02d001d5bfd5761d7ecb4648:EarlyInterventionCare.Api/ParentModule/ParentQuestionnaireContracts.cs
using System.Security.Claims;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
namespace EarlyInterventionCare.Api.ParentModule;

public sealed record ParentRequestContext(ClaimsPrincipal Principal, Guid ActorUserId, Guid OrganizationId,
    Guid CaseId, Guid ConsentId, Guid DocumentVersionId);
public sealed record ParentDraftRequest(Guid QuestionnaireVersionId, string ExpectedRevision, string? RespondentName,
    string? FilledOn, string RespondentRelationship, QuestionnaireAnswer[] Answers, string? Observation);
public sealed record ParentDraftResponse(Guid TaskId, Guid QuestionnaireVersionId, string Revision,
    string? RespondentName, string? FilledOn, string RespondentRelationship, IReadOnlyList<QuestionnaireAnswer> Answers,
    string? Observation, DateTime? UpdatedAtUtc, string TaskStatus);
public sealed record ParentSubmitRequest(Guid QuestionnaireVersionId, string ExpectedRevision, string RespondentName,
    string FilledOn, string RespondentRelationship, QuestionnaireAnswer[] Answers, string? Observation);
public sealed record ParentSubmissionReceipt(Guid ResponseId, Guid TaskId, Guid QuestionnaireVersionId,
    DateTime SubmittedAtUtc, string TaskStatus, bool Replayed);
