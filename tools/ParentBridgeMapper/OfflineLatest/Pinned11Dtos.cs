// TEST-ONLY SOURCE PIN. Never include in the formal API.
// Commit: 26e8b7fb09fdaa24292c17a4f58d7ea4ce536491
// Source: EarlyInterventionCare.Api/ParentModule/ParentQuestionnaireContracts.cs; Git blob: ed5df3eebd68f08c892a6c93ceeff2e470fed4ae
// END PIN METADATA
using System.Security.Claims;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
namespace EarlyInterventionCare.Api.ParentModule;

public static class ParentGatewayContract { public const string Version = "parent-v53-1"; }
// Server-created request context. Never bind this type from an HTTP body.
public sealed record ParentRequestContext(ClaimsPrincipal Principal, Guid ActorUserId, Guid OrganizationId,
    Guid CaseId, Guid ConsentId, Guid DocumentVersionId, Guid GuardianId);
public sealed record ParentDraftRequest(Guid QuestionnaireVersionId, string ExpectedRevision, string? RespondentName,
    string? FilledOn, string? RespondentRelationship, QuestionnaireAnswer[] Answers, string? Observation);
public sealed record ParentDraftResponse(Guid TaskId, Guid QuestionnaireVersionId, string Revision,
    string? RespondentName, string? FilledOn, string? RespondentRelationship, IReadOnlyList<QuestionnaireAnswer> Answers,
    string? Observation, DateTime? UpdatedAtUtc, string TaskStatus);
public sealed record ParentSubmitRequest(Guid QuestionnaireVersionId, string ExpectedRevision, string RespondentName,
    string FilledOn, string RespondentRelationship, QuestionnaireAnswer[] Answers, string? Observation);
public sealed record ParentSubmissionReceipt(Guid ResponseId, Guid TaskId, Guid QuestionnaireVersionId,
    DateTime SubmittedAtUtc, string TaskStatus, bool Replayed);

// 16 implements storage/author isolation/Revision/Idempotency/Response + Task transaction.
// Every operation MUST revalidate IParentQuestionnaireAccess inside the same context
// and consistency protocol as Guardian/Consent revocation. Context is not a bearer permission.
public interface IParentQuestionnaireGateway
{
    Task<ParentDraftResponse> GetDraftAsync(ParentRequestContext context, Guid taskId, CancellationToken ct = default);
    Task<ParentDraftResponse> SaveDraftAsync(ParentRequestContext context, Guid taskId, ParentDraftRequest input, CancellationToken ct = default);
    Task<ParentSubmissionReceipt> SubmitAsync(ParentRequestContext context, Guid taskId, Guid idempotencyKey, ParentSubmitRequest input, CancellationToken ct = default);
    Task<ParentSubmissionReceipt> GetReceiptAsync(ParentRequestContext context, Guid taskId, CancellationToken ct = default);
}
