using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Unregistered adapter. Missing/malformed claims never fall back to version 1.</summary>
public sealed class SessionPrincipalValidator : ISessionPrincipalValidator
{
    private readonly ISessionVersionValidator _versions;

    public SessionPrincipalValidator(ISessionVersionValidator versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        _versions = versions;
    }

    public Task<SessionValidationResult> ValidateAsync(ClaimsPrincipal? authenticatedPrincipal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SessionVersionClaims.TryRead(authenticatedPrincipal, out var userId, out var version)
            ? _versions.ValidateAsync(userId, version, cancellationToken)
            : Task.FromResult(SessionValidationResult.Invalid);
    }
}
