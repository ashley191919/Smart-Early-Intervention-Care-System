using System.ComponentModel.DataAnnotations;
using System.Reflection;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using EarlyInterventionCare.Api.Development;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

// Offline business-rule tests. No database provider, secrets or server startup.
var store = new FakeStore();
var service = new QuestionnaireService(store, new FixedClock());
AssignQuestionnaireRequest Input(Guid? version = null, string role = "TEACHER", int? round = 1, bool? required = true) =>
    new() { QuestionnaireVersionId = version ?? store.Version.QuestionnaireVersionId,
        RespondentRole = role, AssignmentRound = round, IsRequired = required };
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Error(Func<Task> action, string code, int status)
{
    try { await action(); }
    catch (QuestionnaireOperationException ex) { Check(ex.Code == code && ex.Status == status, code); return; }
    throw new Exception("Expected " + code);
}

Check(typeof(QuestionnairesController).GetCustomAttribute<AuthorizeAttribute>() is not null, "JWT required");
var caseGuard = typeof(QuestionnairesController).GetMethod("CaseAllowed", BindingFlags.NonPublic | BindingFlags.Instance)!;
var developmentController = new QuestionnairesController(service, null!, new TestEnvironment("Development"));
var productionController = new QuestionnairesController(service, null!, new TestEnvironment("Production"));
Check((bool)caseGuard.Invoke(developmentController, new object[] { CoreDevelopmentSeed.CaseId })!, "development fixture allowed");
Check(!(bool)caseGuard.Invoke(developmentController, new object[] { Guid.NewGuid() })!, "other cases fail closed");
Check(!(bool)caseGuard.Invoke(productionController, new object[] { CoreDevelopmentSeed.CaseId })!, "production case operations fail closed");
var list = await service.ListAsync();
Check(list.Count == 1 && list[0].Versions.Count == 1, "catalog versions");
Check((await service.ListTasksAsync(store.CaseId)).Count == 0, "empty existing case list");
await Error(async () => { await service.ListTasksAsync(Guid.NewGuid()); }, "CASE_NOT_FOUND", 404);
await Error(async () => { await service.AssignAsync(Guid.Empty, Input()); }, "INVALID_INPUT", 400);
await Error(async () => { await service.AssignAsync(Guid.NewGuid(), Input()); }, "CASE_NOT_FOUND", 404);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(Guid.Empty)); }, "INVALID_INPUT", 400);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(Guid.NewGuid())); }, "VERSION_NOT_FOUND", 404);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(role: "PARENT")); }, "ROLE_MISMATCH", 400);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(role: "DOCTOR")); }, "INVALID_INPUT", 400);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(round: 0)); }, "INVALID_INPUT", 400);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(round: null)); }, "INVALID_INPUT", 400);
await Error(async () => { await service.AssignAsync(store.CaseId, Input(required: null)); }, "INVALID_INPUT", 400);
foreach (var status in new[] { "DRAFT", "RETIRED" })
{
    store.Version.VersionStatus = status;
    await Error(async () => { await service.AssignAsync(store.CaseId, Input()); }, "VERSION_NOT_PUBLISHED", 409);
}
store.Version.VersionStatus = "PUBLISHED";
Check(store.Tasks.Count == 0, "invalid assignments never persisted");
var request = Input(required: false);
Check(Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true), "false isRequired valid");
var taskId = await service.AssignAsync(store.CaseId, request);
var saved = store.Tasks.Single();
Check(taskId != Guid.Empty && saved.TaskId == taskId && saved.TaskStatus == "PENDING", "new UUID and PENDING");
Check(saved.QuestionnaireId == store.Version.QuestionnaireId && !saved.IsRequired, "derive questionnaire, preserve optional");
Check(saved.CreatedAtUtc == FixedClock.Now.UtcDateTime && saved.UpdatedAtUtc == saved.CreatedAtUtc &&
    saved.CreatedAtUtc.Kind == DateTimeKind.Utc && saved.SubmittedAtUtc is null, "UTC timestamps");
await Error(async () => { await service.AssignAsync(store.CaseId, Input()); }, "ASSIGNMENT_EXISTS", 409);
store.Version.QuestionnaireVersionId = Guid.NewGuid();
await Error(async () => { await service.AssignAsync(store.CaseId, Input()); }, "ASSIGNMENT_EXISTS", 409);
saved.TaskStatus = "SUBMITTED";
await Error(async () => { await service.AssignAsync(store.CaseId, Input()); }, "ASSIGNMENT_EXISTS", 409);
Check(saved.TaskStatus == "SUBMITTED", "never reopen submitted task");
store.RejectInsert = true;
await Error(async () => { await service.AssignAsync(store.CaseId, Input(round: 2)); }, "ASSIGNMENT_EXISTS", 409);
store.RejectInsert = false;
await service.AssignAsync(store.CaseId, Input(round: 2));
store.Version.RespondentRole = "PARENT";
await service.AssignAsync(store.CaseId, Input(role: "PARENT"));
Check((await service.ListTasksAsync(store.CaseId)).Count == 3, "round/role separation");
Check(store.Case.CaseStatus == "WAITING_FORM", "case status unchanged");
Console.WriteLine("PASS: offline Phase 2-1 assignment validation, PENDING/UTC, duplicate/version/round/role rules, concurrent-duplicate result, listing and unchanged Case Status.");

sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
sealed class TestEnvironment(string name) : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "Phase21Tests";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class FakeStore : IQuestionnaireStore
{
    public Guid CaseId => Case.CaseId;
    public CaseRecord Case { get; } = new() { CaseId = Guid.NewGuid(), CaseStatus = "WAITING_FORM" };
    public QuestionnaireVersion Version { get; } = new() { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = Guid.NewGuid(),
        VersionStatus = "PUBLISHED", RespondentRole = "TEACHER", VersionNumber = "1.0.0" };
    public List<QuestionnaireTask> Tasks { get; } = new();
    public bool RejectInsert { get; set; }
    public Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<QuestionnaireSummary>>(
        new[] { new QuestionnaireSummary(Version.QuestionnaireId, "SNAP_IV", "SNAP-IV", new[] {
            new QuestionnaireVersionSummary(Version.QuestionnaireVersionId, Version.RespondentRole, Version.VersionNumber, Version.VersionStatus) }) });
    public Task<bool> CaseExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(id == CaseId);
    public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) => Task.FromResult(id == Version.QuestionnaireVersionId ? Version : null);
    public Task<bool> AssignmentExistsAsync(Guid id, Guid questionnaire, string role, uint round, CancellationToken ct) => Task.FromResult(
        Tasks.Any(t => t.CaseId == id && t.QuestionnaireId == questionnaire && t.RespondentRole == role && t.AssignmentRound == round));
    public Task<bool> InsertAsync(QuestionnaireTask task, CancellationToken ct)
    {
        if (RejectInsert) return Task.FromResult(false);
        Tasks.Add(task); return Task.FromResult(true);
    }
    public Task<IReadOnlyList<CaseTaskResponse>> ListTasksAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<CaseTaskResponse>>(
        Tasks.Where(t => t.CaseId == id).Select(t => new CaseTaskResponse(t.TaskId, t.CaseId, t.QuestionnaireId,
            t.QuestionnaireVersionId, "SNAP-IV", "1.0.0", t.RespondentRole, t.AssignmentRound, t.IsRequired,
            t.TaskStatus, t.CreatedAtUtc, t.UpdatedAtUtc, t.SubmittedAtUtc)).ToArray());
}
