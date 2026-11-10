using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Authorization;

/// <summary>Checks case scope for medical Task and Completion reads.</summary>
/// <remarks>The caller authenticates the principal and checks role and Task read permission.</remarks>
public interface IMedicalTaskCaseReadScope
{
    Task<bool> CanReadCaseAsync(
        ClaimsPrincipal principal,
        Guid caseId,
        CancellationToken cancellationToken);
}
