using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public sealed class EfParentQuestionnaireStore(ApplicationDbContext db) : IParentQuestionnaireStore
{
    public async Task<IParentQuestionnaireTransaction> OpenAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Parent gateway requires a clean, exclusively owned scoped DbContext.");
        db.ChangeTracker.Clear();
        return new Transaction(db, await db.Database.BeginTransactionAsync(ct));
    }

    private sealed class Transaction(ApplicationDbContext db, IDbContextTransaction transaction)
        : IParentQuestionnaireTransaction
    {
        public ApplicationDbContext DbContext => db;
        public async Task<QuestionnaireTask?> LockTaskAsync(Guid taskId, CancellationToken ct) =>
            (await db.QuestionnaireTasks.FromSqlInterpolated(
                $"SELECT * FROM case_questionnaires WHERE task_id = {taskId.ToString()} FOR UPDATE")
                .ToListAsync(ct)).SingleOrDefault();
        public Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct) =>
            db.QuestionnaireVersions.AsNoTracking().SingleOrDefaultAsync(v => v.QuestionnaireVersionId == versionId, ct);
        public Task<QuestionnaireDraft?> FindDraftAsync(Guid taskId, CancellationToken ct) =>
            db.QuestionnaireDrafts.SingleOrDefaultAsync(d => d.TaskId == taskId, ct);
        public Task<QuestionnaireResponse?> FindResponseAsync(Guid taskId, CancellationToken ct) =>
            db.QuestionnaireResponses.AsNoTracking().SingleOrDefaultAsync(r => r.TaskId == taskId, ct);
        public Task<QuestionnaireResponse?> FindByKeyAsync(Guid userId, Guid key, CancellationToken ct) =>
            db.QuestionnaireResponses.AsNoTracking().SingleOrDefaultAsync(r => r.ParentAuthorUserId == userId && r.IdempotencyKey == key, ct);
        public void AddDraft(QuestionnaireDraft draft) => db.QuestionnaireDrafts.Add(draft);
        public void AddResponse(QuestionnaireResponse response) => db.QuestionnaireResponses.Add(response);
        public async Task CommitAsync(CancellationToken ct)
        {
            if (db.Database.CurrentTransaction?.TransactionId != transaction.TransactionId)
                throw new InvalidOperationException("Parent authorization must not replace or complete the gateway transaction.");
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            { throw new QuestionnaireWorkflowException("DRAFT_CONFLICT", "Draft changed; reload before retrying.", 409); }
            catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: 1062 })
            { throw new QuestionnaireWorkflowException("WRITE_CONFLICT", "Retry the original request after reloading.", 409); }
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { db.ChangeTracker.Clear(); }
        }
    }
}
