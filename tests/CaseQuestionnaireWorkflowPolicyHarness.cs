using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Workflow;

// Offline policy tests: no host, database provider, secrets, or network connection.
var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

var caseId = Guid.NewGuid();
const uint round = 3;
RoundCompletionResult Completion(params string[] statuses) => RoundCompletionEvaluator.Evaluate(caseId, round,
    statuses.Select(status => new RoundTaskSnapshot(caseId, round, Guid.NewGuid(), true, status)));
CaseQuestionnaireWorkflowResult Decide(string status, RoundCompletionResult completion, bool current = true,
    bool sealedRound = true) => CaseQuestionnaireWorkflowPolicy.Decide(status, completion, current, sealedRound);

var complete = Completion(RoundCompletionEvaluator.Submitted, RoundCompletionEvaluator.Submitted);
var incomplete = Completion(RoundCompletionEvaluator.Submitted, RoundCompletionEvaluator.Pending);
var cancelled = Completion(RoundCompletionEvaluator.Cancelled);

{
    var result = Decide(CaseStatusPolicy.WaitingForm, complete);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.TransitionAllowed,
        "waiting form with confirmed sealed complete round allows transition");
    Check(result.TargetStatus == CaseStatusPolicy.FormCompleted && result.ReasonCode == "QUESTIONNAIRE_ROUND_COMPLETE",
        "allowed transition targets form completed only");
    Check(result.CaseId == caseId && result.AssignmentRound == round, "decision preserves trusted round identity");
}

{
    var result = Decide(CaseStatusPolicy.WaitingForm, complete, current: false);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.PendingIntegration && result.ReasonCode == "CURRENT_ROUND_NOT_CONFIRMED",
        "unconfirmed current round remains pending");
    result = Decide(CaseStatusPolicy.WaitingForm, complete, sealedRound: false);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.PendingIntegration && result.ReasonCode == "ROUND_NOT_SEALED",
        "unsealed round remains pending");
}

{
    var result = Decide(CaseStatusPolicy.WaitingForm, incomplete);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.NotEligible && result.ReasonCode == "REQUIRED_QUESTIONNAIRES_NOT_COMPLETE",
        "incomplete required forms cannot transition");
    result = Decide(CaseStatusPolicy.WaitingForm, cancelled);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.PendingIntegration && result.ReasonCode == "CANCELLATION_POLICY_REQUIRED",
        "cancelled required forms wait for an explicit policy");
}

{
    var result = Decide(CaseStatusPolicy.FormCompleted, complete);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.NoAction && result.TargetStatus is null,
        "already form completed does not transition again");
    var repeated = Decide(CaseStatusPolicy.FormCompleted, complete);
    Check(repeated.Decision == CaseQuestionnaireWorkflowDecision.NoAction && repeated.ReasonCode == "ALREADY_FORM_COMPLETED",
        "repeated completion event cannot request a second History entry");
}

{
    var result = Decide(CaseStatusPolicy.Completed, complete);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.NotEligible && result.ReasonCode == "CASE_ALREADY_COMPLETED",
        "completed Case cannot be reopened by questionnaire event");
    result = Decide("UNKNOWN", complete);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.NotEligible && result.ReasonCode == "INVALID_CASE_STATUS",
        "invalid CaseStatus is not eligible");
    result = Decide(CaseStatusPolicy.Appointed, complete);
    Check(result.Decision == CaseQuestionnaireWorkflowDecision.NotEligible && result.ReasonCode == "CASE_NOT_WAITING_FOR_FORM",
        "only waiting form may use questionnaire transition");
}

Console.WriteLine($"PASS: {checks} offline CaseQuestionnaireWorkflowPolicy checks; trusted round gates and idempotent decision boundary.");
