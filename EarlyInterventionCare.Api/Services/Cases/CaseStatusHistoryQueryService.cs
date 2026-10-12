namespace EarlyInterventionCare.Api.Services.Cases;

public sealed class CaseStatusHistoryQueryOperationException(string code, string message, int status)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed record CaseStatusHistoryPage(Guid CaseId, int Page, int PageSize, int TotalCount,
    IReadOnlyList<CaseStatusHistoryData> Items);

/// <summary>Reads a Case's status history without exposing EF entities or actor identity data.</summary>
public sealed class CaseStatusHistoryQueryService(ICaseStatusHistoryStore store)
{
    public const int DefaultPageSize = 10;
    public const int MaximumPageSize = 100;

    public async Task<CaseStatusHistoryPage> GetPageAsync(Guid caseId, int page = 1,
        int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        if (caseId == Guid.Empty || page < 1 || pageSize < 1 || pageSize > MaximumPageSize)
            throw new CaseStatusHistoryQueryOperationException("INVALID_INPUT", "個案識別碼或分頁參數無效。", 400);

        var result = await store.GetPageAsync(caseId, page, pageSize, ct);
        if (result is null)
            throw new CaseStatusHistoryQueryOperationException("CASE_NOT_FOUND", "找不到個案。", 404);

        return new CaseStatusHistoryPage(result.CaseId, page, pageSize, result.TotalCount, result.Items);
    }
}
