namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// Authoritative persistent read + atomic write contract. No production implementation exists.
/// A process-local dictionary/cache is not a valid production implementation.
/// </summary>
public interface ISessionVersionRevocationStore : ISessionSecurityStateStore
{
    /// <summary>
    /// Atomically and durably advance exactly expectedVersion -> expectedVersion + 1 only if
    /// the same user exists, Status is exactly ACTIVE and its current version still matches.
    /// Return true only after commit; false only for a predicate mismatch. Throw on any store
    /// failure/unknown commit outcome. Never use a read-then-unconditional-write sequence.
    /// Reject invalid IDs/nonpositive versions/Int64 overflow; never wrap, reset or decrease.
    /// GetAsync must read committed authoritative state (not a lagging replica/cache).
    /// Concurrent calls with the same expectedVersion may advance it at most once.
    /// </summary>
    Task<bool> TryAdvanceAsync(Guid userId, long expectedVersion,
        CancellationToken cancellationToken = default);
}
