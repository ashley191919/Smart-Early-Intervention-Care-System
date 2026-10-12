using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Services.Cases;

// Offline history-policy tests: no server startup, database provider, secrets, or network connection.
var checks = 0;
void Check(bool result, string message)
{
    checks++;
    if (!result) throw new Exception(message);
}

void Error(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    throw new Exception(message);
}

var clock = new FixedClock();
var caseId = Guid.NewGuid();
var userId = Guid.NewGuid();
var sources = new[]
{
    CaseStatusHistoryPolicy.Manual,
    CaseStatusHistoryPolicy.QuestionnaireCompletion,
    CaseStatusHistoryPolicy.SystemWorkflow
};

Check(sources.Distinct(StringComparer.Ordinal).Count() == 3, "three distinct trigger-source constants");
Check(CaseStatusHistoryPolicy.ValidTriggerSources.SetEquals(sources), "trigger-source set matches constants");
foreach (var source in sources)
    Check(CaseStatusHistoryPolicy.IsValidTriggerSource(source), "known trigger source is valid");
foreach (var invalid in new string?[] { null, "", "manual", "UNKNOWN" })
    Check(!CaseStatusHistoryPolicy.IsValidTriggerSource(invalid), "unknown trigger source is rejected");

Check(CaseStatusHistoryPolicy.IsValidStatusChange(CaseStatusPolicy.New, CaseStatusPolicy.ToContact),
    "distinct legal statuses are a valid history change");
Check(!CaseStatusHistoryPolicy.IsValidStatusChange(CaseStatusPolicy.New, CaseStatusPolicy.New),
    "same status is rejected");
Check(!CaseStatusHistoryPolicy.IsValidStatusChange("UNKNOWN", CaseStatusPolicy.ToContact),
    "unknown old status is rejected");
Check(!CaseStatusHistoryPolicy.IsValidStatusChange(CaseStatusPolicy.New, "UNKNOWN"),
    "unknown new status is rejected");

Check(CaseStatusHistoryPolicy.RequiresChangedByUserId(CaseStatusHistoryPolicy.Manual),
    "manual events require a user");
Check(!CaseStatusHistoryPolicy.RequiresChangedByUserId(CaseStatusHistoryPolicy.QuestionnaireCompletion),
    "questionnaire event may omit a user");
Check(!CaseStatusHistoryPolicy.RequiresChangedByUserId(CaseStatusHistoryPolicy.SystemWorkflow),
    "system event may omit a user");

var manual = CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.New, CaseStatusPolicy.ToContact,
    CaseStatusHistoryPolicy.Manual, userId, clock);
Check(manual.HistoryId != Guid.Empty && manual.CaseId == caseId && manual.ChangedByUserId == userId,
    "manual entry has generated history id and actor");
Check(manual.OldStatus == CaseStatusPolicy.New && manual.NewStatus == CaseStatusPolicy.ToContact &&
    manual.TriggerSource == CaseStatusHistoryPolicy.Manual, "manual entry preserves status values and source");
Check(manual.ChangedAtUtc == FixedClock.Now.UtcDateTime && manual.ChangedAtUtc.Kind == DateTimeKind.Utc,
    "history timestamp is UTC");

var questionnaire = CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.WaitingForm, CaseStatusPolicy.FormCompleted,
    CaseStatusHistoryPolicy.QuestionnaireCompletion, null, clock);
var system = CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.ToContact, CaseStatusPolicy.Appointed,
    CaseStatusHistoryPolicy.SystemWorkflow, null, clock);
Check(questionnaire.ChangedByUserId is null && system.ChangedByUserId is null,
    "system-originated entries may omit a user");

Error(() => CaseStatusHistoryPolicy.Create(Guid.Empty, CaseStatusPolicy.New, CaseStatusPolicy.ToContact,
    CaseStatusHistoryPolicy.Manual, userId, clock), "empty case id must be rejected");
Error(() => CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.New, CaseStatusPolicy.New,
    CaseStatusHistoryPolicy.Manual, userId, clock), "same statuses must be rejected");
Error(() => CaseStatusHistoryPolicy.Create(caseId, "UNKNOWN", CaseStatusPolicy.ToContact,
    CaseStatusHistoryPolicy.Manual, userId, clock), "unknown old status must be rejected");
Error(() => CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.New, "UNKNOWN",
    CaseStatusHistoryPolicy.Manual, userId, clock), "unknown new status must be rejected");
Error(() => CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.New, CaseStatusPolicy.ToContact,
    "UNKNOWN", userId, clock), "unknown trigger source must be rejected");
Error(() => CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.New, CaseStatusPolicy.ToContact,
    CaseStatusHistoryPolicy.Manual, null, clock), "manual event requires a user");
Error(() => CaseStatusHistoryPolicy.Create(caseId, CaseStatusPolicy.New, CaseStatusPolicy.ToContact,
    CaseStatusHistoryPolicy.Manual, Guid.Empty, clock), "manual event rejects empty user id");

// EF model metadata check only: the design-time factory blocks all database connections.
await using (var db = new ApplicationDbContextFactory().CreateDbContext([]))
{
    var history = db.Model.FindEntityType(typeof(CaseStatusHistory));
    Check(history is not null, "history entity is included in the EF model");
    Check(history!.FindPrimaryKey()!.Properties.Single().Name == nameof(CaseStatusHistory.HistoryId),
        "history id is the primary key");
    Check(history.FindProperty(nameof(CaseStatusHistory.CaseId))!.IsNullable == false,
        "case id is required");
    Check(history.FindProperty(nameof(CaseStatusHistory.ChangedByUserId))!.IsNullable,
        "changed-by user is nullable for system events");
    Check(history.GetForeignKeys().Any(f => f.PrincipalEntityType.ClrType == typeof(CaseRecord) &&
        f.Properties.Single().Name == nameof(CaseStatusHistory.CaseId)), "case foreign key exists");
    Check(history.GetForeignKeys().Any(f => f.PrincipalEntityType.ClrType == typeof(EarlyInterventionCare.Api.Models.Authentication.User) &&
        f.Properties.Single().Name == nameof(CaseStatusHistory.ChangedByUserId)), "user foreign key exists");
}

Console.WriteLine($"PASS: {checks} offline CaseStatusHistoryPolicy checks; validation, actor requirements, UTC entries and immutable-in-memory data.");

sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
