using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Cases;

public interface ICaseStatusHistoryStore
{
    /// <summary>Returns null only when the requested Case does not exist.</summary>
    Task<CaseStatusHistoryPageData?> GetPageAsync(Guid caseId, int page, int pageSize,
        CancellationToken ct);
}

/// <summary>Internal read model. It deliberately omits ChangedByUserId and navigation data.</summary>
public sealed record CaseStatusHistoryPageData(Guid CaseId, int TotalCount,
    IReadOnlyList<CaseStatusHistoryData> Items);

public sealed record CaseStatusHistoryData(Guid HistoryId, Guid CaseId, string OldStatus, string NewStatus,
    DateTime ChangedAtUtc, string TriggerSource, string? Reason);

public sealed class EfCaseStatusHistoryStore(ApplicationDbContext db) : ICaseStatusHistoryStore
{
    public async Task<CaseStatusHistoryPageData?> GetPageAsync(Guid caseId, int page, int pageSize,
        CancellationToken ct)
    {
        var exists = await db.Cases.AsNoTracking().AnyAsync(caseRecord => caseRecord.CaseId == caseId, ct);
        if (!exists) return null;

        var histories = db.CaseStatusHistories.AsNoTracking().Where(history => history.CaseId == caseId);
        var totalCount = await histories.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue)
            return new CaseStatusHistoryPageData(caseId, totalCount, Array.Empty<CaseStatusHistoryData>());

        var items = await histories
            .OrderByDescending(history => history.ChangedAtUtc)
            .ThenByDescending(history => history.HistoryId)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(history => new CaseStatusHistoryData(history.HistoryId, history.CaseId,
                history.OldStatus, history.NewStatus, history.ChangedAtUtc, history.TriggerSource, history.Reason))
            .ToArrayAsync(ct);

        return new CaseStatusHistoryPageData(caseId, totalCount, items);
    }
}
