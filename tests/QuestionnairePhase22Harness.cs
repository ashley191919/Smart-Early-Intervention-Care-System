using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

// No server startup, secrets, sockets or database connection. Transactions are a fake store.
var checks = 0;
void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
async Task Error(Func<Task> action, string code, int status)
{
    try { await action(); }
    catch (QuestionnaireWorkflowException ex) { Check(ex.Code == code && ex.Status == status, code); return; }
    throw new Exception("Expected " + code);
}
(FakeStore Store, TestClock Clock, InMemoryAuditLogService Audit, TeacherQuestionnaireWorkflowService Service) Fixture()
{
    var store = new FakeStore(); var clock = new TestClock(); var audit = new InMemoryAuditLogService(clock);
    return (store, clock, audit, new(store, new(), clock, audit));
}
QuestionnaireAnswer[] Answers() => Enumerable.Range(1, 3).Select(n => new QuestionnaireAnswer($"Q{n}", "1")).ToArray();
QuestionnaireSubmission Submit(FakeStore store) => new(store.VersionId, "測試填表人", "2026-10-09", Answers(), "測試觀察");
SaveQuestionnaireDraftRequest Draft(FakeStore store, ulong? revision, QuestionnaireAnswer?[]? answers = null) =>
    new(store.VersionId, revision, null, null, answers, null);

// Validate compatibility with the old canonical hash: preserves receipt retries after refactoring.
{
    var f = Fixture(); var input = Submit(f.Store);
    var valid = new QuestionnaireAnswerValidator().Validate(f.Store.State.Versions[0].DefinitionSnapshot, input,
        new DateOnly(2026, 10, 9), true);
    var oldAnswers = input.Answers!.OrderBy(a => a!.QuestionId, StringComparer.Ordinal)
        .Select(a => new { questionId = a!.QuestionId!, optionValue = a.OptionValue! }).ToArray();
    var oldHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { taskId = f.Store.TaskId,
        questionnaireVersionId = input.QuestionnaireVersionId, respondentName = input.RespondentName,
        filledOn = input.FilledOn, answers = oldAnswers, observation = input.Observation }));
    Check(oldHash.SequenceEqual(valid.SubmissionHash(f.Store.TaskId, f.Store.VersionId)), "legacy payload hash compatibility");
}

// Empty/partial drafts, revision conflicts, reload, and no grant consumption.
{
    // The existing development SNAP definition has 26 questions and four options.
    var sourcePath = Path.Combine(Environment.CurrentDirectory, "EarlyInterventionCare.Api", "Development", "SnapQuestionnaire.json");
    using var source = JsonDocument.Parse(File.ReadAllText(sourcePath));
    var count = source.RootElement.GetProperty("questions").GetArrayLength();
    var options = source.RootElement.GetProperty("options").GetArrayLength();
    var snapshot = JsonSerializer.Serialize(new { questions = Enumerable.Range(1, count).Select(n => new {
        questionId = $"SNAP_IV_Q{n:00}", options = Enumerable.Range(0, options).Select(v => new { value = v.ToString() }) }) });
    var answers = Enumerable.Range(1, count).Select(n => new QuestionnaireAnswer($"SNAP_IV_Q{n:00}", "1")).ToArray();
    var validator = new QuestionnaireAnswerValidator();
    var input = new QuestionnaireSubmission(Guid.NewGuid(), "測試", "2026-10-09", answers, null);
    Check(count == 26 && options == 4 && validator.Validate(snapshot, input, new(2026, 10, 9), true).Answers.Length == 26,
        "26-question development SNAP full validation");
    Check(validator.Validate(snapshot, input with { Answers = answers.Take(2).ToArray() }, new(2026, 10, 9), false).Answers.Length == 2,
        "same validator accepts partial SNAP draft");
}

// Empty/partial drafts, revision conflicts, reload, and no grant consumption.
{
    var f = Fixture(); var s = f.Store;
    var empty = await f.Service.GetDraftAsync(FakeStore.Token, s.TaskId);
    Check(empty.Revision == 0 && empty.Answers.Count == 0 && empty.UpdatedAtUtc is null, "absent draft revision 0");
    var first = await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 0), "draft");
    Check(first.Revision == 1 && first.TaskStatus == "IN_PROGRESS", "first save changes task status");
    Check(s.State.Grant.GrantStatus == "ACTIVE" && s.State.Responses.Count == 0, "draft does not consume grant or submit");
    var partial = await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 1, new[] { Answers()[0] }), "draft");
    Check(partial.Revision == 2 && partial.Answers.Count == 1 && partial.RespondentName is null, "partial answers optional identity");
    await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 1), "stale"); }, "DRAFT_CONFLICT", 409);
    var recreatedService = new TeacherQuestionnaireWorkflowService(s, new(), f.Clock, f.Audit);
    var reloaded = await recreatedService.GetDraftAsync(FakeStore.Token, s.TaskId);
    Check(reloaded.Revision == 2 && reloaded.Answers.Count == 1, "new service reads persisted fake-store draft");
    Check(reloaded.UpdatedAtUtc!.Value.Kind == DateTimeKind.Utc, "UTC draft timestamp");
    Check(s.State.Drafts.Single().LastSavedByGrantId == s.State.Grant.GrantId, "persist grant provenance");
    await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, null), "invalid"); }, "INVALID_INPUT", 400);
    await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 2) with { QuestionnaireVersionId = Guid.NewGuid() }, "invalid"); }, "VERSION_MISMATCH", 409);
    foreach (var invalid in new QuestionnaireAnswer?[][] {
        new[] { Answers()[0], Answers()[0] }, new[] { new QuestionnaireAnswer("UNKNOWN", "1") },
        new[] { new QuestionnaireAnswer("Q1", "99") }, new QuestionnaireAnswer?[] { null }, new[] { new QuestionnaireAnswer(null, "1") } })
        await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 2, invalid), "invalid"); }, "INVALID_ANSWERS", 400);
    foreach (var invalid in new[] { Draft(s, 2) with { FilledOn = "2026-10-10" },
        Draft(s, 2) with { FilledOn = "invalid" }, Draft(s, 2) with { RespondentName = new string('a', 51) },
        Draft(s, 2) with { Observation = new string('a', 1001) } })
        await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, invalid, "invalid"); }, "INVALID_INPUT", 400);
    Check(s.State.Drafts.Single().Revision == 2 && f.Audit.Query().Count == 2, "conflicts/invalid content never overwrite or log success");
    // New grant for same unsubmitted task keeps the existing draft and revision.
    var grantId = Guid.NewGuid(); s.State.Grant.GrantId = grantId;
    s.State.Sessions[0].GrantId = grantId; s.State.Links[0].GrantId = grantId;
    Check((await f.Service.GetDraftAsync(FakeStore.Token, s.TaskId)).Revision == 2, "regrant reads same task draft");
}

// Scope/session guards. No raw response API and no role-only bypass.
foreach (var mutation in new Action<FakeStore>[] {
    s => s.State.Grant.IsDevelopment = false, s => s.State.Grant.CaseId = Guid.NewGuid(),
    s => s.State.Grant.GrantStatus = "REVOKED", s => s.State.Sessions[0].SessionStatus = "REVOKED",
    s => s.State.Sessions[0].SessionExpiresAtUtc = TestClock.Initial.UtcDateTime })
{
    var f = Fixture(); mutation(f.Store);
    await Error(async () => { await f.Service.GetDraftAsync(FakeStore.Token, f.Store.TaskId); }, "GRANT_UNAVAILABLE", 401);
    Check(f.Store.State.Drafts.Count == 0, "denied access does not write");
}
{
    var f = Fixture(); var s = f.Store;
    await Error(async () => { await f.Service.GetDraftAsync(null, s.TaskId); }, "SESSION_REQUIRED", 401);
    await Error(async () => { await f.Service.GetDraftAsync(new string('B', 64), s.TaskId); }, "SESSION_REQUIRED", 401);
    await Error(async () => { await f.Service.GetDraftAsync(FakeStore.Token, Guid.NewGuid()); }, "TASK_NOT_FOUND", 404);
    s.State.Tasks[0].RespondentRole = "PARENT";
    await Error(async () => { await f.Service.GetDraftAsync(FakeStore.Token, s.TaskId); }, "TASK_NOT_FOUND", 404);
}

// Submit, immutability, response receipts, legacy replay and final grant/session transition.
{
    var f = Fixture(); var s = f.Store; var key = Guid.NewGuid(); var input = Submit(s);
    await Error(async () => { await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, input with { Answers = new[] { Answers()[0] } }, "invalid"); }, "INVALID_ANSWERS", 400);
    await Error(async () => { await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, input with { RespondentName = null }, "invalid"); }, "INVALID_INPUT", 400);
    Check(s.State.Responses.Count == 0 && s.State.Tasks[0].TaskStatus == "PENDING", "incomplete submit has no side effects");
    await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 0, new[] { Answers()[0] }), "draft");
    var receipt = await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, input, "submit");
    Check(!receipt.Replayed && s.State.Responses.Count == 1 && s.State.Tasks[0].TaskStatus == "SUBMITTED", "one response and submitted task");
    Check(s.State.Grant.GrantStatus == "USED" && s.State.Sessions.All(x => x.SessionStatus == "REVOKED"), "grant used and sessions revoked");
    Check(s.State.Sessions[0].ReceiptExpiresAtUtc == TestClock.Initial.UtcDateTime.AddMinutes(15), "15 minute receipt window");
    var replay = await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key,
        input with { Answers = Answers().Reverse().ToArray(), RespondentName = " 測試填表人 ", Observation = " 測試觀察 " }, "retry");
    Check(replay.Replayed && replay.ResponseId == receipt.ResponseId && s.State.Responses.Count == 1, "canonical receipt retry");
    Check(f.Audit.Query().Count(e => e.Action == "TeacherWorkspace.Submit") == 1, "retry no duplicate audit");
    var queried = await f.Service.GetReceiptAsync(FakeStore.Token, s.TaskId);
    Check(queried.ResponseId == receipt.ResponseId, "same-scope receipt query");
    Check(!JsonSerializer.Serialize(queried).Contains("測試填表人") && !JsonSerializer.Serialize(queried).Contains("測試觀察"), "no teacher answers or identity in receipt");
    await Error(async () => { await f.Service.GetDraftAsync(FakeStore.Token, s.TaskId); }, "GRANT_UNAVAILABLE", 401);
    await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 1), "denied"); }, "GRANT_UNAVAILABLE", 401);
    await Error(async () => { await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, input with { Observation = "different" }, "conflict"); }, "IDEMPOTENCY_CONFLICT", 409);
    await Error(async () => { await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, Guid.NewGuid(), input, "new"); }, "GRANT_UNAVAILABLE", 401);
    s.State.Sessions[0].ReceiptExpiresAtUtc = null; // Logout clears receipts.
    await Error(async () => { await f.Service.GetReceiptAsync(FakeStore.Token, s.TaskId); }, "GRANT_UNAVAILABLE", 401);
    s.State.Sessions[0].ReceiptExpiresAtUtc = TestClock.Initial.UtcDateTime.AddMinutes(15);
    f.Clock.Time = TestClock.Initial.AddMinutes(16);
    await Error(async () => { await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, input, "expired"); }, "GRANT_UNAVAILABLE", 401);
    Check(s.State.Drafts.Single().Revision == 1 && s.State.CaseStatus == "WAITING_FORM", "no post-submit draft/case mutation");
}

// Multiple tasks: first submission doesn't consume the grant or block another draft.
{
    var f = Fixture(); var s = f.Store; var second = s.AddTask();
    await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, Guid.NewGuid(), Submit(s), "first");
    Check(s.State.Grant.GrantStatus == "ACTIVE", "partial grant completion");
    await Error(async () => { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 0), "locked"); }, "TASK_SUBMITTED", 409);
    await f.Service.SaveDraftAsync(FakeStore.Token, second, Draft(s, 0), "second-draft");
    Check((await f.Service.GetDraftAsync(FakeStore.Token, second)).Revision == 1, "continue second task");
    await Error(async () => { await f.Service.GetReceiptAsync(FakeStore.Token, second); }, "RESPONSE_NOT_FOUND", 404);
    await f.Service.SubmitAsync(FakeStore.Token, second, Guid.NewGuid(), Submit(s), "second");
    Check(s.State.Grant.GrantStatus == "USED" && s.State.Responses.Count == 2, "all submitted uses grant");
}

// Receipt cannot cross grants and must not outlive its original session.
{
    var f = Fixture(); var s = f.Store;
    s.State.Sessions[0].SessionExpiresAtUtc = TestClock.Initial.UtcDateTime.AddMinutes(5);
    await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, Guid.NewGuid(), Submit(s), "submit");
    Check(s.State.Sessions[0].ReceiptExpiresAtUtc == s.State.Sessions[0].SessionExpiresAtUtc, "receipt bounded by original session");
    f.Clock.Time = TestClock.Initial.AddMinutes(5);
    await Error(async () => { await f.Service.GetReceiptAsync(FakeStore.Token, s.TaskId); }, "GRANT_UNAVAILABLE", 401);
    f.Clock.Time = TestClock.Initial;
    s.State.Grant.GrantId = Guid.NewGuid(); s.State.Grant.GrantStatus = "ACTIVE";
    s.State.Sessions[0].GrantId = s.State.Grant.GrantId; s.State.Sessions[0].SessionStatus = "ACTIVE";
    s.State.Links[0].GrantId = s.State.Grant.GrantId;
    await Error(async () => { await f.Service.GetReceiptAsync(FakeStore.Token, s.TaskId); }, "RESPONSE_NOT_FOUND", 404);
}

// Commit failure rollback; concurrency on first save and duplicate submit (fake serialized transactions).
{
    var f = Fixture(); var s = f.Store; s.FailCommit = true;
    try { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 0), "failure"); throw new Exception("failure expected"); }
    catch (IOException) { Check(s.State.Drafts.Count == 0 && s.State.Tasks[0].TaskStatus == "PENDING", "failed draft rollback"); }
    s.FailCommit = true;
    try { await f.Service.SubmitAsync(FakeStore.Token, s.TaskId, Guid.NewGuid(), Submit(s), "failure"); throw new Exception("failure expected"); }
    catch (IOException) { Check(s.State.Responses.Count == 0 && s.State.Grant.GrantStatus == "ACTIVE", "failed submit rollback"); }
    Check(f.Audit.Query().Count == 0, "no success audit before commit");
    async Task<int> Save() { try { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 0), "parallel"); return 200; }
        catch (QuestionnaireWorkflowException e) { return e.Status; } }
    var saves = await Task.WhenAll(Save(), Save());
    Check(saves.Order().SequenceEqual(new[] { 200, 409 }) && s.State.Drafts.Single().Revision == 1, "one concurrent first save wins");
    var key = Guid.NewGuid();
    var receipts = await Task.WhenAll(f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, Submit(s), "parallel"),
        f.Service.SubmitAsync(FakeStore.Token, s.TaskId, key, Submit(s), "parallel"));
    Check(receipts.Select(r => r.ResponseId).Distinct().Count() == 1 && receipts.Count(r => !r.Replayed) == 1, "concurrent identical submit one receipt");
}

// An overlapping save/submit may save before submit, but never overwrite after submission.
{
    var f = Fixture(); var s = f.Store;
    async Task<int> Save() { try { await f.Service.SaveDraftAsync(FakeStore.Token, s.TaskId, Draft(s, 0), "overlap"); return 200; }
        catch (QuestionnaireWorkflowException e) { return e.Status; } }
    var submission = f.Service.SubmitAsync(FakeStore.Token, s.TaskId, Guid.NewGuid(), Submit(s), "overlap");
    var save = Save();
    await submission;
    Check(await save == 401 && s.State.Drafts.Count == 0 && s.State.Tasks[0].TaskStatus == "SUBMITTED", "submit-first blocks overlapping draft");
}

// EF metadata and pending model check are local operations; factory prevents any DB connection.
{
    var f = Fixture();
    // Exercise controller guards without starting a host or creating a DB context.
    var adapter = new TeacherWorkspaceService(null!, f.Audit, f.Clock, f.Service);
    var http = new DefaultHttpContext(); http.Request.Scheme = "https"; http.Request.Host = new("localhost");
    http.Request.Headers.Cookie = "EarlyCare.DevTeacher=" + FakeStore.Token;
    var dev = new DevelopmentTeacherWorkspaceController(new TestEnvironment("Development"), adapter)
        { ControllerContext = new() { HttpContext = http } };
    var rejected = (ObjectResult)await dev.SaveDraft(f.Store.TaskId, Draft(f.Store, 0), default);
    Check(rejected.StatusCode == 403 && f.Store.State.Drafts.Count == 0, "missing write header rejected before saving");
    http.Request.Headers["X-Teacher-Draft"] = "1"; http.Request.Headers.Origin = "https://untrusted.example";
    rejected = (ObjectResult)await dev.SaveDraft(f.Store.TaskId, Draft(f.Store, 0), default);
    Check(rejected.StatusCode == 403 && f.Store.State.Drafts.Count == 0, "cross-origin draft rejected");
    http.Request.Headers.Origin = "https://localhost";
    Check(await dev.SaveDraft(f.Store.TaskId, Draft(f.Store, 0), default) is OkObjectResult, "same-origin cookie draft accepted");
    Check(await dev.GetDraft(f.Store.TaskId, default) is OkObjectResult, "controller reloads saved draft");
    var prod = new DevelopmentTeacherWorkspaceController(new TestEnvironment("Production"), adapter)
        { ControllerContext = new() { HttpContext = http } };
    Check(await prod.GetDraft(f.Store.TaskId, default) is NotFoundResult &&
        await prod.SaveDraft(f.Store.TaskId, Draft(f.Store, 1), default) is NotFoundResult &&
        await prod.Receipt(f.Store.TaskId, default) is NotFoundResult, "all new endpoints hidden outside Development");
}

// EF metadata and pending model check are local operations; factory prevents any DB connection.
await using (var db = new ApplicationDbContextFactory().CreateDbContext([]))
{
    Check(!db.Database.HasPendingModelChanges(), "no pending schema/model changes");
    Check(db.Model.FindEntityType(typeof(QuestionnaireDraft))!.FindProperty("Revision")!.IsConcurrencyToken, "existing revision mapping preserved");
}
Console.WriteLine($"PASS: {checks} offline Phase 2-2 checks; draft/revision, scope, shared validation/hash, submit/receipt/idempotency, rollback and unchanged EF model.");

sealed class TestClock : TimeProvider
{
    public static readonly DateTimeOffset Initial = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Time { get; set; } = Initial;
    public override DateTimeOffset GetUtcNow() => Time;
}
sealed class TestEnvironment(string name) : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "Phase22Tests";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class FakeState
{
    public TeacherGrant Grant { get; set; } = null!;
    public List<TeacherSession> Sessions { get; set; } = new();
    public List<QuestionnaireTask> Tasks { get; set; } = new();
    public List<TeacherGrantTask> Links { get; set; } = new();
    public List<QuestionnaireVersion> Versions { get; set; } = new();
    public List<QuestionnaireDraft> Drafts { get; set; } = new();
    public List<QuestionnaireResponse> Responses { get; set; } = new();
    public string CaseStatus { get; set; } = "WAITING_FORM";
    public FakeState Clone() => JsonSerializer.Deserialize<FakeState>(JsonSerializer.SerializeToUtf8Bytes(this))!;
}
sealed class FakeStore : ITeacherQuestionnaireStore
{
    public const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly SemaphoreSlim gate = new(1);
    public FakeState State { get; set; }
    public bool FailCommit { get; set; }
    public Guid TaskId => State.Tasks[0].TaskId;
    public Guid VersionId => State.Versions[0].QuestionnaireVersionId;
    public FakeStore()
    {
        var grantId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var questionnaireId = Guid.NewGuid(); var now = TestClock.Initial.UtcDateTime;
        State = new() {
            Grant = new() { GrantId = grantId, CaseId = CoreDevelopmentSeed.CaseId, RespondentRole = "TEACHER", IsDevelopment = true, GrantStatus = "ACTIVE", CodeHash = new byte[32], CreatedAtUtc = now },
            Sessions = new() { new() { SessionHash = SHA256.HashData(Encoding.ASCII.GetBytes(Token)), GrantId = grantId, SessionStatus = "ACTIVE", SessionExpiresAtUtc = now.AddHours(8), CreatedAtUtc = now } },
            Versions = new() { new() { QuestionnaireVersionId = versionId, QuestionnaireId = questionnaireId,
                RespondentRole = "TEACHER", VersionNumber = "1.0.0", VersionStatus = "PUBLISHED",
                DefinitionSnapshot = JsonSerializer.Serialize(new { questions = Enumerable.Range(1, 3).Select(n => new {
                    questionId = $"Q{n}", options = Enumerable.Range(0, 4).Select(v => new { value = v.ToString() }) }) }) } }
        };
        AddTask();
    }
    public Guid AddTask()
    {
        var taskId = Guid.NewGuid(); State.Tasks.Add(new() { TaskId = taskId, CaseId = CoreDevelopmentSeed.CaseId,
            QuestionnaireId = State.Versions[0].QuestionnaireId, QuestionnaireVersionId = VersionId, RespondentRole = "TEACHER",
            AssignmentRound = (uint)State.Tasks.Count + 1, IsRequired = true, TaskStatus = "PENDING" });
        State.Links.Add(new() { TaskId = taskId, GrantId = State.Grant.GrantId, CaseId = CoreDevelopmentSeed.CaseId, RespondentRole = "TEACHER" });
        return taskId;
    }
    public async Task<ITeacherQuestionnaireTransaction?> OpenAsync(byte[] hash, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        await Task.Yield(); // Ensure concurrent calls overlap rather than complete synchronously.
        var working = State.Clone();
        var session = working.Sessions.SingleOrDefault(s => s.SessionHash.SequenceEqual(hash));
        if (session is null) { gate.Release(); return null; }
        return new Transaction(this, working, session);
    }
    private sealed class Transaction(FakeStore owner, FakeState working, TeacherSession session) : ITeacherQuestionnaireTransaction
    {
        public TeacherGrant? Grant => working.Grant;
        public TeacherSession? Session => session;
        public IReadOnlyList<TeacherTaskEntry> Tasks => working.Links.OrderBy(l => l.TaskId)
            .Select(l => new TeacherTaskEntry(l, working.Tasks.SingleOrDefault(t => t.TaskId == l.TaskId))).ToArray();
        public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) => Task.FromResult(working.Versions.SingleOrDefault(v => v.QuestionnaireVersionId == id));
        public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct) => Task.FromResult(working.Drafts.SingleOrDefault(d => d.TaskId == id));
        public Task<bool> HasResponseAsync(Guid id, CancellationToken ct) => Task.FromResult(working.Responses.Any(r => r.TaskId == id));
        public Task<QuestionnaireResponse?> FindByKeyAsync(Guid id, Guid key, CancellationToken ct) => Task.FromResult(working.Responses.SingleOrDefault(r => r.SubmittedByGrantId == id && r.IdempotencyKey == key));
        public Task<QuestionnaireReceipt?> FindReceiptAsync(Guid id, Guid taskId, CancellationToken ct)
        {
            var r = working.Responses.SingleOrDefault(r => r.SubmittedByGrantId == id && r.TaskId == taskId);
            return Task.FromResult(r is null ? null : new QuestionnaireReceipt(r.ResponseId, r.TaskId, r.QuestionnaireVersionId, r.SubmittedAtUtc, working.Grant.GrantStatus, false));
        }
        public void AddDraft(QuestionnaireDraft draft) => working.Drafts.Add(draft);
        public void AddResponse(QuestionnaireResponse response) => working.Responses.Add(response);
        public Task RevokeSessionsAsync(DateTime now, CancellationToken ct)
        { foreach (var s in working.Sessions.Where(s => s.SessionStatus == "ACTIVE")) { s.SessionStatus = "REVOKED"; s.RevokedAtUtc = now; } return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct)
        {
            if (owner.FailCommit) { owner.FailCommit = false; throw new IOException("Synthetic commit failure."); }
            owner.State = working.Clone(); return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { owner.gate.Release(); return ValueTask.CompletedTask; }
    }
}
