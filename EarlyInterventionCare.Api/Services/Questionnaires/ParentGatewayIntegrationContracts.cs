using System.Security.Claims;
using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>Unwired integration contracts, not an enabled Gateway or permission.</summary>
public static class ParentGatewayIntegrationContract
{
    public const string Version = "parent-bridge-contract-1";
}

/// <summary>Parent-only input. Relationship may be absent in an unfilled draft.</summary>
public sealed record ParentSaveDraftCommand(
    SaveQuestionnaireDraftRequest Payload, string? RespondentRelationship);

/// <summary>
/// First submission compares ExpectedRevision with the stored draft (absent draft = 0).
/// A matching successful retry is resolved before that comparison. Revision is not hash content.
/// Relationship is required on submit; non-null annotations do not replace runtime validation.
/// </summary>
public sealed record ParentSubmitCommand(
    QuestionnaireSubmission Payload, ulong ExpectedRevision, string RespondentRelationship);

/// <summary>Legacy/unfilled relationship stays null; never infer it from Guardian data.</summary>
public sealed record ParentDraftResult(
    QuestionnaireDraftResponse Draft, string? RespondentRelationship);

/// <summary>
/// Server precheck comparison data ONLY. Never bind from HTTP or treat as authorization.
/// EligibilityStamp is an opaque versioned snapshot of eligibility-affecting state,
/// not a bearer token or the submission hash. 11 and 16 must still agree its algorithm,
/// change detection (including revoke/rebind), and writer/locking protocol before use.
/// All IDs and the stamp must be present and match a freshly authorized transaction read.
/// A changed snapshot must be rejected, never silently replaced or automatically retried.
/// </summary>
public sealed record ParentAccessExpectation(
    Guid ActorUserId, Guid OrganizationId, Guid CaseId, Guid GuardianId,
    Guid ConsentId, Guid DocumentVersionId, string EligibilityStamp);

/// <summary>
/// Carries the EXISTING trusted principal by reference; constructing this record grants nothing.
/// Never reconstruct a principal from the expectation's IDs. The adapter resolves the sole
/// authenticated userId and checks it against expectation and authoritative storage.
/// </summary>
public sealed record ParentGatewayCallContext(
    ClaimsPrincipal Principal, ParentAccessExpectation ExpectedAccess);

/// <summary>
/// Transaction-only evidence returned after fresh authorization by a reviewed adapter.
/// It remains data, not a token: RevalidateAsync must read storage again in the same transaction.
/// </summary>
public sealed record ParentTransactionAccess(Guid TaskId, ParentAccessExpectation Snapshot);

/// <summary>
/// Future 11 implementation boundary; NOT the currently registered IParentTaskAccessAdapter.
/// No registration or forwarding implementation is provided. Until the lock protocol is
/// jointly reviewed, use DenyParentTransactionAccessAdapter and do not expose an entry point.
/// </summary>
public interface IParentTransactionAccessAdapter
{
    /// <summary>
    /// Use transaction.DbContext: it must be the same scoped ApplicationDbContext and have
    /// its existing active transaction. Do not Begin/Commit/dispose it, open another context,
    /// call a remote service, trust cached approval, or stage unrelated writes.
    /// Resolve ACTIVE user/org, parent role, Case/Task/Guardian binding and applicable Consent
    /// using authoritative current reads and the jointly agreed locks; compare ALL expected
    /// state. Reject changed consent/document/binding without replacement/retry.
    /// Hold locks until the coordinator disposes the transaction. Cross-author Task/Draft
    /// must be masked as not found. writing=false is NOT a bypass; SUBMITTED must remain
    /// eligible for authorized replay/receipt checks, not for a new submission.
    /// Exact lock order, isolation, stamp generation and all revocation writers remain pending.
    /// </summary>
    Task<ParentTransactionAccess> LockAndAuthorizeAsync(
        IParentQuestionnaireTransaction transaction, ClaimsPrincipal principal, Guid taskId,
        ParentAccessExpectation expectedAccess, bool writing, CancellationToken ct = default);

    /// <summary>
    /// Recheck trusted principal, all scope/eligibility and the original snapshot immediately
    /// before the coordinator commits, including reads and receipt replay. Use the identical
    /// context/transaction and fresh reads, not old tracked values or a stale snapshot.
    /// Propagate denial/cancellation/storage failures; never turn them into success.
    /// </summary>
    Task RevalidateAsync(
        IParentQuestionnaireTransaction transaction, ClaimsPrincipal principal,
        ParentTransactionAccess access, CancellationToken ct = default);
}

/// <summary>Pure revision conversion only; no HTTP status, author, scope or transaction logic.</summary>
public interface IParentRevisionCodec
{
    bool TryParse(string? text, out ulong revision);
    string Format(ulong revision);
}

/// <summary>
/// Candidate canonical CONTENT shape, to be built from validated/normalized parent input.
/// Excludes ExpectedRevision, key and client-supplied authors. Relationship is included.
/// This is not an encoder implementation or a clinical/consent policy.
/// </summary>
public sealed record ParentCanonicalSubmission(
    Guid TaskId, Guid QuestionnaireVersionId, string RespondentName, DateOnly FilledOn,
    IReadOnlyList<QuestionnaireAnswer> Answers, string? Observation, string RespondentRelationship);

/// <summary>
/// Pure encoding boundary only. A future implementation must define versioned deterministic
/// UTF-8 bytes, answer ordering, null/text rules and legacy hash handling jointly with 11.
/// SHA-256 of those bytes is the parent's stored payload hash. No encoder is supplied or enabled.
/// Teacher ValidatedQuestionnaireContent.SubmissionHash is unchanged.
/// </summary>
public interface IParentCanonicalSubmissionEncoder
{
    byte[] Encode(ParentCanonicalSubmission content);
}
