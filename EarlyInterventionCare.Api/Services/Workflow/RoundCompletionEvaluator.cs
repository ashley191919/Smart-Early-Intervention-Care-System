namespace EarlyInterventionCare.Api.Services.Workflow;

/// <summary>One persistence-independent Task view for an explicitly selected assignment round.</summary>
public sealed record RoundTaskSnapshot(Guid CaseId, uint AssignmentRound, Guid TaskId, bool IsRequired,
    string? TaskStatus);

public enum RoundCompletionDecision
{
    Complete,
    Incomplete,
    NoRequiredTasks,
    CancellationPolicyRequired
}

/// <summary>Counts only required Tasks and never decides the policy for a cancelled required Task.</summary>
public sealed record RoundCompletionResult(Guid CaseId, uint AssignmentRound, int RequiredTaskCount,
    int SubmittedRequiredTaskCount, int IncompleteRequiredTaskCount, RoundCompletionDecision Decision)
{
    public bool IsComplete => Decision == RoundCompletionDecision.Complete;
}

/// <summary>
/// Pure completion evaluation for a caller-selected Case and assignment round. It neither
/// infers the current round nor changes any Case, Task, response, or History record.
/// </summary>
public static class RoundCompletionEvaluator
{
    public const string Pending = "PENDING";
    public const string InProgress = "IN_PROGRESS";
    public const string Submitted = "SUBMITTED";
    public const string Cancelled = "CANCELLED";

    private static readonly HashSet<string> TaskStatuses = new(StringComparer.Ordinal)
    {
        Pending,
        InProgress,
        Submitted,
        Cancelled
    };

    public static IReadOnlySet<string> ValidTaskStatuses => TaskStatuses;

    public static bool IsValidTaskStatus(string? taskStatus) =>
        taskStatus is not null && TaskStatuses.Contains(taskStatus);

    public static RoundCompletionResult Evaluate(Guid caseId, uint assignmentRound,
        IEnumerable<RoundTaskSnapshot> taskSnapshots)
    {
        if (caseId == Guid.Empty) throw new ArgumentException("CaseId is required.", nameof(caseId));
        if (assignmentRound == 0) throw new ArgumentOutOfRangeException(nameof(assignmentRound), "AssignmentRound must be positive.");
        if (taskSnapshots is null) throw new ArgumentNullException(nameof(taskSnapshots));

        var tasks = taskSnapshots.ToArray();
        var taskIds = new HashSet<Guid>();
        foreach (var task in tasks)
        {
            if (task.TaskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskSnapshots));
            if (task.CaseId != caseId)
                throw new ArgumentException("Every Task must belong to the requested CaseId.", nameof(taskSnapshots));
            if (task.AssignmentRound != assignmentRound)
                throw new ArgumentException("Every Task must belong to the requested AssignmentRound.", nameof(taskSnapshots));
            if (!IsValidTaskStatus(task.TaskStatus))
                throw new ArgumentException("TaskStatus is invalid.", nameof(taskSnapshots));
            if (!taskIds.Add(task.TaskId))
                throw new ArgumentException("TaskId must be unique within a round snapshot.", nameof(taskSnapshots));
        }

        var required = tasks.Where(task => task.IsRequired).ToArray();
        var requiredCount = required.Length;
        var submittedCount = required.Count(task => task.TaskStatus == Submitted);
        var incompleteCount = requiredCount - submittedCount;

        var decision = requiredCount == 0
            ? RoundCompletionDecision.NoRequiredTasks
            : required.Any(task => task.TaskStatus == Cancelled)
                ? RoundCompletionDecision.CancellationPolicyRequired
                : submittedCount == requiredCount
                    ? RoundCompletionDecision.Complete
                    : RoundCompletionDecision.Incomplete;

        return new RoundCompletionResult(caseId, assignmentRound, requiredCount, submittedCount,
            incompleteCount, decision);
    }
}
