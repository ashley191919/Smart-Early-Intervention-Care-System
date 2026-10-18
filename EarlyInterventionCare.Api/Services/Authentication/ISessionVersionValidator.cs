namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Checks current account status and version; does not authenticate a JWT or grant permissions.</summary>
public interface ISessionVersionValidator
{
    /// <summary>
    /// The future JWT adapter must first verify signature/issuer/audience/lifetime and reject
    /// missing, duplicate or malformed identity/version claims. Only pass values from that
    /// authenticated token, never from a request body. This core does not read or mutate claims.
    /// </summary>
    Task<SessionValidationResult> ValidateAsync(Guid userId, long jwtSessionVersion,
        CancellationToken cancellationToken = default);
}
