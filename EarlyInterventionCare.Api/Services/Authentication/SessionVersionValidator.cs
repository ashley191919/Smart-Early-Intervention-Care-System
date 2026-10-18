namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// Transport-independent, fail-closed version validation. No caching, database writes, JWT changes,
/// Cookie/CSRF handling, endpoints or runtime registration are supplied by this core.
/// </summary>
public sealed class SessionVersionValidator : ISessionVersionValidator
{
    private readonly ISessionSecurityStateStore _store;

    public SessionVersionValidator(ISessionSecurityStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<SessionValidationResult> ValidateAsync(Guid userId, long jwtSessionVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty || jwtSessionVersion <= 0)
            return SessionValidationResult.Invalid;

        SessionSecurityState? state;
        try
        {
            state = await _store.GetAsync(userId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Only the store call is inside this failure boundary. An unsuccessful lookup,
            // including an independent store timeout/cancellation, is not invalid credentials.
            // Do not expose provider exception messages or retain them in the result.
            cancellationToken.ThrowIfCancellationRequested();
            return SessionValidationResult.Unavailable;
        }

        // A store that ignores cancellation must not turn a cancelled operation into success.
        cancellationToken.ThrowIfCancellationRequested();
        if (state is null)
            return SessionValidationResult.Invalid;

        // Wrong-user or malformed store data is a server lookup/integrity failure, not a match.
        if (state.UserId != userId || state.SessionVersion <= 0)
            return SessionValidationResult.Unavailable;

        return string.Equals(state.Status, "ACTIVE", StringComparison.Ordinal) &&
               state.SessionVersion == jwtSessionVersion
            ? SessionValidationResult.Allowed
            : SessionValidationResult.Invalid;
    }
}
