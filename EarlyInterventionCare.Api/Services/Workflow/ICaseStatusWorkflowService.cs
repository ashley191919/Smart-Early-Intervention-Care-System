using EarlyInterventionCare.Api.DTOs.Workflow;

namespace EarlyInterventionCare.Api.Services.Workflow;

/// <summary>
/// Server-side integration boundary for Case completion after a verified questionnaire submission.
/// This declaration does not itself enforce trust, transaction sharing, or idempotency.
/// </summary>
/// <remarks>
/// Transaction contract for all future implementations and callers:
/// 1. The Submit coordinator owns BeginTransaction, SaveChanges, Commit, rollback, and disposal.
///    Before calling this method it saves the validated response and SUBMITTED Task in that same
///    transaction without committing, so a database re-query can observe the current submission.
/// 2. Workflow uses the exact same scoped ApplicationDbContext instance and active transaction.
///    It must not BeginTransaction, Commit, dispose the caller's transaction, or create another
///    independent DbContext/scope for status writes. It stages Case and History changes together;
///    the coordinator then SaveChanges and commits the entire submission atomically.
/// 3. Missing transaction, invalid identifiers, or mismatched Case/Task/Response associations are
///    errors. Verify those associations in the transaction; do not trust the context alone.
/// 4. Before staging a transition, re-query CaseStatus, trusted effective-round/sealing metadata,
///    and every required Task of the explicit Case/round, including both PARENT and TEACHER.
///    Do not reuse a grant-only Task list, a caller's completion summary, or stale tracked values.
///    Coordinate current reads and locks with Submit, assignment, and manual status writes; a
///    shared transaction alone does not prevent inconsistent concurrent completion decisions.
/// 5. Use RoundCompletionEvaluator and CaseQuestionnaireWorkflowPolicy. Only WAITING_FORM to
///    FORM_COMPLETED is eligible; never auto-close or reopen a Case. Unconfirmed effective-round,
///    sealing, or required CANCELLED rules yield PendingIntegration with no status/History mutation.
/// 6. Serialize decisions for a Case and re-check its status. Replayed submission/completion
///    events must not insert duplicate History. CorrelationId is not a deduplication key.
/// 7. TransitionApplied means the Case change and one History entry are staged, not committed.
///    Only the coordinator's successful Commit permits a submission success receipt.
/// 8. Propagate cancellation, persistence/concurrency errors, and all failures requiring rollback.
///    Do not swallow them or return TransitionApplied/NoAction/PendingIntegration as success.
///    The coordinator must roll back and discard uncommitted tracked changes on failure.
/// </remarks>
public interface ICaseStatusWorkflowService
{
    /// <summary>
    /// Evaluates an authorized server-created submission context within the caller's transaction
    /// and, if eligible, stages a Case Status update and its History entry together.
    /// No implementation or DI registration is supplied by this contract.
    /// </summary>
    /// <exception cref="ArgumentException">The context contains invalid identifiers or diagnostic data.</exception>
    /// <exception cref="InvalidOperationException">
    /// The required shared transaction is absent or the verified submission associations are inconsistent.
    /// </exception>
    /// <exception cref="OperationCanceledException">The caller cancels the operation.</exception>
    Task<CaseStatusWorkflowResult> EvaluateAndApplyAsync(QuestionnaireSubmissionContext context,
        CancellationToken cancellationToken = default);
}
