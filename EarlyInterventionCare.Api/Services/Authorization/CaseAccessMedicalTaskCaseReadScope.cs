using System.Security.Claims;
using EarlyInterventionCare.Api.Services.Cases;

namespace EarlyInterventionCare.Api.Services.Authorization;

public sealed class CaseAccessMedicalTaskCaseReadScope(CaseAccessService caseAccess)
    : IMedicalTaskCaseReadScope
{
    public Task<bool> CanReadCaseAsync(
        ClaimsPrincipal principal,
        Guid caseId,
        CancellationToken cancellationToken)
        => caseAccess.CanReadAsync(principal, caseId, cancellationToken);
}
