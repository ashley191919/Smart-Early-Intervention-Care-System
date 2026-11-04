using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;

// Exercises production classes, never a test-written Submit/replay decision engine.
int passed = 0, failed = 0, blocked = 0;
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
async Task Pass(string name, Func<Task> work)
{
    try { await work(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}"); }
}
void Block(string name, string evidence)
{ blocked++; Console.WriteLine($"BLOCKED {name}: {evidence}"); }
async Task Expect(Func<Task> work, string code, int status)
{
    try { await work(); throw new Exception("Expected rejection"); }
    catch (QuestionnaireWorkflowException e) { Assert(e.Code == code && e.Status == status); }
}

// Version acceptance is a NEW desired contract probe. An observed legacy success is BLOCKED,
// never PASS. Unexpected behavior is FAIL, so these are not unconditional skipped tests.
foreach (var (label, version) in new (string, string?)[] {
    ("NULL", null), ("unknown", "synthetic.unsupported/v99"),
    ("case-changed", ParentCanonicalSubmissionEncoder.FormatVersion.ToUpperInvariant()),
    ("suffix-space", ParentCanonicalSubmissionEncoder.FormatVersion + " ") })
{
    var f = new Fixture(); f.SeedReceipt(version, canonical: false);
    try
    {
        var receipt = await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload());
        Assert(receipt.Replayed && receipt.ResponseId == f.Store.State.Response!.ResponseId);
        Block($"{label} HashVersion must reject replay", "actual legacy Gateway returns receipt; required IDEMPOTENCY_VERSION_UNSUPPORTED/409 not implemented");
    }
    catch (QuestionnaireWorkflowException e) when (e.Code == "IDEMPOTENCY_VERSION_UNSUPPORTED" && e.Status == 409)
    { passed++; Console.WriteLine($"PASS {label} HashVersion rejects replay"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {label} version probe: {e.GetType().Name}"); }
}

await Pass("authorized historical receipt remains a distinct read, not proof of replay eligibility", async () => {
    var f = new Fixture(); f.SeedReceipt(null, canonical: false);
    var result = await f.Gateway.GetReceiptAsync(f.Principal, f.TaskId);
    Assert(result.ResponseId == f.Store.State.Response!.ResponseId && !result.Replayed);
    Assert(f.Store.State.Response.HashVersion is null && f.Store.State.Response.RespondentRelationship is null);
});

{
    var f = new Fixture(); f.SeedReceipt(ParentCanonicalSubmissionEncoder.FormatVersion, canonical: true);
    try
    {
        // Whitespace/order changes normalize to the seeded canonical content.
        var receipt = await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload(normalized: false));
        Assert(receipt.Replayed && receipt.ResponseId == f.Store.State.Response!.ResponseId);
        passed++; Console.WriteLine("PASS canonical v1 normalized retry returns same receipt");
    }
    catch (QuestionnaireWorkflowException e) when (e.Code == "IDEMPOTENCY_CONFLICT" && e.Status == 409)
    { Block("canonical v1 normalized retry", "actual Gateway computes legacy SubmissionHash and rejects the v1 hash; parent command path is not connected"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL canonical replay: {e.GetType().Name}"); }
}

// New composition coverage: the actual ParentSubmitCommand is consumed by the existing
// Validator/encoder. This is NOT a replay resolver or a first-submit implementation.
await Pass("command revision changes leave normalized canonical bytes unchanged (composition only)", () => {
    var f = new Fixture();
    var a = new ParentSubmitCommand(f.Payload(false), 0, "MOTHER");
    var b = a with { ExpectedRevision = ulong.MaxValue, Payload = f.Payload() };
    byte[] Bytes(ParentSubmitCommand c) => new ParentCanonicalSubmissionEncoder().Encode(
        f.TaskId, f.VersionId, f.Validate(c.Payload), c.RespondentRelationship);
    Assert(Bytes(a).SequenceEqual(Bytes(b)));
    return Task.CompletedTask;
});

// Verify an unknown hash version cannot become a new oracle before author/access checks.
await Pass("cross-author unknown-version Response masks both receipt and retry as 404", async () => {
    var f = new Fixture(); f.SeedReceipt(null, canonical: false);
    f.Store.State.Response!.ParentAuthorUserId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    await Expect(() => f.Gateway.GetReceiptAsync(f.Principal, f.TaskId), "TASK_NOT_FOUND", 404);
    await Expect(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload()), "TASK_NOT_FOUND", 404);
    Assert(f.Store.Commits == 0);
});
await Pass("denied access precedes unknown-version receipt and retry processing (Fake adapter only)", async () => {
    var f = new Fixture(); f.SeedReceipt(null, canonical: false); f.Adapter.Allow = false;
    await Expect(() => f.Gateway.GetReceiptAsync(f.Principal, f.TaskId), "TASK_NOT_FOUND", 404);
    await Expect(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload()), "TASK_NOT_FOUND", 404);
    Assert(f.Store.Commits == 0 && f.Store.KeyLookups == 0);
});
await Pass("read/retry use default deny when real adapter is absent", async () => {
    var f = new Fixture(); f.SeedReceipt(null, canonical: false);
    var gateway = new ParentQuestionnaireGateway(f.Store, new DenyParentTaskAccessAdapter(), new(), f.Clock, new(f.VersionId));
    await Expect(() => gateway.GetReceiptAsync(f.Principal, f.TaskId), "PARENT_ACCESS_NOT_INTEGRATED", 503);
    await Expect(() => gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload()), "PARENT_ACCESS_NOT_INTEGRATED", 503);
    Assert(f.Store.KeyLookups == 0 && f.Store.Commits == 0);
});

// Extend prior rollback coverage specifically to the new nullable persisted metadata and
// a pre-existing draft. Fake copy-on-commit proves only Gateway failure propagation.
await Pass("commit failure preserves existing draft revision/relationship and no Response (Fake only)", async () => {
    var f = new Fixture(); f.SeedDraft(); var before = f.Store.Snapshot(); f.Store.FailCommit = true;
    try { await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload()); throw new Exception("Expected IO failure"); }
    catch (IOException) { }
    Assert(f.Store.Snapshot() == before && f.Store.State.Response is null && f.Store.Commits == 0);
    Assert(f.Store.State.Draft!.Revision == 7 && f.Store.State.Draft.RespondentRelationship is null);
});
await Pass("denial at final revalidation preserves new persisted metadata (Fake only)", async () => {
    var f = new Fixture(); f.SeedDraft(); f.Adapter.RevokeAtFinish = true; var before = f.Store.Snapshot();
    await Expect(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload()), "TASK_NOT_FOUND", 404);
    Assert(f.Store.Snapshot() == before && f.Store.Commits == 0 && f.Adapter.Revalidated);
});

{
    // No invented overload. Absence is the first-submit/replay-order blocker.
    var acceptsCommand = typeof(ParentQuestionnaireGateway).GetMethods()
        .Any(m => m.Name == "SubmitAsync" && m.GetParameters().Any(p => p.ParameterType == typeof(ParentSubmitCommand)));
    if (!acceptsCommand)
        Block("first-submit Draft Revision and successful retry before Revision", "actual Submit accepts QuestionnaireSubmission only; no ParentSubmitCommand/ExpectedRevision parameter");
    else { failed++; Console.WriteLine("FAIL command path changed: add behavioral revision/order cases before claiming coverage"); }
}
{
    var f = new Fixture();
    try
    {
        await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Payload());
        var r = f.Store.State.Response!;
        Assert(r.HashVersion is null && r.RespondentRelationship is null);
        Assert(r.PayloadHash.SequenceEqual(f.Validate(f.Payload()).SubmissionHash(f.TaskId, f.VersionId)));
        Block("new parent Submit requires Relationship and stores canonical v1/version", "actual legacy path still stores null relationship/version and legacy hash; do not enable it");
    }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL legacy write characterization: {e.GetType().Name}"); }
}

Block("Guardian/Consent generation and revocation races", "real adapter/writer lock protocol is not delivered; Fake approval is not evidence");
Block("database atomicity/unique keys/parallel Submit/deadlocks", "no EF/MySQL transaction executed; Fake copy-on-commit cannot validate engine rollback");
Console.WriteLine($"SUMMARY PASS={passed} FAIL={failed} BLOCKED={blocked}; actual Gateway + synthetic Fake only; no API/DB; NOT integration acceptance");
// Exit 2 explicitly means incomplete contract readiness, not green end-to-end acceptance.
Environment.ExitCode = failed > 0 ? 1 : blocked > 0 ? 2 : 0;

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
                DefinitionSnapshot = """{"questions":[{"questionId":"q1","type":"singleChoice","options":[{"value":"1"}]},{"questionId":"q2","type":"singleChoice","options":[{"value":"2"}]}]}""" }
        });
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
    public bool Allow = true, RevokeAtFinish, Revalidated;
    public Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction tx, ClaimsPrincipal p, Guid id, CancellationToken ct)
    {
        if (!Allow) throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Synthetic denial", 404);
        return Task.FromResult(evidence);
    }
    public Task RevalidateAsync(IParentQuestionnaireTransaction tx, ParentTaskAccess access, CancellationToken ct)
    {
        Revalidated = true;
        if (!Allow || RevokeAtFinish) throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Synthetic denial", 404);
        return Task.CompletedTask;
    }
}
// Intentionally single-operation, non-concurrent Fake. Not a transactional database emulator.
sealed class MemoryStore(SyntheticState state) : IParentQuestionnaireStore
{
    public SyntheticState State = state;
    public bool FailCommit;
    public int Commits, KeyLookups;
    public string Snapshot() => JsonSerializer.Serialize(State);
    public Task<IParentQuestionnaireTransaction> OpenAsync(CancellationToken ct) =>
        Task.FromResult<IParentQuestionnaireTransaction>(new Tx(this, State.Clone()));
    private sealed class Tx(MemoryStore store, SyntheticState local) : IParentQuestionnaireTransaction
    {
        public ApplicationDbContext? DbContext => null;
        public Task<QuestionnaireTask?> LockTaskAsync(Guid id, CancellationToken ct) => Task.FromResult(id == local.Task.TaskId ? local.Task : null);
        public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) => Task.FromResult(id == local.Version.QuestionnaireVersionId ? local.Version : null);
        public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct) => Task.FromResult(local.Draft?.TaskId == id ? local.Draft : null);
        public Task<QuestionnaireResponse?> FindResponseAsync(Guid id, CancellationToken ct) => Task.FromResult(local.Response?.TaskId == id ? local.Response : null);
        public Task<QuestionnaireResponse?> FindByKeyAsync(Guid user, Guid key, CancellationToken ct)
        {
            store.KeyLookups++;
            return Task.FromResult(local.Response is { } r && r.ParentAuthorUserId == user && r.IdempotencyKey == key ? r : null);
        }
        public void AddDraft(QuestionnaireDraft draft) => local.Draft = draft;
        public void AddResponse(QuestionnaireResponse response) => local.Response = response;
        public Task CommitAsync(CancellationToken ct)
        {
            if (store.FailCommit) throw new IOException("Synthetic failure");
            store.State = local.Clone(); store.Commits++; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
