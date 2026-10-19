using System.Text.Json;
using EarlyInterventionCare.Api.Services.Questionnaires;

// Pure calculation only: no Fake Store, ADO interception, host or MySQL.
var checks = 0;
var caseId = Guid.NewGuid();
QuestionnaireRoundTask TaskRow(string status = "SUBMITTED", bool required = true, uint round = 1) =>
    new(caseId, round, Guid.NewGuid(), required, status);
QuestionnaireRoundCompletion Evaluate(params QuestionnaireRoundTask[] tasks) =>
    QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, 1, tasks);
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
void Reject(Action action, string label)
{
    try { action(); } catch (ArgumentException) { Check(true, label); return; }
    throw new Exception("Expected invalid input: " + label);
}

// Confirmed technical rules: explicit scope, only required SUBMITTED tasks determine completion.
var complete = Evaluate(TaskRow(), TaskRow());
Check(complete.Decision == QuestionnaireRoundDecision.Complete && complete.IsComplete == true, "all required submitted");
Check(complete.Required.Submitted == 2 && complete.TotalTaskCount == 2 && complete.Optional.Total == 0, "complete counts");
Check(complete.CaseId == caseId && complete.AssignmentRound == 1, "result scope");
var partial = Evaluate(TaskRow(), TaskRow("PENDING"), TaskRow("IN_PROGRESS"));
Check(partial.Decision == QuestionnaireRoundDecision.Incomplete && partial.IsComplete == false, "partial required completion");
Check(partial.Required == new QuestionnaireRoundCounts(1, 1, 1, 0), "required status counts");
var optional = Evaluate(TaskRow(), TaskRow("PENDING", false), TaskRow("IN_PROGRESS", false), TaskRow("CANCELLED", false));
Check(optional.IsComplete == true && optional.Required.Total == 1, "optional tasks never block required completion");
Check(optional.Optional == new QuestionnaireRoundCounts(0, 1, 1, 1) && optional.TotalTaskCount == 4, "optional cancelled preserved separately");

var history = new[] { TaskRow(), TaskRow("PENDING", round: 2), TaskRow("CANCELLED", round: 3) };
var before = JsonSerializer.Serialize(history);
var first = QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, 1, history.Where(t => t.AssignmentRound == 1));
var second = QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, 2, history.Where(t => t.AssignmentRound == 2));
Check(first.IsComplete == true && second.IsComplete == false, "rounds independent");
Check(first.Required.Total == 1 && second.Required.Total == 1, "no cross-round aggregation");
Check(before == JsonSerializer.Serialize(history), "historical tasks unchanged");
Check(QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, 1, history.Where(t => t.AssignmentRound == 1)) == first,
    "historical result remains available and stable");
Reject(() => Evaluate(history), "mixed rounds fail closed");

// Pending business decisions: assert explicit unresolved results, never invent a completion policy.
var none = Evaluate(TaskRow("SUBMITTED", false), TaskRow("PENDING", false));
Check(none.Decision == QuestionnaireRoundDecision.NoRequiredTasks && none.IsComplete is null, "no required tasks unresolved");
Check(none.Required.Total == 0 && none.Optional.Total == 2, "optional-only counts preserved");
foreach (var otherStatus in new[] { "SUBMITTED", "PENDING", "IN_PROGRESS" })
{
    var cancelled = Evaluate(TaskRow("CANCELLED"), TaskRow(otherStatus));
    Check(cancelled.Decision == QuestionnaireRoundDecision.CancellationPolicyRequired && cancelled.IsComplete is null,
        "cancelled required task awaits policy regardless of other tasks");
    Check(cancelled.Required.Cancelled == 1 && cancelled.Required.Total == 2, "cancelled required never discarded");
}
var missing = QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, 99, history.Where(t => t.AssignmentRound == 99));
Check(missing.Decision == QuestionnaireRoundDecision.EmptyRound && missing.IsComplete is null, "missing round not automatically complete");
Check(missing.AssignmentRound == 99 && missing.TotalTaskCount == 0, "empty snapshot preserves requested round");

// Invalid inputs are technical failures; never return a misleading completion summary.
var duplicate = TaskRow();
Reject(() => Evaluate(duplicate, duplicate), "duplicate task");
Reject(() => Evaluate(duplicate, duplicate with { TaskStatus = "PENDING", IsRequired = false }), "conflicting duplicate task");
foreach (var status in new string?[] { null, "", "submitted", " SUBMITTED", "UNKNOWN" })
    Reject(() => Evaluate(TaskRow() with { TaskStatus = status }), "invalid status");
Reject(() => Evaluate(TaskRow() with { TaskId = Guid.Empty }), "empty task id");
Reject(() => Evaluate(TaskRow() with { CaseId = Guid.NewGuid() }), "foreign case");
Reject(() => Evaluate(TaskRow() with { AssignmentRound = 0 }), "invalid row round");
Reject(() => Evaluate(null!), "null collection");
Reject(() => Evaluate(new QuestionnaireRoundTask[] { null! }), "null row");
Reject(() => QuestionnaireRoundCompletionEvaluator.Evaluate(Guid.Empty, 1, []), "empty case");
Reject(() => QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, 0, []), "zero requested round");
var unordered = new[] { TaskRow("PENDING"), TaskRow(), TaskRow("SUBMITTED", false) };
Check(Evaluate(unordered) == Evaluate(unordered.Reverse().ToArray()), "order independent");
Console.WriteLine($"PASS: {checks} pure round-completion checks; confirmed technical rules and explicit pending-policy scenarios; no database.");
