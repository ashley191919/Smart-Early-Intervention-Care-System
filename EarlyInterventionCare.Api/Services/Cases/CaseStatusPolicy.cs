namespace EarlyInterventionCare.Api.Services.Cases;

/// <summary>
/// Defines the interim Case Status rules without reading or writing persistence.
/// Questionnaire completion must be established by a caller before it uses the
/// questionnaire-event transition method.
/// </summary>
public static class CaseStatusPolicy
{
    public const string New = "NEW";
    public const string ToContact = "TO_CONTACT";
    public const string Appointed = "APPOINTED";
    public const string WaitingForm = "WAITING_FORM";
    public const string FormCompleted = "FORM_COMPLETED";
    public const string Completed = "COMPLETED";

    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        New,
        ToContact,
        Appointed,
        WaitingForm,
        FormCompleted,
        Completed
    };

    /// <summary>All exact status values accepted by the current shared contract.</summary>
    public static IReadOnlySet<string> ValidStatuses => Statuses;

    /// <summary>Returns whether <paramref name="status"/> is one of the six contract values.</summary>
    public static bool IsValid(string? status) => status is not null && Statuses.Contains(status);

    /// <summary>
    /// Checks the approved interim manual workflow. FORM_COMPLETED is deliberately
    /// excluded as a manual target because only verified questionnaire completion
    /// may move a case into that state.
    /// </summary>
    public static bool CanManuallyTransition(string? currentStatus, string? targetStatus)
    {
        if (!IsValid(currentStatus) || !IsValid(targetStatus)) return false;

        return (currentStatus, targetStatus) switch
        {
            (New, ToContact) => true,
            (ToContact, Appointed) => true,
            (Appointed, WaitingForm) => true,
            (FormCompleted, Completed) => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks the only status transition reserved for a future questionnaire
    /// completion workflow. This method does not inspect tasks or rounds.
    /// Its caller must first establish that the applicable required forms are complete.
    /// </summary>
    public static bool CanTransitionFromQuestionnaireCompletion(string? currentStatus, string? targetStatus) =>
        currentStatus == WaitingForm && targetStatus == FormCompleted;
}
