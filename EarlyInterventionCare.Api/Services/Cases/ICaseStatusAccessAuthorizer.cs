using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Cases;

/// <summary>
/// Authorizes a status write against an actual Case-to-user scope. Role claims alone
/// are deliberately insufficient because this project has not yet modelled that scope.
/// </summary>
public interface ICaseStatusAccessAuthorizer
{
    Task<CaseStatusAccessDecision> AuthorizeChangeAsync(ClaimsPrincipal user, Guid caseId,
        CancellationToken ct);
}

public sealed record CaseStatusAccessDecision(bool Allowed, string Code, string Message)
{
    public static CaseStatusAccessDecision Denied(string code, string message) => new(false, code, message);
    public static CaseStatusAccessDecision Granted() => new(true, string.Empty, string.Empty);
}

/// <summary>
/// Production-safe placeholder until a trusted Case assignment/organization access
/// service exists. It contains no configuration switch and therefore cannot be enabled
/// accidentally by an API caller or deployment setting.
/// </summary>
public sealed class DenyAllCaseStatusAccessAuthorizer : ICaseStatusAccessAuthorizer
{
    public Task<CaseStatusAccessDecision> AuthorizeChangeAsync(ClaimsPrincipal user, Guid caseId,
        CancellationToken ct) => Task.FromResult(CaseStatusAccessDecision.Denied(
            "CASE_ACCESS_NOT_INTEGRATED", "個案層級權限尚未整合，無法修改個案狀態。"));
}
