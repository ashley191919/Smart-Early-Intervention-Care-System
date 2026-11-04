using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;

// Catch complete legacy suite entry-point exceptions INSIDE this console host, so a
// known schema assertion does not escape to Windows JIT debugging. Never run API Main.
if (args.Length > 0)
{
    if (args.Length != 2 || args[0] != "--regression" || args[1] is not ("teacher" or "parent"))
    { Console.WriteLine("FAIL regression usage: --regression teacher|parent"); Environment.ExitCode = 1; return; }
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var relative = args[1] == "teacher" ? "tests/bin/Debug/net8.0/QuestionnairePhase22Harness.dll"
        : "tests/ParentQuestionnaire/bin/Debug/net8.0/ParentQuestionnaireHarness.dll";
    try
    {
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, relative));
        var entry = assembly.EntryPoint ?? throw new InvalidOperationException("Missing regression entry point");
        var result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [Array.Empty<string>()]);
        if (result is Task task) await task;
        Console.WriteLine($"REGRESSION_COMPLETE {args[1]} exit={Environment.ExitCode}");
    }
    catch (Exception e)
    {
        var cause = e is TargetInvocationException { InnerException: not null } invocation ? invocation.InnerException! : e;
        Console.WriteLine($"REGRESSION_FAIL {args[1]}: {cause.GetType().Name}: {cause.Message}");
        Console.WriteLine(cause.StackTrace);
        Environment.ExitCode = 1;
    }
    return;
}

int passed = 0, failed = 0, blocked = 0;
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
async Task Check(string name, Func<Task> work)
{
    try { await work(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
}
async Task Error(Func<Task> work, string code, int status)
{
    try { await work(); throw new Exception("Expected rejection"); }
    catch (QuestionnaireWorkflowException e) { Assert(e.Code == code && e.Status == status); }
}
await Check("default command denies before storage, even with legacy allow Adapter", async () => {
    var f = new Fixture();
    await Error(() => CommandCall.Submit(f.Gateway, f.Context, f.TaskId, f.Key, f.Command()), "PARENT_ACCESS_NOT_INTEGRATED", 503);
    Assert(f.Store.Opens == 0);
});
await Check("command method and injection constructor are internal, not public DI paths", () => {
    Assert(typeof(ParentQuestionnaireGateway).GetMethod("SubmitCommandAsync") is null);
    Assert(typeof(ParentQuestionnaireGateway).GetConstructors().Length == 1); return Task.CompletedTask;
});
await Check("first submission stores canonical v1, relationship, trusted author and Task together (Fake)", async () => {
    var f = new Fixture(); var receipt = await f.SubmitCommand(); var r = f.Store.State.Response!;
    Assert(!receipt.Replayed && r.ResponseId == receipt.ResponseId && f.Store.State.Task.TaskStatus == "SUBMITTED");
    Assert(r.HashVersion == ParentCanonicalSubmissionEncoder.FormatVersion && r.RespondentRelationship == "MOTHER");
    Assert(r.PayloadHash.SequenceEqual(new ParentCanonicalSubmissionEncoder().ComputeHash(f.TaskId, f.VersionId, f.Validate(f.Payload()), "MOTHER")));
    Assert(r.ParentAuthorUserId == f.UserId && r.SubmittedByGrantId is null && r.SubmittedAtUtc == f.Store.State.Task.SubmittedAtUtc);
    Assert(f.Store.Commits == 1 && f.CommandAccess.SameTransaction && ReferenceEquals(f.Context.Principal, f.CommandAccess.Principal));
});
foreach (var revision in new ulong[] { 1, ulong.MaxValue }) await Check($"no draft rejects nonzero revision {revision}", async () => {
    var f = new Fixture(); var before = f.Store.Snapshot();
    await Error(() => f.SubmitCommand(f.Command(revision)), "DRAFT_CONFLICT", 409);
    Assert(f.Store.Snapshot() == before && f.Store.Commits == 0);
});
foreach (var revision in new ulong[] { 7, ulong.MaxValue }) await Check($"exact draft revision {revision} submits without increment/inference", async () => {
    var f = new Fixture(); f.SeedDraft(); f.Store.State.Draft!.Revision = revision;
    await f.SubmitCommand(f.Command(revision));
    Assert(f.Store.State.Draft.Revision == revision && f.Store.State.Draft.RespondentRelationship is null);
    Assert(f.Store.State.Response!.RespondentRelationship == "MOTHER");
});
await Check("stale draft revision rejects without writes", async () => {
    var f = new Fixture(); f.SeedDraft(); var before = f.Store.Snapshot();
    await Error(() => f.SubmitCommand(f.Command(6)), "DRAFT_CONFLICT", 409); Assert(f.Store.Snapshot() == before);
});
await Check("normalized replay precedes revision comparison and preserves original receipt", async () => {
    var f = new Fixture(); f.SeedDraft(); var first = await f.SubmitCommand(f.Command(7));
    f.Store.State.Draft!.Revision = 99; var before = f.Store.Snapshot();
    var retry = await f.SubmitCommand(f.Command(ulong.MaxValue) with { Payload = f.Payload(false) });
    Assert(retry.Replayed && retry.ResponseId == first.ResponseId && retry.SubmittedAtUtc == first.SubmittedAtUtc);
    Assert(f.Store.Snapshot() == before && f.Store.AddResponses == 1 && f.CommandAccess.Revalidations == 2);
});
await Check("seeded canonical v1 receipt replays with changed ExpectedRevision", async () => {
    var f = new Fixture(); f.SeedReceipt(ParentCanonicalSubmissionEncoder.FormatVersion, true);
    var r = await f.SubmitCommand(f.Command(42) with { Payload = f.Payload(false) });
    Assert(r.Replayed && r.ResponseId == f.Store.State.Response!.ResponseId && f.Store.AddResponses == 0);
});
foreach (var (name, change) in new (string, Func<ParentSubmitCommand, ParentSubmitCommand>)[] {
    ("relationship", x => x with { RespondentRelationship = "FATHER" }),
    ("name", x => x with { Payload = x.Payload with { RespondentName = "Another synthetic respondent" } }),
    ("date", x => x with { Payload = x.Payload with { FilledOn = "2026-10-29" } }),
    ("observation", x => x with { Payload = x.Payload with { Observation = "changed synthetic note" } }),
    ("answer", x => x with { Payload = x.Payload with { Answers = [new("q1", "0"), new("q2", "2")] } }),
}) await Check($"same key changed {name} rejects without rewrite", async () => {
    var f = new Fixture(); await f.SubmitCommand(); var before = f.Store.Snapshot();
    await Error(() => f.SubmitCommand(change(f.Command())), "IDEMPOTENCY_CONFLICT", 409);
    Assert(f.Store.Snapshot() == before && f.Store.Commits == 1 && f.Store.AddResponses == 1);
});
foreach (var (name, version) in new (string, string?)[] {
    ("NULL", null), ("unknown", "synthetic.unsupported/v99"),
    ("case", ParentCanonicalSubmissionEncoder.FormatVersion.ToUpperInvariant()),
    ("space", ParentCanonicalSubmissionEncoder.FormatVersion + " "),
}) await Check($"{name} HashVersion rejects with IDEMPOTENCY_VERSION_UNSUPPORTED/409", async () => {
    var f = new Fixture(); f.SeedReceipt(version, false); var before = f.Store.Snapshot();
    await Error(() => f.SubmitCommand(), "IDEMPOTENCY_VERSION_UNSUPPORTED", 409);
    Assert(f.Store.Snapshot() == before && f.Store.Commits == 0);
});
foreach (var relationship in new string?[] { null, "", "mother", " MOTHER", "SYNTHETIC_INVALID" })
    await Check($"invalid relationship ({relationship ?? "null"}) rejects first submit and replay", async () => {
        var f = new Fixture(); var command = f.Command() with { RespondentRelationship = relationship! };
        await Error(() => f.SubmitCommand(command), "INVALID_INPUT", 400); Assert(f.Store.Commits == 0 && f.Store.State.Response is null);
        f.SeedReceipt(ParentCanonicalSubmissionEncoder.FormatVersion, true);
        await Error(() => f.SubmitCommand(command), "INVALID_INPUT", 400); Assert(f.Store.Commits == 0);
    });
await Check("v1 row with unknown stored relationship cannot replay", async () => {
    var f = new Fixture(); f.SeedReceipt(ParentCanonicalSubmissionEncoder.FormatVersion, true); f.Store.State.Response!.RespondentRelationship = null;
    await Error(() => f.SubmitCommand(), "IDEMPOTENCY_CONFLICT", 409); Assert(f.Store.Commits == 0);
});
await Check("key for another task rejects before exposing version", async () => {
    var f = new Fixture(); f.SeedReceipt(null, false); f.Store.State.Response!.TaskId = Guid.NewGuid();
    f.Store.State.Task.TaskStatus = "PENDING"; f.Store.State.Task.SubmittedAtUtc = null;
    await Error(() => f.SubmitCommand(), "IDEMPOTENCY_CONFLICT", 409); Assert(f.Store.Commits == 0);
});
await Check("cross-author Response masks both receipt and retry before version disclosure", async () => {
    var f = new Fixture(); f.SeedReceipt(null, false); f.Store.State.Response!.ParentAuthorUserId = Guid.NewGuid();
    await Error(() => f.Gateway.GetReceiptAsync(f.Context.Principal, f.TaskId), "TASK_NOT_FOUND", 404);
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404); Assert(f.Store.Commits == 0);
});
await Check("cross-author Draft masks command as 404", async () => {
    var f = new Fixture(); f.SeedDraft(); f.Store.State.Draft!.ParentAuthorUserId = Guid.NewGuid();
    await Error(() => f.SubmitCommand(f.Command(7)), "TASK_NOT_FOUND", 404); Assert(f.Store.Commits == 0);
});
await Check("denied transaction access prevents key lookup", async () => {
    var f = new Fixture(); f.CommandAccess.Allow = false;
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404); Assert(f.Store.KeyLookups == 0 && f.Store.Commits == 0);
});
await Check("Context actor does not replace original principal", async () => {
    var f = new Fixture(); f.Context = f.Context with { ExpectedAccess = f.Context.ExpectedAccess with { ActorUserId = Guid.NewGuid() } };
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404); Assert(f.CommandAccess.Authorizations == 0);
});
await Check("anonymous principal rejected before storage", async () => {
    var f = new Fixture(); f.Context = f.Context with { Principal = new ClaimsPrincipal() };
    await Error(() => f.SubmitCommand(), "AUTHENTICATION_REQUIRED", 401); Assert(f.Store.Opens == 0);
});
foreach (var (label, change) in new (string, Func<ParentAccessExpectation, ParentAccessExpectation>)[] {
    ("consent", x => x with { ConsentId = Guid.NewGuid() }),
    ("document", x => x with { DocumentVersionId = Guid.NewGuid() }),
    ("binding", x => x with { GuardianId = Guid.NewGuid() }),
    ("generation", x => x with { EligibilityStamp = "changed-synthetic-generation" }),
}) await Check($"changed {label} snapshot rejected without replacement/retry", async () => {
    var f = new Fixture(); f.CommandAccess.ChangeSnapshot = change;
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404);
    Assert(f.Store.Commits == 0 && f.Store.KeyLookups == 0 && f.CommandAccess.Authorizations == 1);
});
await Check("missing server eligibility data rejected", async () => {
    var f = new Fixture(); f.Context = f.Context with { ExpectedAccess = f.Context.ExpectedAccess with { EligibilityStamp = "" } };
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404); Assert(f.CommandAccess.Authorizations == 0);
});
await Check("commit failure preserves pre-existing draft and no partial Response (Fake)", async () => {
    var f = new Fixture(); f.SeedDraft(); f.Store.FailCommit = true; var before = f.Store.Snapshot();
    try { await f.SubmitCommand(f.Command(7)); throw new Exception("Expected IOException"); } catch (IOException) { }
    Assert(f.Store.Snapshot() == before && f.Store.Commits == 0 && f.Store.State.Response is null);
});
await Check("final qualification denial rolls back staged canonical Response (Fake)", async () => {
    var f = new Fixture(); f.CommandAccess.RevokeAtFinish = true; var before = f.Store.Snapshot();
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404);
    Assert(f.Store.Snapshot() == before && f.Store.Commits == 0 && f.CommandAccess.Revalidations == 1);
});
await Check("successful replay must revalidate before returning receipt", async () => {
    var f = new Fixture(); await f.SubmitCommand(); f.CommandAccess.RevokeAtFinish = true;
    await Error(() => f.SubmitCommand(f.Command(999)), "TASK_NOT_FOUND", 404); Assert(f.Store.Commits == 1 && f.Store.AddResponses == 1);
});
await Check("new key cannot overwrite submitted task", async () => {
    var f = new Fixture(); await f.SubmitCommand();
    await Error(() => f.SubmitCommand(key: Guid.NewGuid()), "TASK_SUBMITTED", 409); Assert(f.Store.AddResponses == 1);
});
await Check("empty key rejected without storage", async () => {
    var f = new Fixture(); await Error(() => f.SubmitCommand(key: Guid.Empty), "INVALID_INPUT", 400); Assert(f.Store.Opens == 0);
});
await Check("authorized historical receipt stays distinct from command replay eligibility", async () => {
    var f = new Fixture(); f.SeedReceipt(null, false); var r = await f.Gateway.GetReceiptAsync(f.Context.Principal, f.TaskId);
    Assert(!r.Replayed && r.ResponseId == f.Store.State.Response!.ResponseId);
    await Error(() => f.SubmitCommand(), "IDEMPOTENCY_VERSION_UNSUPPORTED", 409);
    Assert(f.Store.State.Response!.HashVersion is null && f.Store.State.Response.RespondentRelationship is null);
});
await Check("legacy public Submit and shared SubmissionHash remain unchanged", async () => {
    var f = new Fixture(); var hash = f.Validate(f.Payload()).SubmissionHash(f.TaskId, f.VersionId);
    var first = await f.Gateway.SubmitAsync(f.Context.Principal, f.TaskId, f.Key, f.Payload());
    var replay = await f.Gateway.SubmitAsync(f.Context.Principal, f.TaskId, f.Key, f.Payload(false));
    Assert(replay.Replayed && replay.ResponseId == first.ResponseId);
    Assert(f.Store.State.Response!.HashVersion is null && f.Store.State.Response.RespondentRelationship is null);
    Assert(f.Store.State.Response.PayloadHash.SequenceEqual(hash));
});
await Check("Adapter proof for wrong Task is rejected", async () => {
    var f = new Fixture(); f.CommandAccess.ReturnWrongTask = true;
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404); Assert(f.Store.KeyLookups == 0 && f.Store.Commits == 0);
});
await Check("incomplete answers cannot be submitted through internal command", async () => {
    var f = new Fixture();
    await Error(() => f.SubmitCommand(f.Command() with { Payload = f.Payload() with { Answers = [] } }), "INVALID_ANSWERS", 400);
    Assert(f.Store.Commits == 0 && f.Store.State.Response is null);
});
await Check("invalid hash bytes do not produce a replay receipt", async () => {
    var f = new Fixture(); f.SeedReceipt(ParentCanonicalSubmissionEncoder.FormatVersion, true);
    f.Store.State.Response!.PayloadHash = [];
    await Error(() => f.SubmitCommand(), "IDEMPOTENCY_CONFLICT", 409); Assert(f.Store.Commits == 0);
});
await Check("new command does not bypass cancelled Task gate", async () => {
    var f = new Fixture(); f.Store.State.Task.TaskStatus = "CANCELLED";
    await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404); Assert(f.Store.Commits == 0);
});
// The Adapter deliberately permits Case/Task access here: the core itself must mask
// a different persisted author before inspecting state/version/hash compatibility.
foreach (var owner in new[] { "draft", "response" })
foreach (var anomaly in new[] { "task-state", "version-state", "missing-version", "null-hash", "unknown-hash", "combined" })
    await Check($"cross-author {owner} + {anomaly} masks as 404 before version/key checks", async () => {
        var f = new Fixture();
        if (owner == "draft") { f.SeedDraft(); f.Store.State.Draft!.ParentAuthorUserId = Guid.NewGuid(); }
        else { f.SeedReceipt(null, false); f.Store.State.Response!.ParentAuthorUserId = Guid.NewGuid(); }
        MaskingAnomaly.Apply(f, anomaly);
        var before = f.Store.Snapshot();
        await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404);
        Assert(f.Store.Snapshot() == before && f.Store.Commits == 0 && f.Store.KeyLookups == 0);
        Assert(f.Store.TaskReads == 1 && f.Store.VersionReads == 0 && f.Store.DraftReads == 1 && f.Store.ResponseReads == 1);
    });
foreach (var anomaly in new[] { "task-state", "version-state", "missing-version", "null-hash", "unknown-hash", "combined" })
    await Check($"no access + {anomaly} rejects before any Task/content lookup", async () => {
        var f = new Fixture(); f.SeedReceipt(null, false); MaskingAnomaly.Apply(f, anomaly);
        f.CommandAccess.Allow = false; var before = f.Store.Snapshot();
        await Error(() => f.SubmitCommand(), "TASK_NOT_FOUND", 404);
        Assert(f.Store.Snapshot() == before && f.Store.Commits == 0 && f.Store.KeyLookups == 0);
        Assert(f.Store.TaskReads == 0 && f.Store.VersionReads == 0 && f.Store.DraftReads == 0 && f.Store.ResponseReads == 0);
    });
foreach (var (anomaly, code) in new[] { ("task-state", "TASK_UNAVAILABLE"), ("version-state", "VERSION_UNAVAILABLE"), ("missing-version", "VERSION_UNAVAILABLE") })
{
    await Check($"own-author {anomaly} retains existing 409", async () => {
        var f = new Fixture(); f.SeedDraft(); MaskingAnomaly.Apply(f, anomaly);
        await Error(() => f.SubmitCommand(f.Command(7)), code, 409); Assert(f.Store.Commits == 0);
    });
    await Check($"legacy Submit keeps original {anomaly} ordering/409", async () => {
        var f = new Fixture(); f.SeedDraft(); f.Store.State.Draft!.ParentAuthorUserId = Guid.NewGuid();
        MaskingAnomaly.Apply(f, anomaly);
        await Error(() => f.Gateway.SubmitAsync(f.Context.Principal, f.TaskId, f.Key, f.Payload()), code, 409);
        Assert(f.Store.Commits == 0 && f.Store.DraftReads == 0 && f.Store.ResponseReads == 0);
    });
}
foreach (var reason in new[] {
    "real Guardian/Consent generation, fresh reads and writer lock protocol are not delivered",
    "actual EF/MySQL rollback, uniqueness, concurrency and deadlocks are not tested",
    "approved incremental Migration/common Schema and HTTP/DI integration are not delivered"
}) { blocked++; Console.WriteLine($"BLOCKED {reason}"); }
Console.WriteLine($"SUMMARY PASS={passed} FAIL={failed} BLOCKED={blocked}; production internal core + Fake only; NOT MySQL/HTTP acceptance");
Environment.ExitCode = failed > 0 ? 1 : blocked > 0 ? 2 : 0;

static class MaskingAnomaly
{
    public static void Apply(Fixture f, string anomaly)
    {
        if (anomaly is "task-state" or "combined") f.Store.State.Task.TaskStatus = "SYNTHETIC_UNAVAILABLE";
        if (anomaly is "version-state" or "combined") f.Store.State.Version.VersionStatus = "DRAFT";
        if (anomaly == "missing-version") f.Store.State.Version.QuestionnaireVersionId = Guid.NewGuid();
        // For draft-owner fixtures, add an otherwise consistent same-author receipt, so
        // the other-author draft must win even when a stored hash/version is present.
        if (anomaly is "null-hash" or "unknown-hash" or "combined")
        {
            var savedStatus = f.Store.State.Task.TaskStatus;
            var hashVersion = anomaly == "null-hash" ? null : "synthetic.unsupported/v99";
            if (f.Store.State.Response is null) f.SeedReceipt(hashVersion, false);
            else f.Store.State.Response.HashVersion = hashVersion;
            if (anomaly == "combined") f.Store.State.Task.TaskStatus = savedStatus;
        }
    }
}

// No friend assembly/public injection is added. Reflection is confined to this test.
static class CommandCall
{
    public static ParentQuestionnaireGateway Create(Fixture f) => (ParentQuestionnaireGateway)typeof(ParentQuestionnaireGateway)
        .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 6)
        .Invoke([f.Store, new DenyParentTaskAccessAdapter(), new QuestionnaireAnswerValidator(), f.Clock,
            new ParentQuestionnaireOptions(f.VersionId), f.CommandAccess]);
    public static Task<ParentQuestionnaireReceipt> Submit(ParentQuestionnaireGateway gateway,
        ParentGatewayCallContext context, Guid task, Guid key, ParentSubmitCommand command)
    {
        try { return (Task<ParentQuestionnaireReceipt>)typeof(ParentQuestionnaireGateway)
            .GetMethod("SubmitCommandAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(gateway, [context, task, key, command, CancellationToken.None])!; }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
}
sealed class SyntheticState
{
    public QuestionnaireTask Task { get; set; } = null!;
    public QuestionnaireVersion Version { get; set; } = null!;
    public QuestionnaireDraft? Draft { get; set; }
    public QuestionnaireResponse? Response { get; set; }
    public SyntheticState Clone() => JsonSerializer.Deserialize<SyntheticState>(JsonSerializer.Serialize(this))!;
}
sealed class Fixture
{
    public Guid TaskId { get; } = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public Guid VersionId { get; } = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public Guid UserId { get; } = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public Guid Key { get; } = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public ParentGatewayCallContext Context { get; set; }
    public TransactionAccess CommandAccess { get; } = new();
    public ParentSubmitCommand Command(ulong revision = 0) => new(Payload(), revision, "MOTHER");
    public Task<ParentQuestionnaireReceipt> SubmitCommand(ParentSubmitCommand? command = null, Guid? key = null) =>
        CommandCall.Submit(CommandCall.Create(this), Context, TaskId, key ?? Key, command ?? Command());
    public ClaimsPrincipal Principal => new(new ClaimsIdentity([new Claim("userId", UserId.ToString())], "synthetic-test"));
    public TimeProvider Clock { get; } = new FixedClock();
    public MemoryStore Store { get; }
    public SyntheticAccess Adapter { get; }
    public ParentQuestionnaireGateway Gateway => new(Store, Adapter, new(), Clock, new(VersionId));
    public Fixture()
    {
        var caseId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var questionnaire = Guid.Parse("66666666-6666-4666-8666-666666666666");
        Store = new(new() {
            Task = new() { TaskId = TaskId, CaseId = caseId, QuestionnaireId = questionnaire,
                QuestionnaireVersionId = VersionId, RespondentRole = "PARENT", AssignmentRound = 1, TaskStatus = "PENDING" },
            Version = new() { QuestionnaireId = questionnaire, QuestionnaireVersionId = VersionId,
                RespondentRole = "PARENT", VersionStatus = "PUBLISHED",
                DefinitionSnapshot = """{"questions":[{"questionId":"q1","type":"singleChoice","options":[{"value":"0"},{"value":"1"}]},{"questionId":"q2","type":"singleChoice","options":[{"value":"2"}]}]}""" }
        });
        Context = new(Principal, new(UserId, Guid.Parse("77777777-7777-4777-8777-777777777777"), caseId,
            Guid.Parse("88888888-8888-4888-8888-888888888888"), Guid.Parse("99999999-9999-4999-8999-999999999999"),
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), "synthetic-eligibility-v1"));
        Adapter = new(new(UserId, Guid.Parse("77777777-7777-4777-8777-777777777777"), caseId, TaskId,
            Guid.Parse("88888888-8888-4888-8888-888888888888"), Guid.Parse("99999999-9999-4999-8999-999999999999")));
    }
    public QuestionnaireSubmission Payload(bool normalized = true) => new(VersionId,
        normalized ? "Synthetic parent" : "  Synthetic parent  ", "2026-10-30",
        normalized ? [new("q1", "1"), new("q2", "2")] : [new("q2", "2"), new("q1", "1")],
        normalized ? null : "   ");
    public ValidatedQuestionnaireContent Validate(QuestionnaireSubmission p) =>
        new QuestionnaireAnswerValidator().Validate(Store.State.Version.DefinitionSnapshot, p, new(2026, 11, 4), true);
    public void SeedDraft()
    {
        Store.State.Task.TaskStatus = "IN_PROGRESS";
        Store.State.Draft = new() { TaskId = TaskId, QuestionnaireVersionId = VersionId, ParentAuthorUserId = UserId,
            AnswersJson = "[]", Revision = 7, RespondentRelationship = null };
    }
    public void SeedReceipt(string? version, bool canonical)
    {
        var content = Validate(Payload());
        var now = Clock.GetUtcNow().UtcDateTime;
        Store.State.Task.TaskStatus = "SUBMITTED"; Store.State.Task.SubmittedAtUtc = now;
        Store.State.Response = new() { ResponseId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            TaskId = TaskId, QuestionnaireVersionId = VersionId, ParentAuthorUserId = UserId, IdempotencyKey = Key,
            RespondentName = content.RespondentName!, FilledOn = content.FilledOn!.Value, AnswersJson = content.AnswersJson,
            HashVersion = version, RespondentRelationship = canonical ? "MOTHER" : null, SubmittedAtUtc = now,
            PayloadHash = canonical ? new ParentCanonicalSubmissionEncoder().ComputeHash(TaskId, VersionId, content, "MOTHER")
                : content.SubmissionHash(TaskId, VersionId) };
    }
    private sealed class FixedClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero); }
}
sealed class SyntheticAccess(ParentTaskAccess evidence) : IParentTaskAccessAdapter
{
    public bool Allow = true;
    public Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction tx, ClaimsPrincipal p, Guid id, CancellationToken ct)
    {
        if (!Allow) throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Synthetic denial", 404);
        return Task.FromResult(evidence);
    }
    public Task RevalidateAsync(IParentQuestionnaireTransaction tx, ParentTaskAccess access, CancellationToken ct)
    {
        if (!Allow) throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Synthetic denial", 404);
        return Task.CompletedTask;
    }
}
// Intentionally single-operation, non-concurrent Fake. Not a transactional database emulator.
sealed class MemoryStore(SyntheticState state) : IParentQuestionnaireStore
{
    public SyntheticState State = state;
    public bool FailCommit;
    public int Commits, KeyLookups, Opens, AddResponses;
    public int TaskReads, VersionReads, DraftReads, ResponseReads;
    public string Snapshot() => JsonSerializer.Serialize(State);
    public Task<IParentQuestionnaireTransaction> OpenAsync(CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Opens++; return Task.FromResult<IParentQuestionnaireTransaction>(new Tx(this, State.Clone())); }
    private sealed class Tx(MemoryStore store, SyntheticState local) : IParentQuestionnaireTransaction
    {
        public ApplicationDbContext? DbContext => null;
        public Task<QuestionnaireTask?> LockTaskAsync(Guid id, CancellationToken ct)
        { store.TaskReads++; return Task.FromResult(id == local.Task.TaskId ? local.Task : null); }
        public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct)
        { store.VersionReads++; return Task.FromResult(id == local.Version.QuestionnaireVersionId ? local.Version : null); }
        public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct)
        { store.DraftReads++; return Task.FromResult(local.Draft?.TaskId == id ? local.Draft : null); }
        public Task<QuestionnaireResponse?> FindResponseAsync(Guid id, CancellationToken ct)
        { store.ResponseReads++; return Task.FromResult(local.Response?.TaskId == id ? local.Response : null); }
        public Task<QuestionnaireResponse?> FindByKeyAsync(Guid user, Guid key, CancellationToken ct)
        {
            store.KeyLookups++;
            return Task.FromResult(local.Response is { } r && r.ParentAuthorUserId == user && r.IdempotencyKey == key ? r : null);
        }
        public void AddDraft(QuestionnaireDraft draft) => local.Draft = draft;
        public void AddResponse(QuestionnaireResponse response) { store.AddResponses++; local.Response = response; }
        public Task CommitAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (store.FailCommit) throw new IOException("Synthetic failure");
            store.State = local.Clone(); store.Commits++; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// A synthetic authorization proof is NOT a delivered Guardian/Consent implementation.
sealed class TransactionAccess : IParentTransactionAccessAdapter
{
    public bool Allow = true, RevokeAtFinish, SameTransaction, ReturnWrongTask;
    public int Authorizations, Revalidations;
    public ClaimsPrincipal? Principal;
    public Func<ParentAccessExpectation, ParentAccessExpectation>? ChangeSnapshot;
    private IParentQuestionnaireTransaction? seen;
    private ParentTransactionAccess? issued;
    public Task<ParentTransactionAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction tx, ClaimsPrincipal principal,
        Guid taskId, ParentAccessExpectation expected, bool writing, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Authorizations++; Principal = principal; seen = tx;
        if (!Allow || !writing || tx.DbContext is not null)
            throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Synthetic denial", 404);
        issued = new(ReturnWrongTask ? Guid.NewGuid() : taskId, ChangeSnapshot?.Invoke(expected) ?? expected);
        return Task.FromResult(issued);
    }
    public Task RevalidateAsync(IParentQuestionnaireTransaction tx, ClaimsPrincipal principal,
        ParentTransactionAccess access, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Revalidations++;
        SameTransaction = ReferenceEquals(seen, tx) && ReferenceEquals(Principal, principal) && ReferenceEquals(issued, access);
        if (!SameTransaction || !Allow || RevokeAtFinish)
            throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Synthetic denial", 404);
        return Task.CompletedTask;
    }
}
