using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{ try { await action(); throw new Exception("Expected " + typeof(T).Name + " " + name); } catch (T) { Check(true, name); } }
async Task Code(Func<Task> action, string code)
{ try { await action(); throw new Exception("Expected " + code); } catch (QuestionnaireWorkflowException e) { Check(e.Code == code, code); } }

try
{
using (var f = new Fixture())
{
    await using (var tx = await f.Store.OpenAsync(default))
    {
        Check(ReferenceEquals(tx.DbContext, f.Db), "exact scoped context");
        Check((await tx.LockTaskAsync(f.Task.TaskId, default))!.CaseId == f.Task.CaseId, "task materialized by actual EF");
        Check((await tx.FindVersionAsync(f.Version.QuestionnaireVersionId, default))!.RespondentRole == "PARENT", "version projection");
        Check(await tx.FindDraftAsync(Guid.NewGuid(), default) is null, "other task draft missing");
        Check(await tx.FindResponseAsync(Guid.NewGuid(), default) is null, "other task response missing");
        Check(await tx.FindByKeyAsync(Guid.NewGuid(), Guid.NewGuid(), default) is null, "key author filter");
        await tx.CommitAsync(default);
    }
    Check(f.Events.IndexOf("begin") < f.Events.IndexOf("read:case_questionnaires") &&
        f.Events.IndexOf("commit") < f.Events.IndexOf("dispose"), "begin/query/commit/dispose order");
    Check(!f.Events.Contains("rollback") && !f.Db.ChangeTracker.Entries().Any(), "committed disposal clears tracker");
    Check(f.Commands.All(c => c.Parameters.Length > 0 && !c.Sql.Contains(f.Task.TaskId.ToString())), "values parameterized, not interpolated into SQL");
    Check(f.Commands.Single(c => c.Sql.Contains("FOR UPDATE")).Parameters.Contains(f.Task.TaskId.ToString()), "task lock parameter");
    Check(f.Commands.Any(c => c.Sql.Contains("`parent_author_user_id` =") && c.Sql.Contains("`idempotency_key` =")), "key query includes author and key predicates");
}
using (var f = new Fixture())
{
    f.Db.QuestionnaireTasks.Add(f.Task);
    await Throws<InvalidOperationException>(() => f.Store.OpenAsync(default), "dirty context refused before transaction");
    Check(!f.Events.Contains("begin"), "no begin for dirty context");
    f.Db.ChangeTracker.Clear();
    await using var external = await f.Db.Database.BeginTransactionAsync();
    await Throws<InvalidOperationException>(() => f.Store.OpenAsync(default), "nested transaction refused");
}
using (var f = new Fixture())
{
    await using (var tx = await f.Store.OpenAsync(default))
    {
        await tx.LockTaskAsync(f.Task.TaskId, default);
        tx.AddDraft(f.Draft());
        await tx.CommitAsync(default);
    }
    Check(f.Events.IndexOf("write") < f.Events.IndexOf("commit"), "SaveChanges before commit");
    Check(f.Commands.Any(c => c.Sql.Contains("INSERT INTO `questionnaire_drafts`") && c.Parameters.Contains(f.UserId.ToString())), "trusted author inserted by EF");
}
using (var f = new Fixture())
{
    f.DraftRow = f.Draft();
    await using var tx = await f.Store.OpenAsync(default);
    Check((await tx.FindDraftAsync(f.Task.TaskId, default))!.ParentAuthorUserId == f.UserId, "draft entity materialized by EF");
    Check(await tx.FindDraftAsync(Guid.NewGuid(), default) is null, "draft query cannot select another task");
    f.ResponseRow = f.Response(Guid.NewGuid());
    Check((await tx.FindResponseAsync(f.Task.TaskId, default))!.ParentAuthorUserId == f.UserId, "response entity materialized by EF");
    Check(await tx.FindResponseAsync(Guid.NewGuid(), default) is null, "response query filters task");
    Check(await tx.FindByKeyAsync(Guid.NewGuid(), f.ResponseRow.IdempotencyKey, default) is null, "same key other author not selected");
    Check((await tx.FindByKeyAsync(f.UserId, f.ResponseRow.IdempotencyKey, default))!.ResponseId == f.ResponseRow.ResponseId, "same author plus key selects response");
}
foreach (var conflict in new[] { false, true })
{
    using var f = new Fixture(); f.DraftRow = f.Draft();
    if (conflict) f.Fault = "zero-row";
    var gateway = f.Gateway(new Access(f, "success"));
    var save = new SaveQuestionnaireDraftRequest(f.Version.QuestionnaireVersionId, 1, null, null, [], null);
    if (conflict) await Code(() => gateway.SaveDraftAsync(f.Principal, f.Task.TaskId, save), "DRAFT_CONFLICT");
    else Check((await gateway.SaveDraftAsync(f.Principal, f.Task.TaskId, save)).Revision == 2, "actual EF revision update");
    var update = f.Commands.Single(c => c.Sql.Contains("UPDATE `questionnaire_drafts`"));
    Check(Regex.IsMatch(update.Sql, "WHERE.*`task_id`.*`revision`", RegexOptions.Singleline), "UPDATE WHERE includes task and original revision");
    Check(update.Parameters.Contains("1") && update.Parameters.Contains("2"), "revision original and next are parameters");
    Check(f.Events.Contains("commit") == !conflict, "zero affected row never commits");
}
using (var f = new Fixture())
{
    var key = Guid.NewGuid(); f.ResponseRow = f.Response(key);
    f.Task.TaskStatus = "SUBMITTED"; f.Task.SubmittedAtUtc = f.ResponseRow.SubmittedAtUtc;
    var receipt = await f.Gateway(new Access(f, "success")).SubmitAsync(f.Principal, f.Task.TaskId, key, f.Submission());
    Check(receipt.Replayed && receipt.ResponseId == f.ResponseRow.ResponseId, "idempotent replay using actual EF reads");
    Check(!f.Events.Contains("write"), "EF replay issues no DML");
    f.Commands.Clear();
    await Code(() => f.Gateway(new Access(f, "success")).SubmitAsync(f.Principal, f.Task.TaskId, key, f.Submission() with { Observation = "changed" }), "IDEMPOTENCY_CONFLICT");
}
using (var f = new Fixture())
{
    var key = Guid.NewGuid(); f.ResponseRow = f.Response(key); f.ResponseRow.TaskId = Guid.NewGuid();
    await Code(() => f.Gateway(new Access(f, "success")).SubmitAsync(f.Principal, f.Task.TaskId, key, f.Submission()), "IDEMPOTENCY_CONFLICT");
    Check(!f.Events.Contains("write") && !f.Events.Contains("commit"), "EF key reuse across tasks never writes");
}
using (var f = new Fixture())
{
    f.ResponseRow = f.Response(Guid.NewGuid()); f.ResponseRow.ParentAuthorUserId = Guid.NewGuid();
    f.Task.TaskStatus = "SUBMITTED"; f.Task.SubmittedAtUtc = f.ResponseRow.SubmittedAtUtc;
    await Code(() => f.Gateway(new Access(f, "success")).GetReceiptAsync(f.Principal, f.Task.TaskId), "TASK_NOT_FOUND");
    Check(!f.Events.Contains("write") && !f.Events.Contains("commit"), "another author receipt refused by gateway over EF");
}
using (var f = new Fixture())
{
    f.DraftRow = f.Draft();
    await Code(() => f.Gateway(new Access(f, "success")).SaveDraftAsync(f.Principal, f.Task.TaskId,
        new(f.Version.QuestionnaireVersionId, 0, null, null, [], null)), "DRAFT_CONFLICT");
    Check(!f.Events.Contains("write") && !f.Events.Contains("commit"), "stale revision refused before DML");
}
using (var f = new Fixture())
{
    await Code(() => f.Gateway(new DenyParentTaskAccessAdapter()).GetDraftAsync(f.Principal, f.Task.TaskId), "PARENT_ACCESS_NOT_INTEGRATED");
    Check(!f.Events.Any(e => e.StartsWith("read:") || e is "write" or "commit"), "missing adapter refuses before EF business queries");
}
using (var f = new Fixture())
{
    await Code(() => f.Gateway(new Access(f, "revoke-before-commit")).SubmitAsync(f.Principal, f.Task.TaskId, Guid.NewGuid(), f.Submission()), "TASK_NOT_FOUND");
    Check(!f.Events.Contains("write") && f.Events.Contains("rollback") && !f.Db.ChangeTracker.Entries().Any(), "submit revalidation failure disposes transaction without saving staged response");
}
using (var f = new Fixture())
{
    f.Connection.FailBegin = true;
    await Throws<IOException>(() => f.Store.OpenAsync(default), "begin failure propagates");
    Check(f.Db.Database.CurrentTransaction is null && !f.Db.ChangeTracker.Entries().Any(), "begin failure leaves no current transaction or tracked data");
    f.Connection.FailBegin = false;
    await using var retry = await f.Store.OpenAsync(default);
    Check(ReferenceEquals(retry.DbContext, f.Db), "begin failure can retry in clean context");
}
using (var f = new Fixture())
{
    f.Fault = "command";
    await using (var tx = await f.Store.OpenAsync(default))
    {
        tx.AddDraft(f.Draft());
        await Throws<DbUpdateException>(() => tx.CommitAsync(default), "actual EF command failure propagated");
    }
    Check(!f.Events.Contains("commit") && f.Events.Contains("rollback") && !f.Db.ChangeTracker.Entries().Any(), "command error rolls back and clears tracker");
}
foreach (var fault in new[] { "save", "concurrency", "duplicate", "commit" })
{
    using var f = new Fixture();
    f.Fault = fault; f.Connection.FailCommit = fault == "commit";
    await using (var tx = await f.Store.OpenAsync(default))
    {
        await tx.LockTaskAsync(f.Task.TaskId, default); tx.AddDraft(f.Draft());
        if (fault == "concurrency") await Code(() => tx.CommitAsync(default), "DRAFT_CONFLICT");
        else if (fault == "duplicate") await Code(() => tx.CommitAsync(default), "WRITE_CONFLICT");
        else await Throws<IOException>(() => tx.CommitAsync(default), "fault propagates " + fault);
    }
    Check(f.Events.Contains("rollback") && f.Events.Contains("dispose"), "uncommitted transaction disposed with simulated rollback " + fault);
    Check(!f.Db.ChangeTracker.Entries().Any(), "tracker clean after " + fault);
    if (fault != "commit") Check(!f.Events.Contains("commit"), "no commit after save failure " + fault);
    f.Fault = null; f.Connection.FailCommit = false;
    await using var retry = await f.Store.OpenAsync(default);
    Check(await retry.FindDraftAsync(f.Task.TaskId, default) is null, "failed tracked insert not returned on next transaction " + fault);
}
using (var f = new Fixture())
{
    await using var tx = await f.Store.OpenAsync(default);
    await f.Db.Database.CurrentTransaction!.DisposeAsync();
    await using var replacement = await f.Db.Database.BeginTransactionAsync();
    await Throws<InvalidOperationException>(() => tx.CommitAsync(default), "replaced transaction refused");
    Check(!f.Events.Contains("write") && !f.Events.Contains("commit"), "replacement never saves or commits original gateway work");
}
// Gateway uses the REAL EF store with an access test double. No Guardian/Consent policy is invented.
foreach (var scenario in new[] { "deny", "wrong-case", "wrong-user", "wrong-organization", "revoke-before-commit", "success" })
{
    using var f = new Fixture(); var adapter = new Access(f, scenario);
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", f.UserId.ToString())], "synthetic-test"));
    var gateway = new ParentQuestionnaireGateway(f.Store, adapter, new(), TimeProvider.System, new(f.Version.QuestionnaireVersionId));
    if (scenario == "success")
    {
        await gateway.SubmitAsync(principal, f.Task.TaskId, Guid.NewGuid(), new(f.Version.QuestionnaireVersionId, "Synthetic parent", "2026-10-01", [new("q", "1")], null));
        Check(f.Commands.Any(c => c.Sql.Contains("INSERT INTO `questionnaire_responses`")) &&
            f.Commands.Any(c => c.Sql.Contains("UPDATE `case_questionnaires`")), "response and Task updates use EF SaveChanges");
        Check(f.Commands.Any(c => c.Sql.Contains("INSERT INTO `questionnaire_responses`") && c.Parameters.Contains(f.UserId.ToString())) &&
            f.Commands.Any(c => c.Sql.Contains("UPDATE `case_questionnaires`") && c.Parameters.Contains("SUBMITTED")), "response author and SUBMITTED status parameterized");
        Check(f.Events.Count(e => e == "commit") == 1 && adapter.SameContext, "one commit and same adapter context");
    }
    else
    {
        await Code(() => gateway.SaveDraftAsync(principal, f.Task.TaskId, new(f.Version.QuestionnaireVersionId, 0, null, null, [], null)), "TASK_NOT_FOUND");
        Check(!f.Events.Contains("write") && !f.Events.Contains("commit"), "denied gateway writes/commits nothing " + scenario);
        if (scenario != "revoke-before-commit") Check(!f.Events.Any(e => e is "read:questionnaire_drafts" or "read:questionnaire_responses"), "scope refusal before sensitive reads " + scenario);
    }
}
Console.WriteLine($"EfParentStore: {passed} checks PASS; actual EF/Pomelo translation + offline ADO/interceptors, NOT MySQL.");
return 0;
}
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }

sealed record CommandLog(string Sql, string[] Parameters);
sealed class Fixture : IDisposable
{
    public List<string> Events { get; } = [];
    public List<CommandLog> Commands { get; } = [];
    public OfflineConnection Connection { get; }
    public ApplicationDbContext Db { get; }
    public EfParentQuestionnaireStore Store { get; }
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid OrganizationId { get; } = Guid.NewGuid();
    public QuestionnaireTask Task { get; }
    public QuestionnaireVersion Version { get; }
    public QuestionnaireDraft? DraftRow;
    public QuestionnaireResponse? ResponseRow;
    public string? Fault;
    public Fixture()
    {
        Version = new() { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = Guid.NewGuid(), RespondentRole = "PARENT", VersionNumber = "synthetic", VersionStatus = "PUBLISHED", DefinitionSnapshot = """{"questions":[{"questionId":"q","options":[{"value":"1"}]}]}""" };
        Task = new() { TaskId = Guid.NewGuid(), CaseId = Guid.NewGuid(), QuestionnaireId = Version.QuestionnaireId, QuestionnaireVersionId = Version.QuestionnaireVersionId, AssignmentRound = 1, RespondentRole = "PARENT", TaskStatus = "PENDING" };
        Connection = new(Events);
        Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseMySql(Connection, new MySqlServerVersion(new Version(8,0,46)))
            .AddInterceptors(new CommandsInterceptor(this), new SaveInterceptor(this)).Options);
        Store = new(Db);
    }
    public QuestionnaireDraft Draft() => new() { TaskId = Task.TaskId, QuestionnaireVersionId = Version.QuestionnaireVersionId, ParentAuthorUserId = UserId, Revision = 1, AnswersJson = "[]", UpdatedAtUtc = DateTime.UtcNow };
    public ClaimsPrincipal Principal => new(new ClaimsIdentity([new Claim("userId", UserId.ToString())], "synthetic-test"));
    public QuestionnaireSubmission Submission() => new(Version.QuestionnaireVersionId, "Synthetic parent", "2026-10-01", [new("q", "1")], null);
    public ParentQuestionnaireGateway Gateway(IParentTaskAccessAdapter adapter) => new(Store, adapter, new(), TimeProvider.System, new(Version.QuestionnaireVersionId));
    public QuestionnaireResponse Response(Guid key)
    {
        var content = new QuestionnaireAnswerValidator().Validate(Version.DefinitionSnapshot, Submission(), new DateOnly(2026,10,21), true);
        return new() { ResponseId = Guid.NewGuid(), TaskId = Task.TaskId, QuestionnaireVersionId = Version.QuestionnaireVersionId,
            ParentAuthorUserId = UserId, RespondentName = content.RespondentName!, FilledOn = content.FilledOn!.Value,
            AnswersJson = content.AnswersJson, IdempotencyKey = key, PayloadHash = content.SubmissionHash(Task.TaskId, Version.QuestionnaireVersionId), SubmittedAtUtc = DateTime.UtcNow };
    }
    public void Dispose() { Db.Dispose(); Connection.Dispose(); }
}
sealed class SaveInterceptor(Fixture f) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (f.Fault == "save") throw new IOException("Synthetic save failure");
        if (f.Fault == "concurrency") throw new DbUpdateConcurrencyException("Synthetic conflict");
        if (f.Fault == "duplicate")
        {
            // MySqlException has no public error-code constructor. Use its offline private constructor.
            var ctor = typeof(MySqlException).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(c => c.GetParameters().Any(p => p.ParameterType == typeof(MySqlErrorCode)));
            var args = ctor.GetParameters().Select(p => p.ParameterType == typeof(MySqlErrorCode) ? (object)(MySqlErrorCode)1062 : p.ParameterType == typeof(string) ? "synthetic" : p.ParameterType == typeof(int) ? 1062 : null).ToArray();
            throw new DbUpdateException("Synthetic duplicate", (Exception)ctor.Invoke(args));
        }
        return ValueTask.FromResult(result);
    }
}
sealed class CommandsInterceptor(Fixture f) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = command.CommandText;
        var values = command.Parameters.Cast<DbParameter>().Select(p => p.Value?.ToString() ?? "NULL").ToArray();
        f.Commands.Add(new(sql, values));
        if (!ReferenceEquals(command.Connection, f.Connection) || command.Transaction is null ||
            !ReferenceEquals(command.Transaction, f.Db.Database.CurrentTransaction?.GetDbTransaction())) throw new Exception("wrong command transaction/connection");
        if (sql.Contains("INSERT INTO `", StringComparison.Ordinal) || sql.Contains("UPDATE `", StringComparison.Ordinal))
        {
            f.Events.Add("write");
            if (f.Fault == "command") throw new OfflineCommandFailure();
            var data = new DataSet();
            var count = Regex.Matches(sql, "SELECT ROW_COUNT\\(\\)").Count;
            if (count == 0) data.Tables.Add(new DataTable());
            for (var i = 0; i < count; i++) { var t = new DataTable(); t.Columns.Add("ROW_COUNT()", typeof(int)); t.Rows.Add(f.Fault == "zero-row" ? 0 : 1); data.Tables.Add(t); }
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(new AffectedReader(data.CreateDataReader(), Regex.Matches(sql, "INSERT INTO `|UPDATE `").Count)));
        }
        object? row; Type type; string table;
        if (sql.Contains("FROM case_questionnaires")) { table = "case_questionnaires"; type = typeof(QuestionnaireTask); row = values.Contains(f.Task.TaskId.ToString()) ? f.Task : null; }
        else if (sql.Contains("FROM `questionnaire_versions`")) { table = "questionnaire_versions"; type = typeof(QuestionnaireVersion); row = values.Contains(f.Version.QuestionnaireVersionId.ToString()) ? f.Version : null; }
        else if (sql.Contains("FROM `questionnaire_drafts`")) { table = "questionnaire_drafts"; type = typeof(QuestionnaireDraft); row = f.DraftRow is { } d && values.Contains(d.TaskId.ToString()) ? d : null; }
        else if (sql.Contains("FROM `questionnaire_responses`"))
        {
            table = "questionnaire_responses"; type = typeof(QuestionnaireResponse);
            var byKey = sql.Contains("WHERE") && sql[(sql.IndexOf("WHERE", StringComparison.Ordinal))..].Contains("`idempotency_key`");
            row = f.ResponseRow is { } r && (byKey ? values.Contains(r.ParentAuthorUserId!.Value.ToString()) && values.Contains(r.IdempotencyKey.ToString()) : values.Contains(r.TaskId.ToString())) ? r : null;
        }
        else throw new Exception("Unhandled SQL shape: " + sql);
        f.Events.Add("read:" + table);
        var predicate = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        var keyQuery = table == "questionnaire_responses" && predicate.Contains("`idempotency_key`");
        if (keyQuery ? values.Length != 2 || !predicate.Contains("`parent_author_user_id`") : values.Length != 1 || !predicate.Contains(table == "questionnaire_versions" ? "questionnaire_version_id" : "task_id"))
            throw new Exception("Unexpected query scope predicate");
        var entity = f.Db.Model.FindEntityType(type)!;
        var store = StoreObjectIdentifier.Table(table, null);
        var properties = entity.GetProperties().ToDictionary(p => p.GetColumnName(store)!);
        var select = sql[..sql.IndexOf("FROM", StringComparison.Ordinal)];
        var selected = Regex.Matches(select, "`[a-zA-Z0-9_]+`\\.`([a-zA-Z0-9_]+)`").Select(m => m.Groups[1].Value).ToArray();
        if (selected.Length == 0) selected = properties.Keys.ToArray();
        var resultTable = new DataTable();
        foreach (var column in selected)
        {
            var clr = Nullable.GetUnderlyingType(properties[column].ClrType) ?? properties[column].ClrType;
            resultTable.Columns.Add(column, clr);
        }
        if (row is not null) resultTable.Rows.Add(selected.Select(c =>
        {
            var v = properties[c].PropertyInfo!.GetValue(row);
            return v ?? DBNull.Value;
        }).ToArray());
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(resultTable.CreateDataReader()));
    }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.StartsWith("SAVEPOINT", StringComparison.Ordinal) && !command.CommandText.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal) && !command.CommandText.StartsWith("ROLLBACK TO SAVEPOINT", StringComparison.Ordinal)) throw new Exception("Unhandled nonquery");
        f.Events.Add("savepoint"); return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }
}
sealed class OfflineCommandFailure : DbException { public OfflineCommandFailure() : base("Synthetic command failure") { } }
sealed class Access(Fixture f, string scenario) : IParentTaskAccessAdapter
{
    public bool SameContext;
    public Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction tx, ClaimsPrincipal principal, Guid taskId, CancellationToken ct)
    {
        SameContext = ReferenceEquals(tx.DbContext, f.Db) && f.Db.Database.CurrentTransaction is not null;
        if (!SameContext || scenario is "deny" or "wrong-organization") throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Unavailable", 404);
        return System.Threading.Tasks.Task.FromResult(new ParentTaskAccess(scenario == "wrong-user" ? Guid.NewGuid() : f.UserId, f.OrganizationId,
            scenario == "wrong-case" ? Guid.NewGuid() : f.Task.CaseId, taskId, Guid.NewGuid(), Guid.NewGuid()));
    }
    public Task RevalidateAsync(IParentQuestionnaireTransaction tx, ParentTaskAccess access, CancellationToken ct)
    {
        if (!ReferenceEquals(tx.DbContext, f.Db) || scenario == "revoke-before-commit") throw new QuestionnaireWorkflowException("TASK_NOT_FOUND", "Unavailable", 404);
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
