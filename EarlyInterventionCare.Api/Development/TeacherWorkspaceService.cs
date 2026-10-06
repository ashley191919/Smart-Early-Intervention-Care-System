using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Development;

public sealed record WorkspaceQuestionnaire(string Id, string VersionId, string Title,
    string Desc, string[] Options, string Instructions, string Source, string Color,
    Dictionary<int, string> Groups, string[] Questions,
    string TaskId = "", string QuestionnaireId = "", string[]? QuestionIds = null, string[]? OptionValues = null);
public sealed record WorkspacePatient(string CaseId, string DisplayName, string Sex, int Age, string CaseCode = "");
public sealed record WorkspaceTask(Guid GrantId, string TaskId, string GrantStatus,
    WorkspacePatient Patient, WorkspaceQuestionnaire[] Questionnaires, string Notice);
public sealed record WorkspaceGrant(Guid GrantId, string AuthorizationCode,
    string TaskId, string QuestionnaireVersionId, string Notice);
public sealed class WorkspaceOperationException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

// Development-only: only the explicit synthetic seed is eligible. No parental consent is asserted.
public sealed partial class TeacherWorkspaceService(ApplicationDbContext db, IAuditLogService audit, TimeProvider clock)
{
    public const string Notice = "開發測試：虛構個案，未驗證家長同意；授權、會話、操作紀錄與提交答案使用 MySQL。授權不限時；全部指定問卷提交成功後失效。草稿僅暫存本頁，刷新後清除。";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.ASCII.GetBytes(value));
    private static bool IsToken(string? value, int size) => value is not null && value.Length == size && value.All(Uri.IsHexDigit);
    private static WorkspaceOperationException Unavailable() => new("TASK_UNAVAILABLE", "請先確認開發資料庫 migration 與虛構 seed 已完成，且指定教師任務尚未提交。", 409);

    public async Task<WorkspaceGrant> CreateAsync(string correlationId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var cases = await db.Cases.FromSqlInterpolated($"SELECT * FROM cases WHERE case_id = {CoreDevelopmentSeed.CaseId.ToString()} FOR UPDATE").ToListAsync(ct);
        if (cases.Count != 1 || cases[0].CaseCode != "CASE-DEMO-001") throw Unavailable();
        var candidateId = await db.QuestionnaireTasks.AsNoTracking()
            .Where(t => t.CaseId == CoreDevelopmentSeed.CaseId && t.QuestionnaireVersionId == CoreDevelopmentSeed.VersionId && t.RespondentRole == "TEACHER" && (t.TaskStatus == "PENDING" || t.TaskStatus == "IN_PROGRESS"))
            .OrderBy(t => t.AssignmentRound).Select(t => (Guid?)t.TaskId).FirstOrDefaultAsync(ct);
        QuestionnaireTask? newTask = null;
        if (candidateId is null)
        {
            // Repeated development acceptance creates a new synthetic round; never reopen an answer.
            var previous = await db.QuestionnaireTasks.AsNoTracking().Where(t => t.CaseId == CoreDevelopmentSeed.CaseId && t.QuestionnaireVersionId == CoreDevelopmentSeed.VersionId && t.RespondentRole == "TEACHER").OrderByDescending(t => t.AssignmentRound).FirstOrDefaultAsync(ct);
            if (previous?.TaskStatus != "SUBMITTED") throw Unavailable();
            newTask = new QuestionnaireTask { TaskId = Guid.NewGuid(), CaseId = previous.CaseId, QuestionnaireId = previous.QuestionnaireId,
                QuestionnaireVersionId = previous.QuestionnaireVersionId, RespondentRole = "TEACHER", AssignmentRound = checked(previous.AssignmentRound + 1),
                IsRequired = true, TaskStatus = "PENDING", CreatedAtUtc = Now, UpdatedAtUtc = Now };
            candidateId = newTask.TaskId;
        }
        // Match submission's grant -> task lock order; the case lock serializes creation.
        var activeIds = await (from link in db.TeacherGrantTasks join grant in db.TeacherGrants on link.GrantId equals grant.GrantId where link.TaskId == candidateId.Value && grant.GrantStatus == "ACTIVE" select grant.GrantId).ToListAsync(ct);
        foreach (var id in activeIds.Order()) await LockGrant(id, ct);
        var tasks = await db.QuestionnaireTasks.FromSqlInterpolated($"SELECT * FROM case_questionnaires WHERE task_id = {candidateId.Value.ToString()} FOR UPDATE").ToListAsync(ct);
        var task = newTask ?? tasks.SingleOrDefault();
        if (task is null || task.CaseId != CoreDevelopmentSeed.CaseId || task.RespondentRole != "TEACHER" || task.TaskStatus is not ("PENDING" or "IN_PROGRESS") || task.QuestionnaireVersionId != CoreDevelopmentSeed.VersionId) throw Unavailable();
        var version = await db.QuestionnaireVersions.SingleOrDefaultAsync(v => v.QuestionnaireVersionId == task.QuestionnaireVersionId && v.RespondentRole == "TEACHER", ct);
        if (version is null) throw Unavailable();
        using var definition = JsonDocument.Parse(version.DefinitionSnapshot);
        if (!definition.RootElement.TryGetProperty("isDevelopment", out var development) || development.ValueKind != JsonValueKind.True) throw Unavailable();
        if (newTask is not null) db.QuestionnaireTasks.Add(newTask);
        foreach (var id in activeIds.Order())
        {
            var old = await LockGrant(id, ct);
            if (old is null || !old.IsDevelopment) throw new WorkspaceOperationException("GRANT_CONFLICT", "此任務已有正式授權，不能由測試入口替換。", 409);
            if (old.GrantStatus == "ACTIVE") { old.GrantStatus = "REVOKED"; old.RevokedAtUtc = Now; await RevokeSessions(id, ct); }
        }
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var created = new TeacherGrant { GrantId = Guid.NewGuid(), CaseId = task.CaseId, RespondentRole = "TEACHER", CodeHash = Hash(raw), GrantStatus = "ACTIVE", IsDevelopment = true, CreatedAtUtc = Now };
        db.TeacherGrants.Add(created);
        db.TeacherGrantTasks.Add(new TeacherGrantTask { GrantId = created.GrantId, TaskId = task.TaskId, CaseId = task.CaseId, RespondentRole = "TEACHER" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        foreach (var id in activeIds) Record(id, "TeacherWorkspace.Replace", correlationId);
        Record(created.GrantId, "TeacherWorkspace.Create", correlationId);
        return new(created.GrantId, string.Join("-", Enumerable.Range(0, 4).Select(i => raw.Substring(i * 4, 4))), task.TaskId.ToString(), task.QuestionnaireVersionId.ToString(), Notice);
    }

    public async Task<string?> VerifyAsync(string? code, string correlationId, CancellationToken ct = default)
    {
        var normalized = code?.Trim().Replace("-", "").ToUpperInvariant();
        if (!IsToken(normalized, 16)) { RecordDenied(null, correlationId); return null; }
        var hash = Hash(normalized!);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entries = await db.TeacherGrants.FromSqlInterpolated($"SELECT * FROM teacher_grants WHERE code_hash = {hash} FOR UPDATE").ToListAsync(ct);
        var entry = entries.SingleOrDefault();
        if (!Eligible(entry) || !await HasAvailableTasks(entry!, ct))
        {
            await tx.RollbackAsync(ct);
            RecordDenied(entry is { IsDevelopment: true } ? entry.GrantId : null, correlationId);
            return null;
        }
        await RevokeSessions(entry!.GrantId, ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = Now;
        db.TeacherSessions.Add(new TeacherSession { SessionHash = Hash(token), GrantId = entry.GrantId, SessionStatus = "ACTIVE", CreatedAtUtc = now, LastSeenAtUtc = now, SessionExpiresAtUtc = now.AddHours(8) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        Record(entry.GrantId, "TeacherWorkspace.Verify", correlationId);
        return token;
    }

    public async Task<WorkspaceTask?> GetTaskAsync(string? token, CancellationToken ct = default)
    {
        if (!IsToken(token, 64)) return null;
        var hash = Hash(token!);
        var grantId = await db.TeacherSessions.AsNoTracking().Where(s => s.SessionHash == hash).Select(s => (Guid?)s.GrantId).SingleOrDefaultAsync(ct);
        if (grantId is null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var grant = await LockGrant(grantId.Value, ct);
        var session = await db.TeacherSessions.SingleOrDefaultAsync(s => s.SessionHash == hash, ct);
        if (!Eligible(grant) || session is null || session.SessionStatus != "ACTIVE" || session.SessionExpiresAtUtc <= Now) return null;
        var assigned = await (from link in db.TeacherGrantTasks join task in db.QuestionnaireTasks on link.TaskId equals task.TaskId join version in db.QuestionnaireVersions on task.QuestionnaireVersionId equals version.QuestionnaireVersionId join form in db.Questionnaires on task.QuestionnaireId equals form.QuestionnaireId where link.GrantId == grant!.GrantId select new { link.CaseId, task, version, form.QuestionnaireCode }).ToListAsync(ct);
        if (assigned.Count == 0 || assigned.Any(x => x.CaseId != grant!.CaseId || x.task.CaseId != grant.CaseId || x.task.RespondentRole != "TEACHER" || x.version.RespondentRole != "TEACHER" || x.task.TaskStatus is not ("PENDING" or "IN_PROGRESS"))) return null;
        var patient = await db.Cases.AsNoTracking().SingleAsync(c => c.CaseId == grant!.CaseId, ct);
        var forms = assigned.OrderBy(x => x.task.TaskId).Select(x => Map(x.task, x.version, x.QuestionnaireCode)).ToArray();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")));
        var age = today.Year - patient.BirthDate.Year;
        if (patient.BirthDate.AddYears(age) > today) age--;
        session.LastSeenAtUtc = Now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var displayName = patient.ChildName == "測試幼兒（虛構）" ? patient.ChildName : patient.ChildName[..1] + "○○";
        return new(grant!.GrantId, forms[0].TaskId, grant.GrantStatus, new(patient.CaseId.ToString(), displayName, patient.Sex switch { "MALE" => "男", "FEMALE" => "女", _ => "未提供" }, Math.Max(0, age), patient.CaseCode), forms, Notice);
    }

    public async Task<bool> RevokeAsync(Guid id, string correlationId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var grant = await LockGrant(id, ct);
        if (grant is null || !grant.IsDevelopment || grant.CaseId != CoreDevelopmentSeed.CaseId) return false;
        if (grant.GrantStatus == "USED") throw new WorkspaceOperationException("GRANT_USED", "授權已完成，不能再撤銷。", 409);
        if (grant.GrantStatus != "REVOKED")
        {
            grant.GrantStatus = "REVOKED"; grant.RevokedAtUtc = Now;
            await RevokeSessions(id, ct); await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); Record(id, "TeacherWorkspace.Revoke", correlationId); return true;
    }

    public async Task LogoutAsync(string? token, CancellationToken ct = default, string? correlationId = null)
    {
        if (!IsToken(token, 64)) return;
        var hash = Hash(token!);
        var id = await db.TeacherSessions.AsNoTracking().Where(s => s.SessionHash == hash).Select(s => (Guid?)s.GrantId).SingleOrDefaultAsync(ct);
        if (id is null) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockGrant(id.Value, ct);
        var session = await db.TeacherSessions.SingleOrDefaultAsync(s => s.SessionHash == hash, ct);
        var changed = session is not null && session.SessionStatus == "ACTIVE";
        if (changed) { session!.SessionStatus = "REVOKED"; session.RevokedAtUtc = Now; await db.SaveChangesAsync(ct); }
        if (session?.ReceiptExpiresAtUtc is not null) { session.ReceiptExpiresAtUtc = null; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct);
        if (changed) Record(id.Value, "TeacherWorkspace.Logout", correlationId ?? Guid.NewGuid().ToString());
    }

    private static bool Eligible(TeacherGrant? grant) => grant is { IsDevelopment: true, RespondentRole: "TEACHER", GrantStatus: "ACTIVE" } && grant.CaseId == CoreDevelopmentSeed.CaseId;
    private async Task<TeacherGrant?> LockGrant(Guid id, CancellationToken ct) => (await db.TeacherGrants.FromSqlInterpolated($"SELECT * FROM teacher_grants WHERE grant_id = {id.ToString()} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
    private async Task RevokeSessions(Guid id, CancellationToken ct)
    {
        var active = await db.TeacherSessions.Where(s => s.GrantId == id && s.SessionStatus == "ACTIVE").ToListAsync(ct);
        foreach (var session in active) { session.SessionStatus = "REVOKED"; session.RevokedAtUtc = Now; }
    }
    private async Task<bool> HasAvailableTasks(TeacherGrant grant, CancellationToken ct)
    {
        var tasks = await (from link in db.TeacherGrantTasks join task in db.QuestionnaireTasks on link.TaskId equals task.TaskId where link.GrantId == grant.GrantId select task).ToListAsync(ct);
        return tasks.Count > 0 && tasks.All(t => t.CaseId == grant.CaseId && t.RespondentRole == "TEACHER" && t.TaskStatus is "PENDING" or "IN_PROGRESS");
    }
    private static WorkspaceQuestionnaire Map(QuestionnaireTask task, QuestionnaireVersion version, string code)
    {
        using var snapshot = JsonDocument.Parse(version.DefinitionSnapshot);
        var root = snapshot.RootElement;
        var questions = root.GetProperty("questions").EnumerateArray().OrderBy(q => q.GetProperty("order").GetInt32()).ToArray();
        if (questions.Length == 0) throw Unavailable();
        var options = questions[0].GetProperty("options").EnumerateArray().ToArray();
        var labels = options.Select(o => o.GetProperty("label").GetString()!).ToArray();
        var values = options.Select(o => o.GetProperty("value").GetString()!).ToArray();
        if (questions.Any(q => !q.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()).SequenceEqual(values) || !q.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("label").GetString()).SequenceEqual(labels))) throw Unavailable();
        return new(code == "SNAP_IV" ? "snap" : task.TaskId.ToString(), version.QuestionnaireVersionId.ToString(), root.GetProperty("title").GetString()!, $"{questions.Length} 題 · 指定版本", labels, root.GetProperty("instructions").GetString()!, root.GetProperty("sourceReference").GetString()!, "", code == "SNAP_IV" ? new() { [0] = "第 1–9 題｜注意力不足", [9] = "第 10–18 題｜過動、衝動", [18] = "第 19–26 題｜反抗對立行為" } : new(), questions.Select(q => q.GetProperty("text").GetString()!).ToArray(), task.TaskId.ToString(), task.QuestionnaireId.ToString(), questions.Select(q => q.GetProperty("questionId").GetString()!).ToArray(), values);
    }
    private void Record(Guid id, string action, string correlationId)
    {
        var bearer = action is "TeacherWorkspace.Verify" or "TeacherWorkspace.Logout" or "TeacherWorkspace.Submit";
        try { audit.TryWrite(new(bearer ? "TeacherGrantBearer" : "DevelopmentTestOperator", bearer ? id.ToString() : "development-test", action, "TeacherWorkspaceGrant", id.ToString(), "Success", correlationId)); } catch { }
    }
    private void RecordDenied(Guid? id, string correlationId)
    {
        try { audit.TryWrite(new("Anonymous", "unknown", "TeacherWorkspace.VerifyDenied", "TeacherWorkspaceGrant", id?.ToString() ?? "unknown", "Denied", correlationId)); } catch { }
    }
}
