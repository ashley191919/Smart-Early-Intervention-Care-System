namespace EarlyInterventionCare.Api.DTOs.Workflow;

/// <summary>Workflow outcomes within the caller-owned submission transaction.</summary>
public enum CaseStatusWorkflowDecision
{
    /// <summary>Case Status and one History entry are staged together; the transaction is not committed.</summary>
    TransitionApplied,

    /// <summary>No Case Status or History changes, including a duplicate event or an ineligible Case.</summary>
    NoAction,

    /// <summary>No Case Status or History changes because required integration rules are unconfirmed.</summary>
    PendingIntegration
}

/// <summary>
/// Internal outcome, not an EF entity or a submission success receipt.
/// Persistence failures, invalid context, and transaction violations must propagate as errors,
/// not be represented by a successful outcome. DTO construction alone does not enforce these rules.
/// </summary>
/// <param name="CaseId">The Case identified by the verified submission context.</param>
/// <param name="AssignmentRound">The explicit round evaluated; never inferred using MAX(AssignmentRound).</param>
/// <param name="OldStatus">
/// Status observed in the caller's transaction, or null if it could not yet be established.
/// </param>
/// <param name="NewStatus">
/// For TransitionApplied, FORM_COMPLETED. For other outcomes, equal to OldStatus when known,
/// otherwise null. Never use a proposed target here for an unapplied transition.
/// </param>
/// <param name="HistoryId">
/// Non-empty ID of the newly staged History only for TransitionApplied; otherwise null.
/// </param>
/// <param name="Decision">Whether a transition was staged, unnecessary, or awaiting integration.</param>
/// <param name="ReasonCode">Non-empty machine-readable explanation; no answers or sensitive data.</param>
public sealed record CaseStatusWorkflowResult(Guid CaseId, uint AssignmentRound, string? OldStatus,
    string? NewStatus, Guid? HistoryId, CaseStatusWorkflowDecision Decision, string ReasonCode);
