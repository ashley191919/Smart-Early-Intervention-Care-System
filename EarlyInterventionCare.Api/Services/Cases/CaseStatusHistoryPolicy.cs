namespace EarlyInterventionCare.Api.Services.Cases;

/// <summary>
/// Defines the validation and creation rules for an in-memory Case Status History entry.
/// It is not an EF Core entity and does not provide database-level immutability.
/// </summary>
public static class CaseStatusHistoryPolicy
{
    public const string Manual = "MANUAL";
    public const string QuestionnaireCompletion = "QUESTIONNAIRE_COMPLETION";
    public const string SystemWorkflow = "SYSTEM_WORKFLOW";

    private static readonly HashSet<string> TriggerSources = new(StringComparer.Ordinal)
    {
        Manual,
        QuestionnaireCompletion,
        SystemWorkflow
    };

    /// <summary>All exact trigger-source values accepted by the interim history contract.</summary>
    public static IReadOnlySet<string> ValidTriggerSources => TriggerSources;

    /// <summary>Returns whether <paramref name="triggerSource"/> is one of the known sources.</summary>
    public static bool IsValidTriggerSource(string? triggerSource) =>
        triggerSource is not null && TriggerSources.Contains(triggerSource);

    /// <summary>
    /// Returns whether the values can describe a history event. This validates only
    /// legal, distinct status values; workflow transition rules remain CaseStatusPolicy's responsibility.
    /// </summary>
    public static bool IsValidStatusChange(string? oldStatus, string? newStatus) =>
        CaseStatusPolicy.IsValid(oldStatus) && CaseStatusPolicy.IsValid(newStatus) && oldStatus != newStatus;

    /// <summary>MANUAL history entries require a non-empty authenticated user identifier.</summary>
    public static bool RequiresChangedByUserId(string? triggerSource) => triggerSource == Manual;

    /// <summary>
    /// Creates an immutable-in-memory history entry using the supplied UTC clock.
    /// System-triggered entries may omit <paramref name="changedByUserId"/>; manual entries may not.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the supplied history values are inconsistent.</exception>
    public static CaseStatusHistoryEntry Create(Guid caseId, string? oldStatus, string? newStatus,
        string? triggerSource, Guid? changedByUserId, TimeProvider clock)
    {
        if (caseId == Guid.Empty) throw new ArgumentException("CaseId is required.", nameof(caseId));
        if (!IsValidStatusChange(oldStatus, newStatus))
            throw new ArgumentException("OldStatus and NewStatus must be distinct legal case statuses.");
        if (!IsValidTriggerSource(triggerSource))
            throw new ArgumentException("TriggerSource is invalid.", nameof(triggerSource));
        if (RequiresChangedByUserId(triggerSource) &&
            (!changedByUserId.HasValue || changedByUserId.Value == Guid.Empty))
            throw new ArgumentException("MANUAL history entries require ChangedByUserId.", nameof(changedByUserId));
        if (clock is null) throw new ArgumentNullException(nameof(clock));

        return new CaseStatusHistoryEntry(Guid.NewGuid(), caseId, oldStatus!, newStatus!,
            clock.GetUtcNow().UtcDateTime, changedByUserId, triggerSource!);
    }
}

/// <summary>
/// Read-only in-memory representation of one valid status-change event. Its get-only
/// properties prevent ordinary mutation in C#, but a future persistence layer must
/// separately enforce append-only behavior.
/// </summary>
public sealed class CaseStatusHistoryEntry
{
    internal CaseStatusHistoryEntry(Guid historyId, Guid caseId, string oldStatus, string newStatus,
        DateTime changedAtUtc, Guid? changedByUserId, string triggerSource)
    {
        HistoryId = historyId;
        CaseId = caseId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ChangedAtUtc = changedAtUtc;
        ChangedByUserId = changedByUserId;
        TriggerSource = triggerSource;
    }

    public Guid HistoryId { get; }
    public Guid CaseId { get; }
    public string OldStatus { get; }
    public string NewStatus { get; }
    public DateTime ChangedAtUtc { get; }
    public Guid? ChangedByUserId { get; }
    public string TriggerSource { get; }
}
