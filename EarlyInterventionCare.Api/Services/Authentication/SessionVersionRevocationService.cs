using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// Unregistered all-session logout core. Uses an atomic compare-and-advance, not a blind increment,
/// so concurrent/retried old logouts cannot repeatedly revoke a later login's version.
/// </summary>
public sealed class SessionVersionRevocationService : ISessionRevocationService
{
    private readonly ISessionVersionRevocationStore _store;

    public SessionVersionRevocationService(ISessionVersionRevocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<SessionRevocationResult> RevokeAllAsync(ClaimsPrincipal? authenticatedPrincipal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SessionVersionClaims.TryRead(authenticatedPrincipal, out var userId, out var version))
            return SessionRevocationResult.Invalid;
        if (version == long.MaxValue)
            return SessionRevocationResult.Unavailable;

        try
        {
            var advanced = await _store.TryAdvanceAsync(userId, version, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (advanced)
                return SessionRevocationResult.Revoked;

            // Resolve concurrent/already-completed logout using a fresh, committed read.
            // There is deliberately no automatic retry with the newly observed version.
            var current = await _store.GetAsync(userId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null)
                return SessionRevocationResult.Invalid;
            if (current.UserId != userId || current.SessionVersion <= 0)
                return SessionRevocationResult.Unavailable;
            if (!string.Equals(current.Status, "ACTIVE", StringComparison.Ordinal) || current.SessionVersion < version)
                return SessionRevocationResult.Invalid;

            return current.SessionVersion > version
                ? SessionRevocationResult.AlreadyRevoked
                : SessionRevocationResult.Unavailable; // Store claimed a mismatch for an unchanged active row.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Includes timeouts and unknown commit outcomes. Do not log/retain exception details,
            // retry blindly, return success or claim that an attempted write was rolled back.
            return SessionRevocationResult.Unavailable;
        }
    }
}
