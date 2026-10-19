using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>One version check for both Bearer and staff Cookie, after normal JWT validation.</summary>
public interface ISessionPrincipalValidator
{
    // IsAuthenticated is necessary, not proof of signature validation. The integration caller
    // must pass only the validated token principal, never construct it from body/header claims.
    Task<SessionValidationResult> ValidateAsync(ClaimsPrincipal? authenticatedPrincipal,
        CancellationToken cancellationToken = default);
}
