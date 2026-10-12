using EarlyInterventionCare.Api.Services.Cases;

// Offline policy tests: no server startup, database provider, secrets, or network connection.
var checks = 0;
void Check(bool result, string message)
{
    checks++;
    if (!result) throw new Exception(message);
}

var statuses = new[]
{
    CaseStatusPolicy.New,
    CaseStatusPolicy.ToContact,
    CaseStatusPolicy.Appointed,
    CaseStatusPolicy.WaitingForm,
    CaseStatusPolicy.FormCompleted,
    CaseStatusPolicy.Completed
};

Check(statuses.Distinct(StringComparer.Ordinal).Count() == 6, "six distinct case-status constants");
Check(CaseStatusPolicy.ValidStatuses.SetEquals(statuses), "valid-status set matches constants");
foreach (var status in statuses)
    Check(CaseStatusPolicy.IsValid(status), "known status is valid: " + status);
foreach (var invalid in new string?[] { null, "", "new", " NEW", "UNKNOWN" })
    Check(!CaseStatusPolicy.IsValid(invalid), "unknown status is rejected");

foreach (var (current, target) in new[]
{
    (CaseStatusPolicy.New, CaseStatusPolicy.ToContact),
    (CaseStatusPolicy.ToContact, CaseStatusPolicy.Appointed),
    (CaseStatusPolicy.Appointed, CaseStatusPolicy.WaitingForm),
    (CaseStatusPolicy.FormCompleted, CaseStatusPolicy.Completed)
})
    Check(CaseStatusPolicy.CanManuallyTransition(current, target), "approved manual transition");

Check(!CaseStatusPolicy.CanManuallyTransition(CaseStatusPolicy.WaitingForm, CaseStatusPolicy.FormCompleted),
    "form-completed cannot be set manually");
Check(!CaseStatusPolicy.CanManuallyTransition(CaseStatusPolicy.New, CaseStatusPolicy.Completed),
    "new cannot skip directly to completed");
Check(!CaseStatusPolicy.CanManuallyTransition(CaseStatusPolicy.Completed, CaseStatusPolicy.Completed),
    "completed has no manual self-transition");
Check(!CaseStatusPolicy.CanManuallyTransition(CaseStatusPolicy.Completed, CaseStatusPolicy.WaitingForm),
    "no unconfirmed reopen rule");
Check(!CaseStatusPolicy.CanManuallyTransition("UNKNOWN", CaseStatusPolicy.ToContact),
    "unknown source status is rejected");
Check(!CaseStatusPolicy.CanManuallyTransition(CaseStatusPolicy.New, "UNKNOWN"),
    "unknown target status is rejected");

Check(CaseStatusPolicy.CanTransitionFromQuestionnaireCompletion(
        CaseStatusPolicy.WaitingForm, CaseStatusPolicy.FormCompleted),
    "verified questionnaire completion may enter form-completed");
Check(!CaseStatusPolicy.CanTransitionFromQuestionnaireCompletion(
        CaseStatusPolicy.WaitingForm, CaseStatusPolicy.Completed),
    "questionnaire completion cannot auto-complete a case");
Check(!CaseStatusPolicy.CanTransitionFromQuestionnaireCompletion(
        CaseStatusPolicy.Completed, CaseStatusPolicy.FormCompleted),
    "completed cannot change from questionnaire event");
Check(!CaseStatusPolicy.CanTransitionFromQuestionnaireCompletion(
        CaseStatusPolicy.FormCompleted, CaseStatusPolicy.Completed),
    "questionnaire event cannot replace staff case completion");

Console.WriteLine($"PASS: {checks} offline CaseStatusPolicy checks; constants, validation, manual workflow and questionnaire-event boundary.");
