using System.Reflection;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

var passed = 0;
var failed = 0;
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new InvalidOperationException("Assertion failed");
        passed++; Console.WriteLine($"PASS {name}");
    }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
using (var db = new ApplicationDbContextFactory().CreateDbContext([]))
{
    // Metadata/ChangeTracker only. The existing factory blocks opening a DB connection.
    var model = db.GetService<IDesignTimeModel>().Model;
    foreach (var type in new[] { typeof(QuestionnaireDraft), typeof(QuestionnaireResponse) })
    {
        var entity = model.FindEntityType(type)!;
        var property = entity.FindProperty("RespondentRelationship")!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        Check($"{type.Name} C# nullable string", () => property.ClrType == typeof(string) &&
            new NullabilityInfoContext().Create(type.GetProperty("RespondentRelationship")!).ReadState == NullabilityState.Nullable);
        Check($"{type.Name} database NULL allowed", () => property.IsNullable);
        Check($"{type.Name} column name", () => property.GetColumnName(table) == "respondent_relationship");
        Check($"{type.Name} varchar(32) and max length", () => property.GetColumnType() == "varchar(32)" && property.GetMaxLength() == 32);
        Check($"{type.Name} no default/default SQL/computed SQL", () =>
            property.FindAnnotation(RelationalAnnotationNames.DefaultValue) == null &&
            property.GetDefaultValueSql() == null && property.GetComputedColumnSql() == null);
        Check($"{type.Name} no generated value", () => property.ValueGenerated == ValueGenerated.Never);
        Check($"{type.Name} not a key/FK/index or new concurrency token", () =>
            !property.IsConcurrencyToken && !entity.GetKeys().Any(k => k.Properties.Contains(property)) &&
            !entity.GetForeignKeys().Any(k => k.Properties.Contains(property)) && !entity.GetIndexes().Any(k => k.Properties.Contains(property)));
    }
    var draft = new QuestionnaireDraft { TaskId = Guid.NewGuid(), QuestionnaireVersionId = Guid.NewGuid(), AnswersJson = "[]" };
    var response = new QuestionnaireResponse { ResponseId = Guid.NewGuid(), TaskId = Guid.NewGuid(), QuestionnaireVersionId = Guid.NewGuid(),
        RespondentName = "synthetic", AnswersJson = "[]", PayloadHash = new byte[32] };
    Check("new entities have unknown relationship", () => draft.RespondentRelationship == null && response.RespondentRelationship == null);
    db.AddRange(draft, response);
    db.ChangeTracker.DetectChanges();
    Check("tracking does not fill relationship", () => draft.RespondentRelationship == null && response.RespondentRelationship == null);
    draft.RespondentRelationship = "FATHER"; response.RespondentRelationship = "MOTHER";
    db.ChangeTracker.DetectChanges();
    Check("explicit values retained without inference", () => draft.RespondentRelationship == "FATHER" && response.RespondentRelationship == "MOTHER");
    draft.RespondentRelationship = null; response.RespondentRelationship = null;
    db.ChangeTracker.DetectChanges();
    Check("explicit null retained", () => draft.RespondentRelationship == null && response.RespondentRelationship == null);
    Check("legacy JSON without relationship remains null", () =>
        JsonSerializer.Deserialize<QuestionnaireDraft>("{}")!.RespondentRelationship == null &&
        JsonSerializer.Deserialize<QuestionnaireResponse>("{}")!.RespondentRelationship == null);
    Check("existing draft revision remains concurrency token", () => model.FindEntityType(typeof(QuestionnaireDraft))!.FindProperty("Revision")!.IsConcurrencyToken);
    Check("existing response task uniqueness retained", () => model.FindEntityType(typeof(QuestionnaireResponse))!.GetIndexes().Any(i =>
        i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TaskId" })));
    Check("response query translation includes new nullable column", () => db.QuestionnaireResponses.ToQueryString().Contains("respondent_relationship", StringComparison.Ordinal));
    Check("draft query translation includes new nullable column", () => db.QuestionnaireDrafts.ToQueryString().Contains("respondent_relationship", StringComparison.Ordinal));
    var responseEntity = model.FindEntityType(typeof(QuestionnaireResponse))!;
    var hashVersion = responseEntity.FindProperty("HashVersion")!;
    var responseTable = StoreObjectIdentifier.Table(responseEntity.GetTableName()!, responseEntity.GetSchema());
    Check("HashVersion nullable C# string and EF property", () => hashVersion.ClrType == typeof(string) && hashVersion.IsNullable &&
        new NullabilityInfoContext().Create(typeof(QuestionnaireResponse).GetProperty("HashVersion")!).ReadState == NullabilityState.Nullable);
    Check("HashVersion column varchar(64) and length", () => hashVersion.GetColumnName(responseTable) == "hash_version" &&
        hashVersion.GetColumnType() == "varchar(64)" && hashVersion.GetMaxLength() == 64);
    Check("HashVersion no default/computed/generated value", () =>
        hashVersion.FindAnnotation(RelationalAnnotationNames.DefaultValue) == null && hashVersion.GetDefaultValueSql() == null &&
        hashVersion.GetComputedColumnSql() == null && hashVersion.ValueGenerated == ValueGenerated.Never);
    Check("HashVersion not key/index/FK/concurrency token", () => !hashVersion.IsConcurrencyToken &&
        !responseEntity.GetKeys().Any(k => k.Properties.Contains(hashVersion)) &&
        !responseEntity.GetIndexes().Any(k => k.Properties.Contains(hashVersion)) &&
        !responseEntity.GetForeignKeys().Any(k => k.Properties.Contains(hashVersion)));
    Check("HashVersion does not set column collation", () => hashVersion.FindAnnotation(RelationalAnnotationNames.Collation) == null);
    Check("PayloadHash stays required binary(32)", () => responseEntity.FindProperty("PayloadHash") is { IsNullable: false } hash &&
        hash.GetColumnType() == "binary(32)");
    Check("all three response unique indexes retained", () => responseEntity.GetIndexes().Where(i => i.IsUnique)
        .Select(i => i.GetDatabaseName()).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(
            new[] { "uq_responses_grant_key", "uq_responses_parent_key", "uq_responses_task" }));
    Check("Draft has no HashVersion", () => typeof(QuestionnaireDraft).GetProperty("HashVersion") == null &&
        model.FindEntityType(typeof(QuestionnaireDraft))!.FindProperty("HashVersion") == null);
    Check("new Response has unknown HashVersion", () => new QuestionnaireResponse().HashVersion == null);
    Check("tracked legacy response HashVersion stays null", () => response.HashVersion == null);
    Check("legacy JSON missing HashVersion stays null", () => JsonSerializer.Deserialize<QuestionnaireResponse>("{}")!.HashVersion == null);
    var originalHash = response.PayloadHash.ToArray();
    response.HashVersion = ParentCanonicalSubmissionEncoder.FormatVersion;
    db.ChangeTracker.DetectChanges();
    Check("explicit v1 value retained with original hash", () => response.HashVersion == "earlycare.parent-submit.canonical/v1" &&
        response.PayloadHash.SequenceEqual(originalHash));
    response.HashVersion = null;
    db.ChangeTracker.DetectChanges();
    Check("clearing version keeps unknown and does not rewrite hash", () => response.HashVersion == null && response.PayloadHash.SequenceEqual(originalHash));
    Check("response SQL translation includes hash_version", () => db.QuestionnaireResponses.ToQueryString().Contains("hash_version", StringComparison.Ordinal));
    // Never SaveChanges, EnsureCreated, migrate, execute queries, or open connections.
}

// Exercise the unchanged real teacher service with a memory-only store, not another engine.
try
{
    var fixture = new TeacherMemoryStore();
    var clock = new FixedClock();
    var service = new TeacherQuestionnaireWorkflowService(fixture, new(), clock, new InMemoryAuditLogService(clock));
    var initial = await service.GetDraftAsync(TeacherMemoryStore.Token, fixture.Task.TaskId);
    Check("teacher reads empty draft without relationship input", () => initial.Revision == 0);
    var input = new SaveQuestionnaireDraftRequest(fixture.Version.QuestionnaireVersionId, 0, null, null, [new("Q1", "0")], null);
    var saved = await service.SaveDraftAsync(TeacherMemoryStore.Token, fixture.Task.TaskId, input, "synthetic-draft");
    Check("teacher creates draft with null relationship", () => saved.Revision == 1 && fixture.Draft?.RespondentRelationship == null);
    await service.SaveDraftAsync(TeacherMemoryStore.Token, fixture.Task.TaskId, input with { ExpectedRevision = 1 }, "synthetic-update");
    Check("teacher updates draft without filling relationship", () => fixture.Draft?.Revision == 2 && fixture.Draft.RespondentRelationship == null);
    var reread = await service.GetDraftAsync(TeacherMemoryStore.Token, fixture.Task.TaskId);
    Check("teacher reloads original answers", () => reread.Revision == 2 && reread.Answers.Single().OptionValue == "0");
    var submission = new QuestionnaireSubmission(fixture.Version.QuestionnaireVersionId, "synthetic teacher", "2026-10-30", [new("Q1", "0")], null);
    var key = Guid.NewGuid();
    var receipt = await service.SubmitAsync(TeacherMemoryStore.Token, fixture.Task.TaskId, key, submission, "synthetic-submit");
    Check("teacher submits without relationship and leaves null", () => !receipt.Replayed && fixture.Response is not null &&
        fixture.Response.RespondentRelationship == null && fixture.Task.TaskStatus == "SUBMITTED");
    var expected = new QuestionnaireAnswerValidator().Validate(fixture.Version.DefinitionSnapshot, submission, new(2026, 10, 30), true)
        .SubmissionHash(fixture.Task.TaskId, fixture.Version.QuestionnaireVersionId);
    Check("teacher still uses original shared hash", () => fixture.Response!.PayloadHash.SequenceEqual(expected));
    Check("teacher submit does not infer HashVersion", () => fixture.Response!.HashVersion == null);
    var again = await service.SubmitAsync(TeacherMemoryStore.Token, fixture.Task.TaskId, key, submission, "synthetic-replay");
    Check("teacher identical retry keeps original receipt/null", () => again.Replayed && again.ResponseId == receipt.ResponseId && fixture.Response!.RespondentRelationship == null);
    Check("teacher replay preserves null version and original hash", () => fixture.Response!.HashVersion == null && fixture.Response.PayloadHash.SequenceEqual(expected));
    var read = await service.GetReceiptAsync(TeacherMemoryStore.Token, fixture.Task.TaskId);
    Check("teacher receipt reload unchanged", () => read.ResponseId == receipt.ResponseId && fixture.Draft!.RespondentRelationship == null);
    Check("teacher receipt read leaves version unknown", () => fixture.Response!.HashVersion == null);
}
catch (Exception e) { Check($"teacher compatibility unexpected {e.GetType().Name}: {e.Message}", () => false); }

Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL; offline metadata/tracking and memory-only teacher service; no DB");
return failed == 0 ? 0 : 1;

sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
}

// Minimal test fixture only: no DbContext/connection/socket, no durable/rollback claims.
sealed class TeacherMemoryStore : ITeacherQuestionnaireStore, ITeacherQuestionnaireTransaction
{
    public const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public TeacherGrant? Grant { get; } = new() { GrantId = Guid.NewGuid(), CaseId = CoreDevelopmentSeed.CaseId,
        IsDevelopment = true, RespondentRole = "TEACHER", GrantStatus = "ACTIVE" };
    public TeacherSession? Session { get; }
    public QuestionnaireVersion Version { get; } = new() { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = Guid.NewGuid(),
        RespondentRole = "TEACHER", VersionStatus = "PUBLISHED", VersionNumber = "synthetic-1",
        DefinitionSnapshot = """{"questions":[{"questionId":"Q1","options":[{"value":"0"}]}]}""" };
    public QuestionnaireTask Task { get; }
    public IReadOnlyList<TeacherTaskEntry> Tasks { get; }
    public QuestionnaireDraft? Draft;
    public QuestionnaireResponse? Response;
    public TeacherMemoryStore()
    {
        Session = new() { GrantId = Grant!.GrantId, SessionStatus = "ACTIVE", SessionExpiresAtUtc = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc) };
        Task = new() { TaskId = Guid.NewGuid(), CaseId = Grant.CaseId, QuestionnaireId = Version.QuestionnaireId,
            QuestionnaireVersionId = Version.QuestionnaireVersionId, RespondentRole = "TEACHER", TaskStatus = "PENDING" };
        Tasks = [new(new() { GrantId = Grant.GrantId, TaskId = Task.TaskId, CaseId = Task.CaseId, RespondentRole = "TEACHER" }, Task)];
    }
    public Task<ITeacherQuestionnaireTransaction?> OpenAsync(byte[] hash, CancellationToken ct) =>
        System.Threading.Tasks.Task.FromResult<ITeacherQuestionnaireTransaction?>(hash.SequenceEqual(SHA256.HashData(Encoding.ASCII.GetBytes(Token))) ? this : null);
    public Task<QuestionnaireVersion?> FindVersionAsync(Guid id, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(id == Version.QuestionnaireVersionId ? Version : null);
    public Task<QuestionnaireDraft?> FindDraftAsync(Guid id, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(Draft?.TaskId == id ? Draft : null);
    public Task<bool> HasResponseAsync(Guid id, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(Response?.TaskId == id);
    public Task<QuestionnaireResponse?> FindByKeyAsync(Guid grant, Guid key, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(Response?.SubmittedByGrantId == grant && Response.IdempotencyKey == key ? Response : null);
    public Task<QuestionnaireReceipt?> FindReceiptAsync(Guid grant, Guid task, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(
        Response?.SubmittedByGrantId == grant && Response.TaskId == task ? new QuestionnaireReceipt(Response.ResponseId, Response.TaskId,
            Response.QuestionnaireVersionId, Response.SubmittedAtUtc, Grant!.GrantStatus, false) : null);
    public void AddDraft(QuestionnaireDraft draft) => Draft = draft;
    public void AddResponse(QuestionnaireResponse response) => Response = response;
    public Task RevokeSessionsAsync(DateTime now, CancellationToken ct) { Session!.SessionStatus = "REVOKED"; return System.Threading.Tasks.Task.CompletedTask; }
    public Task CommitAsync(CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
