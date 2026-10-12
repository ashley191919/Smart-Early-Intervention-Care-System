using EarlyInterventionCare.Api.Services.Workflow;

// Offline evaluator tests: no host, database provider, secrets, or network connection.
var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

void Error(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    throw new Exception(message);
}

var caseId = Guid.NewGuid();
const uint round = 2;
RoundTaskSnapshot Task(string status, bool required = true, Guid? taskId = null, Guid? forCase = null,
    uint? forRound = null) => new(forCase ?? caseId, forRound ?? round, taskId ?? Guid.NewGuid(), required, status);

Check(RoundCompletionEvaluator.ValidTaskStatuses.SetEquals(new[]
    { RoundCompletionEvaluator.Pending, RoundCompletionEvaluator.InProgress, RoundCompletionEvaluator.Submitted, RoundCompletionEvaluator.Cancelled }),
    "all four TaskStatus constants are valid");
foreach (var status in RoundCompletionEvaluator.ValidTaskStatuses)
    Check(RoundCompletionEvaluator.IsValidTaskStatus(status), "known TaskStatus is accepted: " + status);
foreach (var status in new string?[] { null, "", "submitted", "UNKNOWN" })
    Check(!RoundCompletionEvaluator.IsValidTaskStatus(status), "invalid TaskStatus is rejected");

{
    var result = RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.Submitted), Task(RoundCompletionEvaluator.Submitted) });
    Check(result.Decision == RoundCompletionDecision.Complete && result.IsComplete, "all required submitted is complete");
    Check(result.RequiredTaskCount == 2 && result.SubmittedRequiredTaskCount == 2 && result.IncompleteRequiredTaskCount == 0,
        "complete result has correct required counts");
}

{
    var result = RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.Submitted), Task(RoundCompletionEvaluator.Pending) });
    Check(result.Decision == RoundCompletionDecision.Incomplete && !result.IsComplete, "required pending task is incomplete");
    Check(result.SubmittedRequiredTaskCount == 1 && result.IncompleteRequiredTaskCount == 1, "pending remains incomplete");
}

{
    var result = RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.InProgress) });
    Check(result.Decision == RoundCompletionDecision.Incomplete && result.IncompleteRequiredTaskCount == 1,
        "required in-progress task is incomplete");
}

{
    var result = RoundCompletionEvaluator.Evaluate(caseId, round, Array.Empty<RoundTaskSnapshot>());
    Check(result.Decision == RoundCompletionDecision.NoRequiredTasks && !result.IsComplete && result.RequiredTaskCount == 0,
        "empty round is not complete");
    result = RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.Pending, required: false) });
    Check(result.Decision == RoundCompletionDecision.NoRequiredTasks, "non-required Task does not create a required denominator");
}

{
    var result = RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.Cancelled), Task(RoundCompletionEvaluator.Submitted) });
    Check(result.Decision == RoundCompletionDecision.CancellationPolicyRequired && !result.IsComplete,
        "cancelled required Task waits for policy confirmation");
    Check(result.RequiredTaskCount == 2 && result.SubmittedRequiredTaskCount == 1 && result.IncompleteRequiredTaskCount == 1,
        "cancelled required Task remains visible in counts");
}

{
    var result = RoundCompletionEvaluator.Evaluate(caseId, round, new[]
    {
        Task(RoundCompletionEvaluator.Submitted),
        Task(RoundCompletionEvaluator.Pending, required: false),
        Task(RoundCompletionEvaluator.Cancelled, required: false)
    });
    Check(result.Decision == RoundCompletionDecision.Complete && result.RequiredTaskCount == 1,
        "non-required unfinished Tasks do not affect completion");
}

Error(() => RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.Submitted, forRound: round + 1) }),
    "mixed AssignmentRound must be rejected");
Error(() => RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task(RoundCompletionEvaluator.Submitted, forCase: Guid.NewGuid()) }),
    "mixed CaseId must be rejected");
Error(() => RoundCompletionEvaluator.Evaluate(caseId, round, new[] { Task("UNKNOWN") }),
    "invalid TaskStatus must be rejected");
var duplicateId = Guid.NewGuid();
Error(() => RoundCompletionEvaluator.Evaluate(caseId, round, new[]
    { Task(RoundCompletionEvaluator.Submitted, taskId: duplicateId), Task(RoundCompletionEvaluator.Pending, taskId: duplicateId) }),
    "duplicate TaskId must be rejected");
Error(() => RoundCompletionEvaluator.Evaluate(Guid.Empty, round, Array.Empty<RoundTaskSnapshot>()), "empty CaseId must be rejected");
Error(() => RoundCompletionEvaluator.Evaluate(caseId, 0, Array.Empty<RoundTaskSnapshot>()), "zero AssignmentRound must be rejected");

Console.WriteLine($"PASS: {checks} offline RoundCompletionEvaluator checks; round scope, completion counts and cancellation boundary.");
