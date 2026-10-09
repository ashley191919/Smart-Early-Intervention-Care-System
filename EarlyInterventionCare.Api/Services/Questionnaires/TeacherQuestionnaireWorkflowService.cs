using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

// Development fixture only. TODO(11): formal Case/Guardian scope and response-view policy.
// TODO(7): Case Status eligibility and completion/dashboard rules. Never mutate Case Status here.
public sealed class TeacherQuestionnaireWorkflowService(ITeacherQuestionnaireStore store,
    QuestionnaireAnswerValidator validator, TimeProvider clock, IAuditLogService audit)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")));
    private static QuestionnaireWorkflowException Error(string code, string message, int status) => new(code, message, status);

    public async Task<QuestionnaireDraftResponse> GetDraftAsync(string? token, Guid taskId, CancellationToken ct = default)
    {
        await using var tx = await Open(token, ct);
        var task = RequireScope(tx, taskId, allowReceipt: false);
        await RequireEditable(tx, task, ct);
        var draft = await tx.FindDraftAsync(taskId, ct);
        return DraftResponse(task, draft);
    }

    public async Task<QuestionnaireDraftResponse> SaveDraftAsync(string? token, Guid taskId,
        SaveQuestionnaireDraftRequest input, string correlationId, CancellationToken ct = default)
    {
        if (input.ExpectedRevision is null) throw Error("INVALID_INPUT", "必須提供 expectedRevision。", 400);
        await using var tx = await Open(token, ct);
        var task = RequireScope(tx, taskId, allowReceipt: false);
        await RequireEditable(tx, task, ct);
        var version = await RequireVersion(tx, task, input.QuestionnaireVersionId, ct);
        var draft = await tx.FindDraftAsync(taskId, ct);
        if ((draft?.Revision ?? 0) != input.ExpectedRevision || input.ExpectedRevision == ulong.MaxValue)
            throw Error("DRAFT_CONFLICT", "草稿版本已變更，請重新讀取。", 409);
        var content = validator.Validate(version.DefinitionSnapshot,
            new(input.QuestionnaireVersionId, input.RespondentName, input.FilledOn, input.Answers, input.Observation), Today, complete: false);
        if (draft is null)
        {
            draft = new QuestionnaireDraft { TaskId = taskId, QuestionnaireVersionId = task.QuestionnaireVersionId };
            tx.AddDraft(draft);
        }
        draft.RespondentName = content.RespondentName; draft.FilledOn = content.FilledOn;
        draft.AnswersJson = content.AnswersJson; draft.Observation = content.Observation;
        draft.LastSavedByGrantId = tx.Grant!.GrantId; draft.Revision++;
        draft.UpdatedAtUtc = Now;
        if (task.TaskStatus == "PENDING") task.TaskStatus = "IN_PROGRESS";
        task.UpdatedAtUtc = draft.UpdatedAtUtc;
        await tx.CommitAsync(ct);
        Record(tx.Grant!.GrantId, "TeacherWorkspace.SaveDraft", correlationId);
        return DraftResponse(task, draft);
    }

    // Existing submission algorithm, with storage extracted for offline tests; no new answer rules.
    public async Task<QuestionnaireReceipt> SubmitAsync(string? token, Guid taskId, Guid key,
        QuestionnaireSubmission input, string correlationId, CancellationToken ct = default)
    {
        if (key == Guid.Empty) throw Error("INVALID_INPUT", "提交請求識別碼無效。", 400);
        await using var tx = await Open(token, ct);
        var target = RequireScope(tx, taskId, allowReceipt: true);
        var version = await RequireVersion(tx, target, input.QuestionnaireVersionId, ct);
        var content = validator.Validate(version.DefinitionSnapshot, input, Today, complete: true);
        var payloadHash = content.SubmissionHash(taskId, input.QuestionnaireVersionId);
        var existing = await tx.FindByKeyAsync(tx.Grant!.GrantId, key, ct);
        if (existing is not null)
        {
            if (existing.TaskId != taskId || !CryptographicOperations.FixedTimeEquals(existing.PayloadHash, payloadHash))
                throw Error("IDEMPOTENCY_CONFLICT", "同一提交識別碼不能用於不同內容。", 409);
            await tx.CommitAsync(ct);
            return Receipt(existing, tx.Grant.GrantStatus, true);
        }
        if (tx.Grant.GrantStatus == "USED") throw Error("GRANT_UNAVAILABLE", "授權已完成，不能建立新答案。", 401);
        await RequireEditable(tx, target, ct);
        if (tx.Tasks.Any(t => t.Task!.TaskStatus == "CANCELLED")) throw Error("GRANT_UNAVAILABLE", "授權無法使用。", 401);
        var now = Now;
        var response = new QuestionnaireResponse { ResponseId = Guid.NewGuid(), TaskId = taskId,
            QuestionnaireVersionId = target.QuestionnaireVersionId, RespondentName = content.RespondentName!,
            FilledOn = content.FilledOn!.Value, AnswersJson = content.AnswersJson, Observation = content.Observation,
            SubmittedByGrantId = tx.Grant.GrantId, IdempotencyKey = key, PayloadHash = payloadHash, SubmittedAtUtc = now };
        tx.AddResponse(response);
        target.TaskStatus = "SUBMITTED"; target.SubmittedAtUtc = now; target.UpdatedAtUtc = now;
        if (tx.Tasks.All(t => t.Task!.TaskStatus == "SUBMITTED"))
        {
            tx.Grant.GrantStatus = "USED"; tx.Grant.UsedAtUtc = now;
            await tx.RevokeSessionsAsync(now, ct);
            var session = tx.Session!;
            session.ReceiptExpiresAtUtc = now.AddMinutes(15) < session.SessionExpiresAtUtc ? now.AddMinutes(15) : session.SessionExpiresAtUtc;
        }
        await tx.CommitAsync(ct);
        Record(tx.Grant.GrantId, "TeacherWorkspace.Submit", correlationId);
        return Receipt(response, tx.Grant.GrantStatus, false);
    }

    // Receipt only, scoped to the same grant/session; no raw response or answers endpoint.
    public async Task<QuestionnaireReceipt> GetReceiptAsync(string? token, Guid taskId, CancellationToken ct = default)
    {
        await using var tx = await Open(token, ct);
        var task = RequireScope(tx, taskId, allowReceipt: true);
        if (task.TaskStatus != "SUBMITTED") throw Error("RESPONSE_NOT_FOUND", "此任務尚未提交。", 404);
        var receipt = await tx.FindReceiptAsync(tx.Grant!.GrantId, taskId, ct)
            ?? throw Error("RESPONSE_NOT_FOUND", "找不到此授權的提交收據。", 404);
        return receipt with { SubmittedAtUtc = DateTime.SpecifyKind(receipt.SubmittedAtUtc, DateTimeKind.Utc) };
    }

    private async Task<ITeacherQuestionnaireTransaction> Open(string? token, CancellationToken ct)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) throw Error("SESSION_REQUIRED", "請重新驗證授權碼。", 401);
        return await store.OpenAsync(SHA256.HashData(Encoding.ASCII.GetBytes(token)), ct)
            ?? throw Error("SESSION_REQUIRED", "請重新驗證授權碼。", 401);
    }

    private QuestionnaireTask RequireScope(ITeacherQuestionnaireTransaction tx, Guid taskId, bool allowReceipt)
    {
        var grant = tx.Grant; var session = tx.Session;
        if (grant is not { IsDevelopment: true, RespondentRole: "TEACHER" } || grant.CaseId != CoreDevelopmentSeed.CaseId ||
            session is null || session.GrantId != grant.GrantId || session.SessionExpiresAtUtc <= Now || grant.GrantStatus == "REVOKED")
            throw Error("GRANT_UNAVAILABLE", "授權無法使用。", 401);
        var receiptOnly = allowReceipt && grant.GrantStatus == "USED" && session.ReceiptExpiresAtUtc > Now;
        if (!receiptOnly && (grant.GrantStatus != "ACTIVE" || session.SessionStatus != "ACTIVE"))
            throw Error("GRANT_UNAVAILABLE", "授權無法使用。", 401);
        if (tx.Tasks.Count == 0 || tx.Tasks.Any(t => t.Task is null || t.Link.GrantId != grant.GrantId || t.Link.TaskId != t.Task.TaskId ||
            t.Link.CaseId != grant.CaseId || t.Task.CaseId != grant.CaseId || t.Link.RespondentRole != "TEACHER" || t.Task.RespondentRole != "TEACHER"))
            throw Error("TASK_NOT_FOUND", "找不到指定任務。", 404);
        return tx.Tasks.SingleOrDefault(t => t.Task!.TaskId == taskId)?.Task
            ?? throw Error("TASK_NOT_FOUND", "找不到指定任務。", 404);
    }

    private static async Task RequireEditable(ITeacherQuestionnaireTransaction tx, QuestionnaireTask task, CancellationToken ct)
    {
        if (task.TaskStatus is not ("PENDING" or "IN_PROGRESS") || await tx.HasResponseAsync(task.TaskId, ct))
            throw Error("TASK_SUBMITTED", "此任務已提交或無法填寫。", 409);
    }

    private static async Task<QuestionnaireVersion> RequireVersion(ITeacherQuestionnaireTransaction tx, QuestionnaireTask task, Guid versionId, CancellationToken ct)
    {
        if (versionId != task.QuestionnaireVersionId) throw Error("VERSION_MISMATCH", "問卷版本不符，請重新取得任務。", 409);
        var version = await tx.FindVersionAsync(task.QuestionnaireVersionId, ct);
        if (version is null || version.RespondentRole != task.RespondentRole || version.QuestionnaireId != task.QuestionnaireId)
            throw Error("VERSION_MISMATCH", "問卷版本不符，請重新取得任務。", 409);
        return version;
    }

    private static QuestionnaireDraftResponse DraftResponse(QuestionnaireTask task, QuestionnaireDraft? draft) => new(
        task.TaskId, task.QuestionnaireVersionId, draft?.Revision ?? 0, draft?.RespondentName,
        draft?.FilledOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        draft is null ? Array.Empty<QuestionnaireAnswer>() : JsonSerializer.Deserialize<QuestionnaireAnswer[]>(draft.AnswersJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Draft answers are invalid."),
        draft?.Observation, draft is null ? null : DateTime.SpecifyKind(draft.UpdatedAtUtc, DateTimeKind.Utc), task.TaskStatus);
    private static QuestionnaireReceipt Receipt(QuestionnaireResponse response, string status, bool replayed) => new(
        response.ResponseId, response.TaskId, response.QuestionnaireVersionId,
        DateTime.SpecifyKind(response.SubmittedAtUtc, DateTimeKind.Utc), status, replayed);

    private void Record(Guid grantId, string action, string correlationId)
    {
        try { audit.TryWrite(new("TeacherGrantBearer", grantId.ToString(), action,
            "TeacherWorkspaceGrant", grantId.ToString(), "Success", correlationId)); }
        catch { /* Existing best-effort audit contract; never undo a committed submission. */ }
    }
}
