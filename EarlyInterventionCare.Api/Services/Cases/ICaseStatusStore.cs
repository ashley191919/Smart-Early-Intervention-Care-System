using EarlyInterventionCare.Api.Data.Entities;

namespace EarlyInterventionCare.Api.Services.Cases;

public interface ICaseStatusStore
{
    Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct);
    Task<ICaseStatusTransaction?> OpenAsync(Guid caseId, CancellationToken ct);
}

/// <summary>Owns the case row lock and the transaction for one manual status change.</summary>
public interface ICaseStatusTransaction : IAsyncDisposable
{
    CaseRecord Case { get; }
    void AddHistory(CaseStatusHistory history);
    Task CommitAsync(CancellationToken ct);
}
