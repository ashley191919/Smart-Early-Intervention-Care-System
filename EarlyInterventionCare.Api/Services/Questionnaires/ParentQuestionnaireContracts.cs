using System.Security.Claims;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

// Internal server-produced evidence, never an HTTP input DTO.
public sealed record ParentTaskAccess(Guid UserId, Guid OrganizationId, Guid CaseId, Guid TaskId,
    Guid GuardianId, Guid ConsentRecordId);
public sealed record ParentQuestionnaireReceipt(Guid ResponseId, Guid TaskId,
    Guid QuestionnaireVersionId, DateTime SubmittedAtUtc, string TaskStatus, bool Replayed);
public sealed record ParentTaskStatus(Guid TaskId, Guid QuestionnaireVersionId,
    string TaskStatus, DateTime? SubmittedAtUtc);

public sealed record ParentQuestionnaireOptions(Guid QuestionnaireVersionId)
{
    public void Validate()
    {
        if (QuestionnaireVersionId == Guid.Empty)
            throw new ArgumentException("An approved PARENT version must be pinned before registration.");
    }
}

/// <summary>
/// 11 implements this adapter. Resolve the sole authenticated userId from principal;
/// lock/re-query ACTIVE account/organization, Case scope, Guardian link and applicable
/// Consent in THIS transaction. Reject wrong role/organization, revoked/unbound/no-consent.
/// Lock order: organization -> user -> Case -> Guardian/link -> Consent -> Task.
/// Sort multiple rows by stable ID. Revocation/binding writers must follow the same order.
/// Hold locks until transaction disposal; no separate context/HTTP service/cached approval.
/// RevalidateAsync must recheck on the same transaction before commit (including receipt replay).
/// Return TASK_NOT_FOUND/404 for unavailable scope, not distinguishing another person's Case.
/// Missing adapter remains deny-all. This contract defines no medical consent policy.
/// </summary>
public interface IParentTaskAccessAdapter
{
    Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction transaction,
        ClaimsPrincipal principal, Guid taskId, CancellationToken ct);
    Task RevalidateAsync(IParentQuestionnaireTransaction transaction, ParentTaskAccess access,
        CancellationToken ct);
}

public interface IParentQuestionnaireStore
{
    Task<IParentQuestionnaireTransaction> OpenAsync(CancellationToken ct);
}

public interface IParentQuestionnaireTransaction : IAsyncDisposable
{
    // Non-null for the EF adapter. 11 must use this exact context and its active transaction.
    // Null is permitted only for isolated contract-test implementations.
    ApplicationDbContext? DbContext { get; }
    Task<QuestionnaireTask?> LockTaskAsync(Guid taskId, CancellationToken ct);
    Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct);
    Task<QuestionnaireDraft?> FindDraftAsync(Guid taskId, CancellationToken ct);
    Task<QuestionnaireResponse?> FindResponseAsync(Guid taskId, CancellationToken ct);
    Task<QuestionnaireResponse?> FindByKeyAsync(Guid userId, Guid key, CancellationToken ct);
    void AddDraft(QuestionnaireDraft draft);
    void AddResponse(QuestionnaireResponse response);
    Task CommitAsync(CancellationToken ct);
}

public sealed class DenyParentTaskAccessAdapter : IParentTaskAccessAdapter
{
    public Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction transaction,
        ClaimsPrincipal principal, Guid taskId, CancellationToken ct) => throw Denied();
    public Task RevalidateAsync(IParentQuestionnaireTransaction transaction, ParentTaskAccess access,
        CancellationToken ct) => throw Denied();
    private static QuestionnaireWorkflowException Denied() =>
        new("PARENT_ACCESS_NOT_INTEGRATED", "Parent authorization is not integrated.", 503);
}
