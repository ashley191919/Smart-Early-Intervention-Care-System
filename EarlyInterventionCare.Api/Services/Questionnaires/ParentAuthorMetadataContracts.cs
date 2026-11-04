using EarlyInterventionCare.Api.Data;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>Server-only metadata. A missing row differs from an existing row with unknown author.</summary>
public sealed record ParentAuthorRowMetadata(bool Exists, Guid? ParentAuthorUserId, bool HasTeacherGrant);

public sealed record ParentTaskAuthorMetadata(ParentAuthorRowMetadata Draft, ParentAuthorRowMetadata Response);

/// <summary>
/// Metadata query only, NOT authorization or transaction evidence. The caller must first verify
/// the trusted actor and Case/Task scope. Never expose this data as an HTTP response.
/// 11's IParentAuthorVisibility owns the decision: every existing row must have the same
/// nonempty parent author and no teacher grant. Neither missing rows nor matching authors
/// prove Guardian/Consent eligibility. Do not infer authors from current Guardian bindings.
/// Use the supplied scoped context/transaction; this reader must not own its lifecycle.
/// Separate precheck reads do not prove a consistent snapshot or prevent revocation races.
/// Query/cancellation errors propagate, never become missing-row metadata.
/// </summary>
public interface IParentAuthorMetadataReader
{
    Task<ParentTaskAuthorMetadata> ReadAsync(ApplicationDbContext db, Guid taskId, CancellationToken ct);
}
