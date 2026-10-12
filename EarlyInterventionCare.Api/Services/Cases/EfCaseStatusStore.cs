using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EarlyInterventionCare.Api.Services.Cases;

public sealed class EfCaseStatusStore(ApplicationDbContext db) : ICaseStatusStore
{
    public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.Status == "ACTIVE", ct);

    public async Task<ICaseStatusTransaction?> OpenAsync(Guid caseId, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serializes status changes for this case until the transaction commits or rolls back.
            var cases = await db.Cases.FromSqlInterpolated(
                $"SELECT * FROM cases WHERE case_id = {caseId.ToString()} FOR UPDATE").ToListAsync(ct);
            var caseRecord = cases.SingleOrDefault();
            if (caseRecord is null)
            {
                await transaction.DisposeAsync();
                db.ChangeTracker.Clear();
                return null;
            }

            return new Transaction(db, transaction, caseRecord);
        }
        catch
        {
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private sealed class Transaction(ApplicationDbContext db, IDbContextTransaction transaction,
        CaseRecord caseRecord) : ICaseStatusTransaction
    {
        private bool committed;
        public CaseRecord Case => caseRecord;
        public void AddHistory(CaseStatusHistory history) => db.CaseStatusHistories.Add(history);

        public async Task CommitAsync(CancellationToken ct)
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { if (!committed) db.ChangeTracker.Clear(); }
        }
    }
}
