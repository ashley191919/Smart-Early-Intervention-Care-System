using System.Data;
using System.Data.Common;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

int passed = 0, failed = 0;
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
async Task Check(string name, Func<Task> work)
{
    try { await work(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}"); }
}
async Task Throws<T>(Func<Task> work) where T : Exception
{
    try { await work(); throw new Exception("Expected failure"); } catch (T) { }
}
// TEST-ONLY wrapper decision specification, not the real 11 wrapper or an authorization adapter.
bool Visible(ParentTaskAuthorMetadata m, Guid actor) => actor != Guid.Empty &&
    new[] { m.Draft, m.Response }.All(r => !r.Exists ||
        (r.ParentAuthorUserId is Guid author && author != Guid.Empty && author == actor && !r.HasTeacherGrant));

await Check("two absent rows remain absent and only satisfy the author predicate", async () => {
    using var f = new Fixture(); var m = await f.Read();
    Assert(m == new ParentTaskAuthorMetadata(new(false, null, false), new(false, null, false)) && Visible(m, f.Actor));
    Assert(f.Commands.Count == 2 && f.Db.Database.CurrentTransaction is null);
});
foreach (var which in new[] { "draft", "response", "both" })
foreach (var kind in new[] { "own", "null", "empty", "other", "teacher", "teacher-empty-grant", "mixed-author-teacher" })
    await Check($"{which}: preserves {kind} metadata and supports conservative wrapper", async () => {
        using var f = new Fixture();
        Guid? author = kind == "null" || kind.StartsWith("teacher") ? null : kind == "empty" ? Guid.Empty : kind == "other" ? f.Other : f.Actor;
        bool teacher = kind.Contains("teacher");
        var grant = teacher ? (kind == "teacher-empty-grant" ? Guid.Empty : f.Other) : (Guid?)null;
        if (which is "draft" or "both") f.Draft = new() { TaskId = f.TaskId, ParentAuthorUserId = author, LastSavedByGrantId = grant };
        if (which is "response" or "both") f.Response = new() { TaskId = f.TaskId, ParentAuthorUserId = author, SubmittedByGrantId = grant };
        var m = await f.Read();
        foreach (var r in which == "both" ? new[] { m.Draft, m.Response } : new[] { which == "draft" ? m.Draft : m.Response })
            Assert(r.Exists && r.ParentAuthorUserId == author && r.HasTeacherGrant == teacher);
        Assert(Visible(m, f.Actor) == (kind == "own"));
        Assert(!f.Db.ChangeTracker.Entries().Any());
    });
foreach (var invalidDraft in new[] { true, false })
    await Check($"both rows must match, invalid Draft={invalidDraft}", async () => {
        using var f = new Fixture();
        f.Draft = new() { TaskId = f.TaskId, ParentAuthorUserId = invalidDraft ? f.Other : f.Actor };
        f.Response = new() { TaskId = f.TaskId, ParentAuthorUserId = invalidDraft ? f.Actor : f.Other };
        var m = await f.Read(); Assert(m.Draft.Exists && m.Response.Exists && !Visible(m, f.Actor));
    });
await Check("other Task rows are not selected", async () => {
    using var f = new Fixture(); f.Draft = new() { TaskId = f.Other, ParentAuthorUserId = f.Actor };
    f.Response = new() { TaskId = f.Other, ParentAuthorUserId = f.Actor };
    var m = await f.Read(); Assert(!m.Draft.Exists && !m.Response.Exists);
});
foreach (var fault in new[] { "draft", "response" })
    await Check($"{fault} query failure propagates, never returns absent metadata", async () => {
        using var f = new Fixture(); f.Fault = fault;
        await Throws<IOException>(() => f.Read()); Assert(f.Commands.Count == (fault == "draft" ? 1 : 2));
    });
foreach (var duplicate in new[] { "draft", "response" })
    await Check($"unexpected duplicate {duplicate} rows fail rather than choosing one", async () => {
        using var f = new Fixture(); f.Duplicate = duplicate;
        await Throws<InvalidOperationException>(() => f.Read());
    });
await Check("cancelled query fails before ADO access", async () => {
    using var f = new Fixture(); using var c = new CancellationTokenSource(); c.Cancel();
    await Throws<OperationCanceledException>(() => f.Read(c.Token)); Assert(f.Commands.Count == 0);
});
await Check("empty Task ID fails without querying", async () => {
    using var f = new Fixture(); await Throws<ArgumentException>(() => f.Reader.ReadAsync(f.Db, Guid.Empty, default)); Assert(f.Commands.Count == 0);
});
await Check("null context fails without fallback context", async () => {
    using var f = new Fixture(); await Throws<ArgumentNullException>(() => f.Reader.ReadAsync(null!, f.TaskId, default)); Assert(f.Commands.Count == 0);
});
await Check("SQL selects only author and grant presence, scoped by parameterized Task", async () => {
    using var f = new Fixture(); await f.Read();
    Assert(f.Commands.Count == 2 && f.Commands.All(x => x.Contains("IS NOT NULL")));
    Assert(!f.Db.ChangeTracker.Entries().Any() && !f.Events.Contains("begin") && !f.Events.Contains("commit"));
});
await Check("existing transaction and pending tracked changes remain owned by caller", async () => {
    using var f = new Fixture();
    var pending = new QuestionnaireDraft { TaskId = f.TaskId, ParentAuthorUserId = f.Other, AnswersJson = "[]" };
    f.Db.QuestionnaireDrafts.Add(pending);
    f.Draft = new() { TaskId = f.TaskId, ParentAuthorUserId = f.Actor };
    await using var tx = await f.Db.Database.BeginTransactionAsync();
    var m = await f.Read();
    Assert(m.Draft.ParentAuthorUserId == f.Actor && ReferenceEquals(f.Db.Database.CurrentTransaction, tx));
    Assert(f.Db.Entry(pending).State == EntityState.Added && f.Db.ChangeTracker.Entries().Count() == 1);
    Assert(f.Events.Count(x => x == "begin") == 1 && !f.Events.Contains("commit") && !f.Events.Contains("rollback"));
    Assert(f.SameTransaction && !f.ConnectionDisposed);
});
await Check("reader is not a Gateway, authorization adapter or store", () => {
    Assert(typeof(EfParentAuthorMetadataReader).GetInterfaces().SequenceEqual(new[] { typeof(IParentAuthorMetadataReader) }));
    Assert(typeof(EfParentAuthorMetadataReader).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Length == 0);
    return Task.CompletedTask;
});
Console.WriteLine("BLOCKED actual 11 wrapper/Guardian/Consent/Stamp and concurrent authorization integration");
Console.WriteLine("BLOCKED approved common Migration and actual MySQL query/locking/isolation acceptance");
Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL, 2 BLOCKED; EF/Pomelo + offline ADO only; no socket/database");
return failed > 0 ? 1 : 2;

sealed class Fixture : IDisposable
{
    public Guid TaskId { get; } = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public Guid Actor { get; } = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public Guid Other { get; } = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public List<string> Events { get; } = [];
    public List<string> Commands { get; } = [];
    public OfflineConnection Connection { get; }
    public ApplicationDbContext Db { get; }
    public IParentAuthorMetadataReader Reader { get; } = new EfParentAuthorMetadataReader();
    public QuestionnaireDraft? Draft;
    public QuestionnaireResponse? Response;
    public string? Fault, Duplicate;
    public bool SameTransaction = true, ConnectionDisposed;
    public Fixture()
    {
        Connection = new(Events) { FailBegin = false, FailCommit = false };
        Db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(Connection, new MySqlServerVersion(new Version(8, 0, 46)))
            .AddInterceptors(new ProjectionInterceptor(this)).Options);
    }
    public Task<ParentTaskAuthorMetadata> Read(CancellationToken ct = default) => Reader.ReadAsync(Db, TaskId, ct);
    public void Dispose() { Db.Dispose(); Connection.Dispose(); ConnectionDisposed = true; }
}

sealed class ProjectionInterceptor(Fixture f) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = command.CommandText; f.Commands.Add(sql);
        if (!ReferenceEquals(eventData.Context, f.Db) || !ReferenceEquals(command.Connection, f.Connection)) throw new Exception("Wrong context");
        f.SameTransaction &= ReferenceEquals(command.Transaction, f.Db.Database.CurrentTransaction?.GetDbTransaction());
        var draft = sql.Contains("FROM `questionnaire_drafts`");
        if (!draft && !sql.Contains("FROM `questionnaire_responses`")) throw new Exception("Unexpected table");
        var grant = draft ? "last_saved_by_grant_id" : "submitted_by_grant_id";
        var select = sql[..sql.IndexOf("FROM", StringComparison.Ordinal)];
        var columns = System.Text.RegularExpressions.Regex.Matches(select, "`[^`]+`\\.`([^`]+)`")
            .Select(m => m.Groups[1].Value).ToArray();
        if (select.Contains('*') || !select.Contains("`parent_author_user_id`") || !select.Contains($"`{grant}` IS NOT NULL") ||
            !columns.SequenceEqual(new[] { "parent_author_user_id", grant }) ||
            new[] { "answers_json", "respondent_name", "payload_hash", "observation", "respondent_relationship", "hash_version", "revision" }.Any(sql.Contains))
            throw new Exception("Sensitive/unexpected projection");
        var predicate = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        if (!predicate.Contains("`task_id`") || command.Parameters.Count != 1 || sql.Contains(f.TaskId.ToString()))
            throw new Exception("Unscoped/unparameterized query");
        var part = draft ? "draft" : "response";
        if (f.Fault == part) throw new IOException("Synthetic query failure");
        var t = new DataTable(); t.Columns.Add("parent_author_user_id", typeof(Guid)); t.Columns.Add("HasTeacherGrant", typeof(bool));
        var matches = command.Parameters[0].Value?.ToString() == f.TaskId.ToString();
        if (matches && (draft ? f.Draft?.TaskId == f.TaskId : f.Response?.TaskId == f.TaskId))
        {
            Guid? author = draft ? f.Draft!.ParentAuthorUserId : f.Response!.ParentAuthorUserId;
            bool teacher = draft ? f.Draft!.LastSavedByGrantId.HasValue : f.Response!.SubmittedByGrantId.HasValue;
            t.Rows.Add((object?)author ?? DBNull.Value, teacher);
        }
        if (f.Duplicate == part) { t.Rows.Clear(); t.Rows.Add(f.Actor, false); t.Rows.Add(f.Actor, false); }
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(t.CreateDataReader()));
    }
}
