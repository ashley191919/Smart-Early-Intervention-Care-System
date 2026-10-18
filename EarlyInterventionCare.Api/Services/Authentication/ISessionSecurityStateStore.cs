namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Server-owned source of current security state, not client-supplied account data.</summary>
public interface ISessionSecurityStateStore
{
    /// <summary>
    /// Read the requested user's status and version together from the authoritative store on
    /// every call. Return null only when the user is absent. Throw on lookup failure; never
    /// return fabricated defaults, stale fallback data or null for a database outage.
    /// Propagate the caller's cancellation. No EF implementation or DI registration exists yet.
    /// </summary>
    Task<SessionSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}
