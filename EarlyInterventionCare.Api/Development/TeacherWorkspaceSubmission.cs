using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Development;

public sealed record WorkspaceAnswer(string? QuestionId, string? OptionValue);
public sealed record WorkspaceSubmission(Guid QuestionnaireVersionId, string? RespondentName,
    string? FilledOn, WorkspaceAnswer?[]? Answers, string? Observation);
public sealed record WorkspaceReceipt(Guid ResponseId, Guid TaskId, Guid QuestionnaireVersionId,
    DateTime SubmittedAtUtc, string GrantStatus, bool Replayed);

public sealed partial class TeacherWorkspaceService
{
    private static WorkspaceOperationException SubmissionError(string code, string message, int status = 400) => new(code, message, status);

    public async Task<WorkspaceReceipt> SubmitAsync(string? token, Guid taskId, Guid key,
        WorkspaceSubmission input, string correlationId, CancellationToken ct = default)
    {
        if (!IsToken(token, 64)) throw SubmissionError("SESSION_REQUIRED", "請重新驗證授權碼。", 401);
        if (key == Guid.Empty) throw SubmissionError("INVALID_INPUT", "提交請求識別碼無效。");
        var sessionHash = Hash(token!);
        var grantId = await db.TeacherSessions.AsNoTracking().Where(s => s.SessionHash == sessionHash).Select(s => (Guid?)s.GrantId).SingleOrDefaultAsync(ct);
        if (grantId is null) throw SubmissionError("SESSION_REQUIRED", "請重新驗證授權碼。", 401);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var grant = await LockGrant(grantId.Value, ct);
        var session = await db.TeacherSessions.SingleOrDefaultAsync(s => s.SessionHash == sessionHash, ct);
        if (grant is not { IsDevelopment: true, RespondentRole: "TEACHER" } || grant.CaseId != CoreDevelopmentSeed.CaseId ||
            session is null || session.SessionExpiresAtUtc <= Now || grant.GrantStatus == "REVOKED")
            throw SubmissionError("GRANT_UNAVAILABLE", "授權無法使用。", 401);
        var receiptOnly = grant.GrantStatus == "USED" && session.ReceiptExpiresAtUtc > Now;
        if (!receiptOnly && (grant.GrantStatus != "ACTIVE" || session.SessionStatus != "ACTIVE"))
            throw SubmissionError("GRANT_UNAVAILABLE", "授權無法使用。", 401);
        var links = await db.TeacherGrantTasks.AsNoTracking().Where(l => l.GrantId == grant.GrantId).OrderBy(l => l.TaskId).ToListAsync(ct);
        if (!links.Any(l => l.TaskId == taskId)) throw SubmissionError("TASK_NOT_FOUND", "找不到指定任務。", 404);
        var tasks = new List<QuestionnaireTask>();
        foreach (var link in links)
        {
            var rows = await db.QuestionnaireTasks.FromSqlInterpolated($"SELECT * FROM case_questionnaires WHERE task_id = {link.TaskId.ToString()} FOR UPDATE").ToListAsync(ct);
            var task = rows.SingleOrDefault();
            if (task is null || task.CaseId != grant.CaseId || link.CaseId != grant.CaseId || task.RespondentRole != "TEACHER" || link.RespondentRole != "TEACHER")
                throw SubmissionError("TASK_NOT_FOUND", "找不到指定任務。", 404);
            tasks.Add(task);
        }
        var target = tasks.Single(t => t.TaskId == taskId);
        if (input.QuestionnaireVersionId != target.QuestionnaireVersionId)
            throw SubmissionError("VERSION_MISMATCH", "問卷版本不符，請重新取得任務。", 409);
        var version = await db.QuestionnaireVersions.AsNoTracking().SingleAsync(v => v.QuestionnaireVersionId == target.QuestionnaireVersionId, ct);
        using var definition = JsonDocument.Parse(version.DefinitionSnapshot);
        var name = input.RespondentName?.Trim();
        var observation = input.Observation?.Trim() ?? "";
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 50 || name.Any(char.IsControl) || observation.Length > 1000 ||
            !DateOnly.TryParseExact(input.FilledOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filledOn) || filledOn > today)
            throw SubmissionError("INVALID_INPUT", "請填寫有效的姓名、日期及補充觀察；日期不能晚於今天。");
        var questions = definition.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        if (input.Answers is null || input.Answers.Length != questions.Length || input.Answers.Any(a => a is null || a.QuestionId is null || a.OptionValue is null) ||
            input.Answers.Select(a => a!.QuestionId).Distinct(StringComparer.Ordinal).Count() != questions.Length)
            throw SubmissionError("INVALID_ANSWERS", "請完成全部題目，且每題只能提交一個答案。");
        var answerMap = input.Answers.ToDictionary(a => a!.QuestionId!, a => a!.OptionValue!, StringComparer.Ordinal);
        foreach (var question in questions)
            if (!answerMap.TryGetValue(question.GetProperty("questionId").GetString()!, out var value) ||
                !question.GetProperty("options").EnumerateArray().Any(o => o.GetProperty("value").GetString() == value))
                throw SubmissionError("INVALID_ANSWERS", "題號或選項不符合指定問卷。");
        var answers = answerMap.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new { questionId = a.Key, optionValue = a.Value }).ToArray();
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { taskId, questionnaireVersionId = input.QuestionnaireVersionId, respondentName = name, filledOn = filledOn.ToString("yyyy-MM-dd"), answers, observation });
        var payloadHash = SHA256.HashData(canonical);
        var existing = await db.QuestionnaireResponses.AsNoTracking().SingleOrDefaultAsync(r => r.SubmittedByGrantId == grant.GrantId && r.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.TaskId != taskId || !CryptographicOperations.FixedTimeEquals(existing.PayloadHash, payloadHash))
                throw SubmissionError("IDEMPOTENCY_CONFLICT", "同一提交識別碼不能用於不同內容。", 409);
            await tx.CommitAsync(ct);
            return new(existing.ResponseId, existing.TaskId, existing.QuestionnaireVersionId, existing.SubmittedAtUtc, grant.GrantStatus, true);
        }
        if (receiptOnly) throw SubmissionError("GRANT_UNAVAILABLE", "授權已完成，不能建立新答案。", 401);
        if (target.TaskStatus is not ("PENDING" or "IN_PROGRESS") || await db.QuestionnaireResponses.AnyAsync(r => r.TaskId == taskId, ct))
            throw SubmissionError("TASK_SUBMITTED", "此任務已提交或無法填寫。", 409);
        if (tasks.Any(t => t.TaskStatus == "CANCELLED")) throw SubmissionError("GRANT_UNAVAILABLE", "授權無法使用。", 401);
        var now = Now;
        var response = new QuestionnaireResponse { ResponseId = Guid.NewGuid(), TaskId = taskId,
            QuestionnaireVersionId = target.QuestionnaireVersionId, RespondentName = name, FilledOn = filledOn,
            AnswersJson = JsonSerializer.Serialize(answers), Observation = observation.Length == 0 ? null : observation,
            SubmittedByGrantId = grant.GrantId, IdempotencyKey = key, PayloadHash = payloadHash, SubmittedAtUtc = now };
        db.QuestionnaireResponses.Add(response);
        target.TaskStatus = "SUBMITTED"; target.SubmittedAtUtc = now; target.UpdatedAtUtc = now;
        if (tasks.All(t => t.TaskStatus == "SUBMITTED"))
        {
            grant.GrantStatus = "USED"; grant.UsedAtUtc = now;
            await RevokeSessions(grant.GrantId, ct);
            // Only the submitting session may replay the exact receipt for up to 15 minutes.
            session.ReceiptExpiresAtUtc = now.AddMinutes(15) < session.SessionExpiresAtUtc ? now.AddMinutes(15) : session.SessionExpiresAtUtc;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        Record(grant.GrantId, "TeacherWorkspace.Submit", correlationId);
        return new(response.ResponseId, taskId, target.QuestionnaireVersionId, now, grant.GrantStatus, false);
    }
}
