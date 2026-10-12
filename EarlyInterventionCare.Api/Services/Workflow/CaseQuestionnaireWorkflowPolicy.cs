using EarlyInterventionCare.Api.Services.Cases;

namespace EarlyInterventionCare.Api.Services.Workflow;

public enum CaseQuestionnaireWorkflowDecision
{
    TransitionAllowed,
    NoAction,
    PendingIntegration,
    NotEligible
}

/// <summary>
/// A persistence-independent decision only. TargetStatus is set solely for an allowed
/// transition; callers remain responsible for a later transaction and History write.
/// </summary>
public sealed record CaseQuestionnaireWorkflowResult(Guid CaseId, uint AssignmentRound,
    CaseQuestionnaireWorkflowDecision Decision, string? TargetStatus, string ReasonCode);

/// <summary>
/// Determines whether verified questionnaire completion is eligible for the sole
/// questionnaire-originated Case transition: WAITING_FORM to FORM_COMPLETED.
/// The two confirmation flags must originate from trusted server-side round metadata;
/// never bind them from an API request or other client-controlled input.
/// </summary>
public static class CaseQuestionnaireWorkflowPolicy
{
    public static CaseQuestionnaireWorkflowResult Decide(string? currentCaseStatus,
        RoundCompletionResult roundCompletion, bool currentEffectiveRoundConfirmed,
        bool assignmentRoundSealed)
    {
        ArgumentNullException.ThrowIfNull(roundCompletion);
        if (roundCompletion.CaseId == Guid.Empty || roundCompletion.AssignmentRound == 0)
            throw new ArgumentException("RoundCompletionResult must identify a Case and a positive AssignmentRound.", nameof(roundCompletion));

        if (!CaseStatusPolicy.IsValid(currentCaseStatus))
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.NotEligible, null, "INVALID_CASE_STATUS");

        // This comes before completion checks so duplicate completion events cannot add History.
        if (currentCaseStatus == CaseStatusPolicy.FormCompleted)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.NoAction, null, "ALREADY_FORM_COMPLETED");

        if (currentCaseStatus == CaseStatusPolicy.Completed)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.NotEligible, null, "CASE_ALREADY_COMPLETED");

        if (currentCaseStatus != CaseStatusPolicy.WaitingForm)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.NotEligible, null, "CASE_NOT_WAITING_FOR_FORM");

        if (!currentEffectiveRoundConfirmed)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.PendingIntegration, null,
                "CURRENT_ROUND_NOT_CONFIRMED");

        if (!assignmentRoundSealed)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.PendingIntegration, null,
                "ROUND_NOT_SEALED");

        if (roundCompletion.Decision == RoundCompletionDecision.CancellationPolicyRequired)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.PendingIntegration, null,
                "CANCELLATION_POLICY_REQUIRED");

        if (roundCompletion.Decision != RoundCompletionDecision.Complete)
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.NotEligible, null,
                "REQUIRED_QUESTIONNAIRES_NOT_COMPLETE");

        if (!CaseStatusPolicy.CanTransitionFromQuestionnaireCompletion(currentCaseStatus, CaseStatusPolicy.FormCompleted))
            return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.NotEligible, null,
                "QUESTIONNAIRE_TRANSITION_NOT_ALLOWED");

        return Result(roundCompletion, CaseQuestionnaireWorkflowDecision.TransitionAllowed,
            CaseStatusPolicy.FormCompleted, "QUESTIONNAIRE_ROUND_COMPLETE");
    }

    private static CaseQuestionnaireWorkflowResult Result(RoundCompletionResult roundCompletion,
        CaseQuestionnaireWorkflowDecision decision, string? targetStatus, string reasonCode) =>
        new(roundCompletion.CaseId, roundCompletion.AssignmentRound, decision, targetStatus, reasonCode);
}
