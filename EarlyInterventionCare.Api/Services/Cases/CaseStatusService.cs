using EarlyInterventionCare.Api.Data.Entities;

namespace EarlyInterventionCare.Api.Services.Cases;

public sealed class CaseStatusOperationException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed record CaseStatusChangeResult(Guid CaseId, string OldStatus, string NewStatus,
    DateTime ChangedAtUtc, Guid ChangedByUserId, Guid HistoryId, string? Reason);

/// <summary>Coordinates one manually requested status change and its History record.</summary>
public sealed class CaseStatusService(ICaseStatusStore store, ICaseStatusActorContext actorContext,
    TimeProvider clock)
{
    private static CaseStatusOperationException Error(string code, string message, int status) => new(code, message, status);

    /// <summary>
    /// Changes a Case Status through the interim manual workflow. The actor is read
    /// from the authenticated server-side context, never from caller-supplied input.
    /// </summary>
    public async Task<CaseStatusChangeResult> ChangeManuallyAsync(Guid caseId, string? targetStatus,
        string? reason, CancellationToken ct = default)
    {
        if (caseId == Guid.Empty) throw Error("INVALID_INPUT", "個案識別碼無效。", 400);
        var changedByUserId = actorContext.GetAuthenticatedUserId();
        if (changedByUserId is null) throw Error("AUTHENTICATED_USER_REQUIRED", "需要有效登入身分。", 401);
        if (!await store.IsActiveUserAsync(changedByUserId.Value, ct))
            throw Error("AUTHENTICATED_USER_REQUIRED", "需要有效登入身分。", 401);

        await using var transaction = await store.OpenAsync(caseId, ct);
        if (transaction is null) throw Error("CASE_NOT_FOUND", "找不到個案。", 404);

        var oldStatus = transaction.Case.CaseStatus;
        if (!CaseStatusPolicy.CanManuallyTransition(oldStatus, targetStatus))
            throw Error("INVALID_STATUS_TRANSITION", "不允許此人工個案狀態轉換。", 409);

        var entry = CaseStatusHistoryPolicy.Create(caseId, oldStatus, targetStatus,
            CaseStatusHistoryPolicy.Manual, changedByUserId.Value, clock);
        transaction.Case.CaseStatus = entry.NewStatus;
        transaction.Case.UpdatedAtUtc = entry.ChangedAtUtc;
        transaction.AddHistory(new CaseStatusHistory
        {
            HistoryId = entry.HistoryId,
            CaseId = entry.CaseId,
            OldStatus = entry.OldStatus,
            NewStatus = entry.NewStatus,
            ChangedAtUtc = entry.ChangedAtUtc,
            ChangedByUserId = entry.ChangedByUserId,
            TriggerSource = entry.TriggerSource,
            Reason = reason
        });
        await transaction.CommitAsync(ct);

        return new(caseId, entry.OldStatus, entry.NewStatus, entry.ChangedAtUtc,
            changedByUserId.Value, entry.HistoryId, reason);
    }
}
