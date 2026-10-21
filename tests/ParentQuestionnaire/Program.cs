using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using EarlyInterventionCare.Api.Options;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
async Task Error(Func<Task> action, string code)
{
    try { await action(); throw new Exception("Expected " + code); }
    catch (QuestionnaireWorkflowException e) { Check(e.Code == code, code + " actual=" + e.Code); }
}
Fixture New() => new();

// The fixture is intentionally synthetic, not an approved clinical parent questionnaire.
{
    var f = New();
    var empty = await f.Gateway.GetDraftAsync(f.Principal, f.TaskId);
    Check(empty.Revision == 0 && empty.Answers.Count == 0, "empty draft");
    var first = await f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft());
    Check(first.Revision == 1 && first.TaskStatus == "IN_PROGRESS", "partial save");
    Check(f.Store.State.Draft!.ParentAuthorUserId == f.UserId && f.Store.State.Draft.LastSavedByGrantId is null, "trusted parent author only");
    Check(f.Store.State.Response is null, "draft does not submit");
    var recreated = f.CreateGateway();
    Check((await recreated.GetDraftAsync(f.Principal, f.TaskId)).Revision == 1, "server-side reread through recreated service");
    await Error(() => recreated.SaveDraftAsync(f.Principal, f.TaskId, f.Draft()), "DRAFT_CONFLICT");
    Check(f.Store.State.Draft.Revision == 1, "stale revision preserves content");
    var second = await recreated.SaveDraftAsync(f.Principal, f.TaskId, f.Draft(1));
    Check(second.Revision == 2, "revision increments");
    var key = Guid.NewGuid();
    var receipt = await recreated.SubmitAsync(f.Principal, f.TaskId, key, f.Submit());
    Check(!receipt.Replayed && receipt.TaskStatus == "SUBMITTED", "commit receipt");
    Check(f.Store.State.Task.TaskStatus == "SUBMITTED" && f.Store.State.Response?.ResponseId == receipt.ResponseId, "response and task atomic");
    Check(f.Store.State.Response!.ParentAuthorUserId == f.UserId && f.Store.State.Response.SubmittedByGrantId is null, "response author");
    var replay = await f.Gateway.SubmitAsync(f.Principal, f.TaskId, key, f.Submit());
    Check(replay.Replayed && replay.ResponseId == receipt.ResponseId, "same-key replay");
    Check((await f.Gateway.GetReceiptAsync(f.Principal, f.TaskId)).ResponseId == receipt.ResponseId, "receipt reread");
    Check((await f.Gateway.GetStatusAsync(f.Principal, f.TaskId)).TaskStatus == "SUBMITTED", "status reread");
    Check(!JsonSerializer.Serialize(receipt).Contains("Answers") && !JsonSerializer.Serialize(receipt).Contains("ParentAuthor"), "receipt privacy");
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft(2)), "TASK_SUBMITTED");
    await Error(() => f.Gateway.GetDraftAsync(f.Principal, f.TaskId), "TASK_SUBMITTED");
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, Guid.NewGuid(), f.Submit()), "TASK_SUBMITTED");
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, key, f.Submit() with { Observation = "changed" }), "IDEMPOTENCY_CONFLICT");
    f.Adapter.Allow = false;
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, key, f.Submit()), "TASK_NOT_FOUND");
    await Error(() => f.Gateway.GetReceiptAsync(f.Principal, f.TaskId), "TASK_NOT_FOUND");
}
foreach (var principal in new[] { new ClaimsPrincipal(),
    new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", Guid.Empty.ToString())], "test")),
    new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", Guid.NewGuid().ToString()), new Claim("userId", Guid.NewGuid().ToString())], "test")),
    new ClaimsPrincipal(new[] { new ClaimsIdentity([new Claim("userId", Guid.NewGuid().ToString())], "a"), new ClaimsIdentity([], "b") }) })
{
    var f = New();
    await Error(() => f.Gateway.GetDraftAsync(principal, f.TaskId), "AUTHENTICATION_REQUIRED");
    Check(f.Store.Opens == 0, "invalid identity does not open store");
}
foreach (var mutation in new Action<Fixture>[] {
    f => f.Adapter.Allow = false, // revoked link, missing Consent, wrong role/organization: adapter denies
    f => f.Adapter.Evidence = f.Adapter.Evidence with { UserId = Guid.NewGuid() },
    f => f.Adapter.Evidence = f.Adapter.Evidence with { CaseId = Guid.NewGuid() },
    f => f.Adapter.Evidence = f.Adapter.Evidence with { OrganizationId = Guid.Empty },
    f => f.Adapter.Evidence = f.Adapter.Evidence with { GuardianId = Guid.Empty },
    f => f.Adapter.Evidence = f.Adapter.Evidence with { ConsentRecordId = Guid.Empty },
    f => f.Adapter.Evidence = f.Adapter.Evidence with { TaskId = Guid.NewGuid() },
    f => f.Store.State.Task.RespondentRole = "TEACHER",
    f => f.Store.State.Task.TaskStatus = "CANCELLED" })
{
    var f = New(); mutation(f);
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft()), "TASK_NOT_FOUND");
    Check(f.Store.State.Draft is null, "denied save writes nothing");
}
{
    var f = New();
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, Guid.Empty, f.Submit()), "INVALID_INPUT");
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft() with { ExpectedRevision = null }), "INVALID_INPUT");
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft() with { QuestionnaireVersionId = Guid.NewGuid() }), "VERSION_MISMATCH");
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, Guid.NewGuid(), f.Submit() with { Answers = [] }), "INVALID_ANSWERS");
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, Guid.NewGuid(), f.Submit() with { Answers = [new("q", "bad")] }), "INVALID_ANSWERS");
    await Error(() => f.Gateway.GetReceiptAsync(f.Principal, f.TaskId), "RESPONSE_NOT_FOUND");
    Check(f.Store.State.Task.TaskStatus == "PENDING" && f.Store.State.Response is null, "invalid payload leaves task unchanged");
    f.Store.State.Version.RespondentRole = "TEACHER";
    await Error(() => f.Gateway.GetDraftAsync(f.Principal, f.TaskId), "VERSION_UNAVAILABLE");
}
{
    var f = New(); await f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft());
    var other = Fixture.PrincipalFor(Guid.NewGuid());
    f.Adapter.Evidence = f.Adapter.Evidence with { UserId = Guid.Parse(other.FindFirst("userId")!.Value) };
    await Error(() => f.Gateway.GetDraftAsync(other, f.TaskId), "TASK_NOT_FOUND");
    await Error(() => f.Gateway.SubmitAsync(other, f.TaskId, Guid.NewGuid(), f.Submit()), "TASK_NOT_FOUND");
    Check(f.Store.State.Draft!.Revision == 1, "another currently scoped parent cannot take over author");
}
foreach (var save in new[] { false, true })
{
    var f = New(); f.Store.FailCommit = true;
    try
    {
        if (save) await f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft());
        else await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit());
        throw new Exception("Expected simulated commit failure");
    }
    catch (IOException) { Check(true, "commit failure propagated"); }
    Check(f.Store.State.Draft is null && f.Store.State.Response is null && f.Store.State.Task.TaskStatus == "PENDING", "rollback on commit failure");
    f.Store.FailCommit = false;
    if (save) Check((await f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft())).Revision == 1, "draft retry");
    else Check(!(await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit())).Replayed, "submit retry");
}
{
    var f = New(); f.Adapter.RevokeBeforeCommit = true;
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit()), "TASK_NOT_FOUND");
    Check(f.Store.State.Response is null && f.Store.State.Task.TaskStatus == "PENDING", "transaction revalidation rolls back");
    Check(f.Adapter.SameTransaction, "authorize and revalidate receive same transaction");
}
{
    var f = New();
    var receipts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit())));
    Check(receipts.Select(r => r.ResponseId).Distinct().Count() == 1 && receipts.Count(r => r.Replayed) == 1, "overlapping submit deduplicated");
}
{
    var f = New();
    async Task<string> Save() { try { await f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft()); return "OK"; } catch (QuestionnaireWorkflowException e) { return e.Code; } }
    var outcomes = await Task.WhenAll(Save(), Save());
    Check(outcomes.Count(s => s == "OK") == 1 && outcomes.Count(s => s == "DRAFT_CONFLICT") == 1, "overlapping revision conflict");
}
{
    var f = New();
    var gateway = new ParentQuestionnaireGateway(f.Store, new DenyParentTaskAccessAdapter(), new(), TimeProvider.System, new(f.VersionId));
    await Error(() => gateway.GetDraftAsync(f.Principal, f.TaskId), "PARENT_ACCESS_NOT_INTEGRATED");
}
{
    var f = New(); f.Store.State.Version.DefinitionSnapshot = "{broken";
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft()), "DEFINITION_UNAVAILABLE");
    Check(f.Store.State.Draft is null, "invalid server definition does not save a draft");
}
{
    var f = New();
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit(), cancellation.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(f.Store.State.Response is null, "cancellation writes nothing"); }
    await f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft());
    f.Store.State.Draft!.Revision = ulong.MaxValue;
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft(ulong.MaxValue)), "DRAFT_CONFLICT");
}
{
    var f = New();
    f.Store.State.Response = new() { ResponseId = Guid.NewGuid(), TaskId = Guid.NewGuid(),
        QuestionnaireVersionId = f.VersionId, ParentAuthorUserId = f.UserId,
        IdempotencyKey = f.Key, RespondentName = "Synthetic", AnswersJson = "[]", PayloadHash = new byte[32] };
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit()), "IDEMPOTENCY_CONFLICT");
    Check(f.Store.State.Task.TaskStatus == "PENDING", "key bound to another task cannot submit");
}
{
    var f = New(); await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit());
    f.Adapter.RevokeBeforeCommit = true;
    await Error(() => f.Gateway.GetReceiptAsync(f.Principal, f.TaskId), "TASK_NOT_FOUND");
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit()), "TASK_NOT_FOUND");
}
{
    // Minimal medical Permission + scope tests; no DbContext is registered in this fixture.
    var f = New(); var permission = new ReadPermissions(); var readScope = new ReadScope(f.CaseId, f.TaskId);
    var services = new ServiceCollection(); services.AddLogging(); services.AddRouting();
    services.AddSingleton<IPermissionService>(permission);
    services.AddSingleton<ICaseTaskReadScope>(readScope);
    services.AddScopedCaseTaskReads();
    using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
    var reads = scope.ServiceProvider.GetRequiredService<ScopedReadAuthorization>();
    Check(!await reads.CanReadCaseAsync(f.Principal, f.CaseId), "missing permission denies even with correct scope");
    permission.Allowed = true;
    Check(await reads.CanReadCaseAsync(f.Principal, f.CaseId), "permission plus scope allows case");
    Check(await reads.CanReadTaskAsync(f.Principal, f.CaseId, f.TaskId), "permission plus scope allows task");
    Check(!await reads.CanReadCaseAsync(f.Principal, Guid.NewGuid()), "case scope blocks broad permission");
    Check(!await reads.CanReadTaskAsync(f.Principal, f.CaseId, Guid.NewGuid()), "task membership blocks broad permission");
    permission.Fail = true;
    try { await reads.CanReadCaseAsync(f.Principal, f.CaseId); throw new Exception("Expected permission failure"); }
    catch (PermissionLookupException) { Check(true, "permission failure propagates"); }
}
foreach (var connection in new string?[] { null, "", "Server=127.0.0.1;Port=3306;Database=earlycare_dev;User=root;Password=synthetic-only",
    "Server=example.invalid;Port=33326;Database=earlycare_v53_synthetic;User=earlycare_v53_app;Password=synthetic-only",
    "Server=127.0.0.1;Port=33326;Database=earlycare_v53_synthetic;User=earlycare_v53_app;Password=REPLACE_ME" })
{
    try { LocalIsolationSettings.ValidateConnection(connection); throw new Exception("Expected local configuration rejection"); }
    catch (InvalidOperationException e) { Check(e.Message == "LOCAL_ISOLATION_CONFIGURATION_REJECTED", "local guard rejects without connection detail"); }
}
LocalIsolationSettings.ValidateConnection("Server=127.0.0.1;Port=33326;Database=earlycare_v53_synthetic;User=earlycare_v53_app;Password=synthetic-test-only");
Check(true, "isolated connection shape validated without opening connection");
// Offline EF model inspection; the existing five migrations/snapshot deliberately remain unchanged.
using (var db = new ApplicationDbContextFactory().CreateDbContext([]))
{
    Check(db.Database.GetMigrations().Count() == 5, "five migrations preserved");
    Check(db.Database.HasPendingModelChanges(), "unapproved parent schema cannot silently pass deployment gate");
    Check(ParentAuthorSchemaProposal.IsExact(db), "only six additive author schema operations proposed");
    foreach (var type in new[] { typeof(QuestionnaireDraft), typeof(QuestionnaireResponse) })
    {
        var entity = db.Model.FindEntityType(type)!;
        Check(entity.FindProperty("ParentAuthorUserId")!.IsNullable, "nullable additive author");
        Check(entity.GetForeignKeys().Any(fk => fk.Properties.Any(p => p.Name == "ParentAuthorUserId") && fk.DeleteBehavior == DeleteBehavior.Restrict), "restricted author FK");
    }
    Check(db.Model.FindEntityType(typeof(QuestionnaireResponse))!.GetIndexes().Any(i => i.IsUnique && i.GetDatabaseName() == "uq_responses_parent_key"), "parent key unique");
}
{
    var f = New(); var services = new ServiceCollection();
    services.AddLogging(); services.AddRouting(); services.AddDbContext<ApplicationDbContext>(o => o.UseMySql("Server=localhost;Database=offline;User=offline", new MySqlServerVersion(new Version(8,0,46))));
    services.AddParentQuestionnaireCore(new(f.VersionId));
    services.AddScopedCaseTaskReads(); services.AddScopedCaseTaskReads();
    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    using var scope = provider.CreateScope();
    Check(scope.ServiceProvider.GetRequiredService<IParentTaskAccessAdapter>() is DenyParentTaskAccessAdapter, "default parent deny");
    Check(scope.ServiceProvider.GetRequiredService<ICaseTaskReadScope>() is DenyCaseTaskReadScope, "default medical scope deny");
    Check(!await scope.ServiceProvider.GetRequiredService<ScopedReadAuthorization>().CanReadCaseAsync(new ClaimsPrincipal(), f.CaseId), "anonymous medical denied without DB");
}
// Inconsistent persisted records must fail closed, including reads and idempotent replay.
foreach (var kind in new[] { "submitted-without-response", "response-with-pending-task", "wrong-response-version", "missing-submit-time", "different-submit-time" })
{
    var f = New();
    await f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit());
    switch (kind)
    {
        case "submitted-without-response": f.Store.State.Response = null; break;
        case "response-with-pending-task": f.Store.State.Task.TaskStatus = "PENDING"; break;
        case "wrong-response-version": f.Store.State.Response!.QuestionnaireVersionId = Guid.NewGuid(); break;
        case "missing-submit-time": f.Store.State.Task.SubmittedAtUtc = null; break;
        case "different-submit-time": f.Store.State.Task.SubmittedAtUtc = f.Store.State.Response!.SubmittedAtUtc.AddMinutes(1); break;
    }
    await Error(() => f.Gateway.GetStatusAsync(f.Principal, f.TaskId), "STORAGE_INCONSISTENT");
    await Error(() => f.Gateway.GetReceiptAsync(f.Principal, f.TaskId), "STORAGE_INCONSISTENT");
    await Error(() => f.Gateway.GetDraftAsync(f.Principal, f.TaskId), "STORAGE_INCONSISTENT");
    await Error(() => f.Gateway.SaveDraftAsync(f.Principal, f.TaskId, f.Draft()), "STORAGE_INCONSISTENT");
    await Error(() => f.Gateway.SubmitAsync(f.Principal, f.TaskId, f.Key, f.Submit()), "STORAGE_INCONSISTENT");
}
Console.WriteLine($"PASS {passed} checks; transactional Fake Store/Adapter and offline EF only; no MySQL, no HTTP host.");

sealed class State
{
    public QuestionnaireTask Task { get; set; } = null!;
    public QuestionnaireVersion Version { get; set; } = null!;
    public QuestionnaireDraft? Draft { get; set; }
    public QuestionnaireResponse? Response { get; set; }
    public State Clone() => JsonSerializer.Deserialize<State>(JsonSerializer.Serialize(this))!;
}
sealed class Fixture
{
    public Guid UserId { get; } = Guid.NewGuid(); public Guid CaseId { get; } = Guid.NewGuid();
    public Guid TaskId { get; } = Guid.NewGuid(); public Guid VersionId { get; } = Guid.NewGuid();
    public Guid Key { get; } = Guid.NewGuid();
    public ClaimsPrincipal Principal => PrincipalFor(UserId);
    public static ClaimsPrincipal PrincipalFor(Guid id) => new(new ClaimsIdentity([new Claim("userId", id.ToString())], "contract-test"));
    public Store Store { get; } public Adapter Adapter { get; }
    public ParentQuestionnaireGateway Gateway => CreateGateway();
    public ParentQuestionnaireGateway CreateGateway() => new(Store, Adapter, new(), TimeProvider.System, new(VersionId));
    public Fixture()
    {
        var questionnaireId = Guid.NewGuid();
        Store = new(new State {
            Task = new() { TaskId = TaskId, CaseId = CaseId, QuestionnaireId = questionnaireId, QuestionnaireVersionId = VersionId, RespondentRole = "PARENT", TaskStatus = "PENDING", AssignmentRound = 1 },
            Version = new() { QuestionnaireId = questionnaireId, QuestionnaireVersionId = VersionId, RespondentRole = "PARENT", VersionStatus = "PUBLISHED", DefinitionSnapshot = """{"questions":[{"questionId":"q","type":"singleChoice","options":[{"value":"1"}]}]}""" }
        });
        Adapter = new(new(UserId, Guid.NewGuid(), CaseId, TaskId, Guid.NewGuid(), Guid.NewGuid()));
    }
    public SaveQuestionnaireDraftRequest Draft(ulong revision = 0) => new(VersionId, revision, null, null, [], null);
    public QuestionnaireSubmission Submit() => new(VersionId, "Synthetic parent", "2026-10-01", [new("q", "1")], null);
}
sealed class Adapter(ParentTaskAccess evidence) : IParentTaskAccessAdapter
{
    public ParentTaskAccess Evidence = evidence;
    public bool Allow = true, RevokeBeforeCommit, SameTransaction;
    private IParentQuestionnaireTransaction? seen;
    public Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction tx, ClaimsPrincipal principal, Guid taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); seen = tx;
        if (!Allow) throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Unavailable", 404);
        return Task.FromResult(Evidence);
    }
    public Task RevalidateAsync(IParentQuestionnaireTransaction tx, ParentTaskAccess access, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); SameTransaction = ReferenceEquals(seen, tx);
        if (!SameTransaction || !Allow || RevokeBeforeCommit) throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Unavailable", 404);
        return Task.CompletedTask;
    }
}
sealed class Store(State initial) : IParentQuestionnaireStore
{
    public State State = initial; public bool FailCommit; public int Opens;
    private readonly SemaphoreSlim mutex = new(1);
    public async Task<IParentQuestionnaireTransaction> OpenAsync(CancellationToken ct)
    { Opens++; await mutex.WaitAsync(ct); await Task.Yield(); return new Tx(this, State.Clone()); }
    private sealed class Tx(Store store, State state) : IParentQuestionnaireTransaction
    {
        public ApplicationDbContext? DbContext => null;
        public Task<QuestionnaireTask?> LockTaskAsync(Guid id, CancellationToken ct) => Task.FromResult(id == state.Task.TaskId ? state.Task : null);
        public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) => Task.FromResult(id == state.Version.QuestionnaireVersionId ? state.Version : null);
        public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct) => Task.FromResult(state.Draft?.TaskId == id ? state.Draft : null);
        public Task<QuestionnaireResponse?> FindResponseAsync(Guid id, CancellationToken ct) => Task.FromResult(state.Response?.TaskId == id ? state.Response : null);
        public Task<QuestionnaireResponse?> FindByKeyAsync(Guid user, Guid key, CancellationToken ct) => Task.FromResult(state.Response is { } r && r.ParentAuthorUserId == user && r.IdempotencyKey == key ? r : null);
        public void AddDraft(QuestionnaireDraft draft) => state.Draft = draft;
        public void AddResponse(QuestionnaireResponse response) => state.Response = response;
        public Task CommitAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (store.FailCommit) throw new IOException("Synthetic commit failure"); store.State = state.Clone(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { store.mutex.Release(); return ValueTask.CompletedTask; }
    }
}
sealed class ReadPermissions : IPermissionService
{
    public bool Allowed, Fail;
    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Allowed ? ["case.read", "questionnaire.task.read"] : []);
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    { if (Fail) throw new PermissionLookupException(); return Task.FromResult(Allowed); }
}
sealed class ReadScope(Guid caseId, Guid taskId) : ICaseTaskReadScope
{
    public Task<bool> CanReadCaseAsync(ClaimsPrincipal principal, Guid id, CancellationToken ct) => Task.FromResult(id == caseId);
    public Task<bool> CanReadTaskAsync(ClaimsPrincipal principal, Guid id, Guid task, CancellationToken ct) => Task.FromResult(id == caseId && task == taskId);
}
