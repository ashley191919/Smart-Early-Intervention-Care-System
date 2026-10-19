using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Revoke all sessions of the authenticated actor, not an account ID from a request.</summary>
public interface ISessionRevocationService
{
    // Future endpoint must first pass JWT/version validation and existing Cookie CSRF/Origin.
    Task<SessionRevocationResult> RevokeAllAsync(ClaimsPrincipal? authenticatedPrincipal,
        CancellationToken cancellationToken = default);
}
