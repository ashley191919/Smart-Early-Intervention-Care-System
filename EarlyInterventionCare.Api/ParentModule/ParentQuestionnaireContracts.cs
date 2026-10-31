using System.Security.Claims;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;

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
public sealed class UnavailableParentQuestionnaireGateway : IParentQuestionnaireGateway
{
    static CaseAccessException Unavailable() => new("PARENT_GATEWAY_UNAVAILABLE", 503, "問卷填答服務暫時無法使用，請稍後再試。");
    public Task<ParentDraftResponse> GetDraftAsync(ParentRequestContext context, Guid taskId, CancellationToken ct = default) => throw Unavailable();
    public Task<ParentDraftResponse> SaveDraftAsync(ParentRequestContext context, Guid taskId, ParentDraftRequest input, CancellationToken ct = default) => throw Unavailable();
    public Task<ParentSubmissionReceipt> SubmitAsync(ParentRequestContext context, Guid taskId, Guid idempotencyKey, ParentSubmitRequest input, CancellationToken ct = default) => throw Unavailable();
    public Task<ParentSubmissionReceipt> GetReceiptAsync(ParentRequestContext context, Guid taskId, CancellationToken ct = default) => throw Unavailable();
}
public interface IParentQuestionnaireAccess
{
    Task<ParentRequestContext> RequireAsync(ClaimsPrincipal principal, Guid taskId, bool writing,
        ApplicationDbContext transactionContext, CancellationToken ct = default);
    Task<ParentRequestContext> RequireInTransactionAsync(ClaimsPrincipal principal, Guid taskId, bool writing,
        ApplicationDbContext transactionContext, CancellationToken ct = default);
}
public sealed class ParentQuestionnaireAccess(ApplicationDbContext db, IParentActorProvider actors,
    CaseAccessService access, ParentConsentService consents) : IParentQuestionnaireAccess
{
    public Task<ParentRequestContext> RequireInTransactionAsync(ClaimsPrincipal principal, Guid taskId, bool writing,
        ApplicationDbContext transactionContext, CancellationToken ct = default)
    {
        if (!ReferenceEquals(db, transactionContext) || db.Database.CurrentTransaction == null)
            throw new CaseAccessException("PARENT_TRANSACTION_REQUIRED", 503, "問卷一致性檢查尚未就緒。");
        return RequireAsync(principal, taskId, writing, transactionContext, ct);
    }
    public async Task<ParentRequestContext> RequireAsync(ClaimsPrincipal principal, Guid taskId, bool writing,
        ApplicationDbContext transactionContext, CancellationToken ct = default)
    {
        if (!ReferenceEquals(db, transactionContext)) throw new InvalidOperationException("Parent access requires the shared scoped DbContext.");
        var actor = await actors.ResolveAsync(principal, ct);
        var task = await db.QuestionnaireTasks.AsNoTracking().SingleOrDefaultAsync(t => t.TaskId == taskId &&
            t.RespondentRole == "PARENT" && t.TaskStatus != "CANCELLED" && access.ReadableCases(actor).Any(c => c.CaseId == t.CaseId), ct);
        if (task == null) throw new CaseAccessException("TASK_NOT_FOUND", 404, "找不到問卷，或目前已無法存取。");
        // SUBMITTED is allowed through to the engine so an identical idempotent retry
        // can return its original receipt. The engine must reject any new write.
        if (task.TaskStatus is not ("PENDING" or "IN_PROGRESS" or "SUBMITTED"))
            throw new CaseAccessException("TASK_NOT_EDITABLE", 409, "問卷目前無法填寫。");
        // Server-side precheck evidence only. Never select an arbitrary contact or
        // infer GuardianId from HTTP input. The reviewed transaction adapter must
        // re-read/lock and compare this result; this query itself is not a lock.
        var guardians = await (from link in db.CaseGuardians.AsNoTracking()
            join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.GuardianId
            where link.CaseId == task.CaseId && link.LinkStatus == "ACTIVE" && link.BoundAtUtc != null &&
                link.RevokedAtUtc == null && guardian.OrganizationId == actor.OrganizationId && guardian.ParentUserId == actor.UserId
            select guardian.GuardianId).Distinct().Take(2).ToArrayAsync(ct);
        if (guardians.Length != 1)
            throw new CaseAccessException("TASK_NOT_FOUND", 404, "找不到問卷，或目前已無法存取。");
        var consent = await consents.RequireAsync(actor, task.CaseId, ct);
        return new(principal, actor.UserId, actor.OrganizationId, task.CaseId, consent.ConsentId, consent.DocumentVersionId, guardians[0]);
    }
}
