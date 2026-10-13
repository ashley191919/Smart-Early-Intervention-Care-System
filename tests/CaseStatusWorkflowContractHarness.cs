using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Workflow;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Workflow;

// Offline contract shape/serialization checks only. No implementation is instantiated;
// these checks do not prove transaction sharing, locking, authorization, or deduplication.
var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

var context = new QuestionnaireSubmissionContext(Guid.NewGuid(), 3, Guid.NewGuid(),
    Guid.NewGuid(), "server-trace-001");
var restoredContext = JsonSerializer.Deserialize<QuestionnaireSubmissionContext>(JsonSerializer.Serialize(context));
Check(restoredContext == context, "handoff preserves Case, round, Task, Response and server trace");
using (var json = JsonDocument.Parse(JsonSerializer.Serialize(context)))
{
    var fields = json.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
    Check(fields.SetEquals(["CaseId", "AssignmentRound", "TaskId", "ResponseId", "CorrelationId"]),
        "context contains identifiers only, without answers or client completion/round flags");
}
Check(typeof(QuestionnaireSubmissionContext).GetProperty("AssignmentRound")!.PropertyType == typeof(uint),
    "round uses the same uint type as QuestionnaireTask");

var historyId = Guid.NewGuid();
var applied = new CaseStatusWorkflowResult(context.CaseId, context.AssignmentRound,
    CaseStatusPolicy.WaitingForm, CaseStatusPolicy.FormCompleted, historyId,
    CaseStatusWorkflowDecision.TransitionApplied, "QUESTIONNAIRE_ROUND_COMPLETE");
Check(JsonSerializer.Deserialize<CaseStatusWorkflowResult>(JsonSerializer.Serialize(applied)) == applied,
    "applied outcome preserves statuses, round, History ID, decision and reason");
using (var json = JsonDocument.Parse(JsonSerializer.Serialize(applied)))
{
    var fields = json.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
    Check(fields.SetEquals(["CaseId", "AssignmentRound", "OldStatus", "NewStatus", "HistoryId", "Decision", "ReasonCode"]),
        "result exposes no answers, credentials, actor details or claimed commit flag");
}
Check(typeof(CaseStatusWorkflowResult).GetProperty("HistoryId")!.PropertyType == typeof(Guid?),
    "History ID can be absent for unapplied outcomes");

var noAction = new CaseStatusWorkflowResult(context.CaseId, context.AssignmentRound,
    CaseStatusPolicy.FormCompleted, CaseStatusPolicy.FormCompleted, null,
    CaseStatusWorkflowDecision.NoAction, "ALREADY_FORM_COMPLETED");
Check(JsonSerializer.Deserialize<CaseStatusWorkflowResult>(JsonSerializer.Serialize(noAction)) == noAction,
    "duplicate-event outcome can carry unchanged status and no new History ID");

var pending = new CaseStatusWorkflowResult(context.CaseId, context.AssignmentRound, null, null, null,
    CaseStatusWorkflowDecision.PendingIntegration, "CURRENT_ROUND_NOT_CONFIRMED");
Check(JsonSerializer.Deserialize<CaseStatusWorkflowResult>(JsonSerializer.Serialize(pending)) == pending,
    "unknown status and absent History are preserved as null, not fabricated values");

var method = typeof(ICaseStatusWorkflowService).GetMethod(nameof(ICaseStatusWorkflowService.EvaluateAndApplyAsync))!;
Check(method.ReturnType == typeof(Task<CaseStatusWorkflowResult>), "interface returns the asynchronous outcome contract");
var parameters = method.GetParameters();
Check(parameters.Length == 2 && parameters[0].ParameterType == typeof(QuestionnaireSubmissionContext) &&
    parameters[1].ParameterType == typeof(CancellationToken) && parameters[1].HasDefaultValue,
    "interface accepts context and optional caller cancellation, without an independent transaction factory");

Console.WriteLine($"PASS: {checks} offline CaseStatusWorkflow contract checks; no transaction integration tested.");
