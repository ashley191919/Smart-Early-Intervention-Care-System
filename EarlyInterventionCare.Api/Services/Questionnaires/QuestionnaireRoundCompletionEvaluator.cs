namespace EarlyInterventionCare.Api.Services.Questionnaires;

// Internal calculation contract, not an HTTP DTO or a tracked persistence entity.
public sealed record QuestionnaireRoundTask(Guid CaseId, uint AssignmentRound, Guid TaskId,
    bool IsRequired, string? TaskStatus);

public enum QuestionnaireRoundDecision
{
    EmptyRound,
    NoRequiredTasks,
    Complete,
    Incomplete,
    CancellationPolicyRequired
}

public sealed record QuestionnaireRoundCounts(int Submitted, int Pending, int InProgress, int Cancelled)
{
    public int Total => Submitted + Pending + InProgress + Cancelled;
}

public sealed record QuestionnaireRoundCompletion(Guid CaseId, uint AssignmentRound,
    QuestionnaireRoundCounts Required, QuestionnaireRoundCounts Optional, QuestionnaireRoundDecision Decision)
{
    public int TotalTaskCount => Required.Total + Optional.Total;
    // Null explicitly represents a policy decision still outstanding, not an incomplete verdict.
    public bool? IsComplete => Decision switch
    {
        QuestionnaireRoundDecision.Complete => true,
        QuestionnaireRoundDecision.Incomplete => false,
        _ => null
    };
}

/// <summary>
/// Evaluates a complete, caller-selected Case/round snapshot. Rejects mixed scopes rather
/// than silently filtering them, following 07's existing round evaluator boundary.
/// Empty input means no supplied tasks, not proof that a persisted round does not exist.
/// No inference of effective round, authorization, database access or status mutation.
/// </summary>
public static class QuestionnaireRoundCompletionEvaluator
{
    public static QuestionnaireRoundCompletion Evaluate(Guid caseId, uint assignmentRound,
        IEnumerable<QuestionnaireRoundTask> tasks)
    {
        if (caseId == Guid.Empty) throw new ArgumentException("CaseId is required.", nameof(caseId));
        if (assignmentRound == 0) throw new ArgumentOutOfRangeException(nameof(assignmentRound));
        ArgumentNullException.ThrowIfNull(tasks);
        var required = new int[4]; var optional = new int[4]; var ids = new HashSet<Guid>();
        foreach (var task in tasks)
        {
            if (task is null || task.TaskId == Guid.Empty || task.CaseId != caseId ||
                task.AssignmentRound != assignmentRound || !ids.Add(task.TaskId))
                throw new ArgumentException("Tasks must be unique and belong to the specified Case and round.", nameof(tasks));
            var status = task.TaskStatus switch
            {
                "SUBMITTED" => 0, "PENDING" => 1, "IN_PROGRESS" => 2, "CANCELLED" => 3,
                _ => throw new ArgumentException("TaskStatus is invalid.", nameof(tasks))
            };
            (task.IsRequired ? required : optional)[status]++;
        }
        var r = new QuestionnaireRoundCounts(required[0], required[1], required[2], required[3]);
        var o = new QuestionnaireRoundCounts(optional[0], optional[1], optional[2], optional[3]);
        var decision = ids.Count == 0 ? QuestionnaireRoundDecision.EmptyRound
            : r.Total == 0 ? QuestionnaireRoundDecision.NoRequiredTasks
            : r.Cancelled > 0 ? QuestionnaireRoundDecision.CancellationPolicyRequired
            : r.Submitted == r.Total ? QuestionnaireRoundDecision.Complete
            : QuestionnaireRoundDecision.Incomplete;
        return new(caseId, assignmentRound, r, o, decision);
    }
}
