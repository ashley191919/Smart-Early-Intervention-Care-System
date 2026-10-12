using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Cases;

/// <summary>
/// Separates history read scope from status-write scope. A future implementation must
/// verify Case assignment/organization data; a role claim alone is not sufficient.
/// </summary>
public interface ICaseStatusHistoryAccessAuthorizer
{
    Task<CaseStatusHistoryAccessDecision> AuthorizeReadAsync(ClaimsPrincipal user, Guid caseId,
        CancellationToken ct);
}

public sealed record CaseStatusHistoryAccessDecision(bool Allowed, bool CanViewReason, string Code,
    string Message)
{
    public static CaseStatusHistoryAccessDecision Denied(string code, string message) => new(false, false, code, message);
    public static CaseStatusHistoryAccessDecision Granted(bool canViewReason) => new(true, canViewReason, string.Empty, string.Empty);
}

/// <summary>Fails closed until trusted Case-level read authorization is implemented.</summary>
public sealed class DenyAllCaseStatusHistoryAccessAuthorizer : ICaseStatusHistoryAccessAuthorizer
{
    public Task<CaseStatusHistoryAccessDecision> AuthorizeReadAsync(ClaimsPrincipal user, Guid caseId,
        CancellationToken ct) => Task.FromResult(CaseStatusHistoryAccessDecision.Denied(
            "CASE_ACCESS_NOT_INTEGRATED", "個案層級讀取權限尚未整合，無法查詢狀態歷程。"));
}
