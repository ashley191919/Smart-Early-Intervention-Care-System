using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public sealed class EfTeacherQuestionnaireStore(ApplicationDbContext db) : ITeacherQuestionnaireStore
{
    public async Task<ITeacherQuestionnaireTransaction?> OpenAsync(byte[] sessionHash, CancellationToken ct)
    {
        var grantId = await db.TeacherSessions.AsNoTracking().Where(s => s.SessionHash == sessionHash)
            .Select(s => (Guid?)s.GrantId).SingleOrDefaultAsync(ct);
        if (grantId is null) return null;
        var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var grant = (await db.TeacherGrants.FromSqlInterpolated(
                $"SELECT * FROM teacher_grants WHERE grant_id = {grantId.Value.ToString()} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
            var session = await db.TeacherSessions.SingleOrDefaultAsync(s => s.SessionHash == sessionHash, ct);
            var links = await db.TeacherGrantTasks.AsNoTracking().Where(l => l.GrantId == grantId).OrderBy(l => l.TaskId).ToListAsync(ct);
            var tasks = new List<TeacherTaskEntry>();
            foreach (var link in links)
            {
                var task = (await db.QuestionnaireTasks.FromSqlInterpolated(
                    $"SELECT * FROM case_questionnaires WHERE task_id = {link.TaskId.ToString()} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
                tasks.Add(new(link, task));
            }
            return new Transaction(db, tx, grant, session, tasks);
        }
        catch { await tx.DisposeAsync(); db.ChangeTracker.Clear(); throw; }
    }

    private sealed class Transaction(ApplicationDbContext db, IDbContextTransaction tx, TeacherGrant? grant,
        TeacherSession? session, IReadOnlyList<TeacherTaskEntry> tasks) : ITeacherQuestionnaireTransaction
    {
        private bool committed;
        public TeacherGrant? Grant => grant;
        public TeacherSession? Session => session;
        public IReadOnlyList<TeacherTaskEntry> Tasks => tasks;
        public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) =>
            db.QuestionnaireVersions.AsNoTracking().SingleOrDefaultAsync(v => v.QuestionnaireVersionId == id, ct);
        public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct) => db.QuestionnaireDrafts.SingleOrDefaultAsync(d => d.TaskId == id, ct);
        public Task<bool> HasResponseAsync(Guid id, CancellationToken ct) => db.QuestionnaireResponses.AsNoTracking().AnyAsync(r => r.TaskId == id, ct);
        public Task<QuestionnaireResponse?> FindByKeyAsync(Guid id, Guid key, CancellationToken ct) =>
            db.QuestionnaireResponses.AsNoTracking().SingleOrDefaultAsync(r => r.SubmittedByGrantId == id && r.IdempotencyKey == key, ct);
        public Task<QuestionnaireReceipt?> FindReceiptAsync(Guid id, Guid taskId, CancellationToken ct) =>
            db.QuestionnaireResponses.AsNoTracking().Where(r => r.SubmittedByGrantId == id && r.TaskId == taskId)
                .Select(r => new QuestionnaireReceipt(r.ResponseId, r.TaskId, r.QuestionnaireVersionId,
                    r.SubmittedAtUtc, grant!.GrantStatus, false)).SingleOrDefaultAsync(ct);
        public void AddDraft(QuestionnaireDraft draft) => db.QuestionnaireDrafts.Add(draft);
        public void AddResponse(QuestionnaireResponse response) => db.QuestionnaireResponses.Add(response);
        public async Task RevokeSessionsAsync(DateTime now, CancellationToken ct)
        {
            var sessions = await db.TeacherSessions.Where(s => s.GrantId == grant!.GrantId && s.SessionStatus == "ACTIVE").ToListAsync(ct);
            foreach (var entry in sessions) { entry.SessionStatus = "REVOKED"; entry.RevokedAtUtc = now; }
        }
        public async Task CommitAsync(CancellationToken ct)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            { throw new QuestionnaireWorkflowException("DRAFT_CONFLICT", "草稿已由其他請求更新，請重新讀取。", 409); }
            await tx.CommitAsync(ct);
            committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            try { await tx.DisposeAsync(); }
            finally { if (!committed) db.ChangeTracker.Clear(); }
        }
    }
}
