using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;

// Reuses the socket-free ADO double. No MySqlConnection or connection string exists here.
sealed class ReadFixture : IDisposable
{
    public static readonly Guid Actor = Guid.Parse("60000000-0000-4000-8000-000000000001");
    public Guid CaseId => CoreDevelopmentSeed.CaseId;
    public readonly List<string> Events = [];
    public readonly List<(string Sql, object?[] Parameters)> Commands = [];
    // These rows stand in for the database result, not an implementation of Completion.
    // They are supplied in provider order; SQL ORDER BY is verified independently.
    public readonly List<QuestionnaireTask> Rows = [];
    public string Role = "CASE_MANAGER", Status = "ACTIVE";
    public bool AccountExists = true, CaseExists = true, FailRead, FailTaskRead;
    public ApplicationDbContext Db { get; }
    public QuestionnaireService Service { get; }
    public OfflineConnection Connection { get; }
    public ReadFixture()
    {
        Connection = new OfflineConnection(Events) { FailBegin = false, FailCommit = false };
        Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(Connection, new MySqlServerVersion(new Version(8, 0, 46)))
            .AddInterceptors(new Reader(this), new NoOpen(), new NoSave()).Options);
        Service = new(new EfQuestionnaireStore(Db), TimeProvider.System);
    }
    public QuestionnairesController Controller(string environment = "Development", ClaimsPrincipal? principal = null) =>
        new(Service, Db, new EnvironmentFixture(environment)) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = principal ?? new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", Actor.ToString())], "synthetic")) } }
        };
    public QuestionnaireTask Row(int id, uint round = 1, string status = "PENDING", bool required = true, string role = "PARENT") => new() {
        TaskId = Guid.Parse($"70000000-0000-4000-8000-{id:D12}"), CaseId = CaseId,
        QuestionnaireId = CoreDevelopmentSeed.QuestionnaireId, QuestionnaireVersionId = CoreDevelopmentSeed.VersionId,
        AssignmentRound = round, IsRequired = required, RespondentRole = role, TaskStatus = status,
        CreatedAtUtc = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Unspecified),
        UpdatedAtUtc = new(2026, 11, 8, 1, 0, 0, DateTimeKind.Unspecified),
        SubmittedAtUtc = status == "SUBMITTED" ? new(2026, 11, 8, 2, 0, 0, DateTimeKind.Unspecified) : null,
        CancelledAtUtc = status == "CANCELLED" ? new(2026, 11, 8, 2, 0, 0, DateTimeKind.Unspecified) : null
    };
    public void Dispose() { Db.Dispose(); Connection.Dispose(); }

    sealed class NoOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection c, ConnectionEventData e, InterceptionResult r) => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c, ConnectionEventData e, InterceptionResult r, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(InterceptionResult.Suppress()); }
    }
    sealed class NoSave : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> r) => throw new InvalidOperationException("No writes allowed");
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default) => throw new InvalidOperationException("No writes allowed");
    }
    sealed class Reader(ReadFixture f) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (command.Connection != f.Connection || command.Connection.State != ConnectionState.Closed)
                throw new InvalidOperationException("Offline guard bypassed");
            var sql = command.CommandText;
            var parameters = command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray();
            f.Commands.Add((sql, parameters));
            if (f.FailRead || f.FailTaskRead && sql.Contains("FROM `case_questionnaires`", StringComparison.Ordinal)) throw new SyntheticReadFailure();
            var table = new DataTable();
            if (sql.StartsWith("SELECT EXISTS", StringComparison.Ordinal)) {
                if (!sql.Contains("FROM `cases`", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected scalar query");
                table.Columns.Add("exists", typeof(bool)); table.Rows.Add(f.CaseExists && parameters.Contains(f.CaseId));
            }
            else if (sql.Contains("FROM `users`", StringComparison.Ordinal)) {
                if (!sql.Contains("`Status`", StringComparison.Ordinal) && !sql.Contains("`status`", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected account projection");
                table.Columns.Add("status", typeof(string)); table.Columns.Add("name", typeof(string));
                if (f.AccountExists && parameters.Contains(Actor)) table.Rows.Add(f.Status, f.Role);
            }
            else if (sql.Contains("FROM `case_questionnaires`", StringComparison.Ordinal)) {
                var select = sql[..sql.IndexOf("\nFROM", StringComparison.Ordinal)].Replace("\r", "");
                var selected = Regex.Matches(select, @"`[^`]+`\.`([^`]+)`").Select(m => m.Groups[1].Value).ToArray();
                var entity = f.Db.Model.FindEntityType(typeof(QuestionnaireTask))!;
                var properties = entity.GetProperties().ToDictionary(p => p.GetColumnName()!);
                for (var i = 0; i < selected.Length; i++) {
                    var type = selected[i] is "title" or "version_number" ? typeof(string) : Nullable.GetUnderlyingType(properties[selected[i]].ClrType) ?? properties[selected[i]].ClrType;
                    table.Columns.Add("field" + i, type);
                }
                foreach (var row in f.Rows.Where(r => parameters.Contains(r.CaseId)))
                    table.Rows.Add(selected.Select(c => c switch {
                        "title" => (object)"Synthetic questionnaire", "version_number" => "1.0",
                        _ => properties[c].PropertyInfo!.GetValue(row) ?? DBNull.Value
                    }).ToArray());
            }
            else throw new InvalidOperationException("Query outside closed-world fixture");
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) => throw new InvalidOperationException("Writes forbidden");
    }
}
sealed class SyntheticReadFailure : DbException { public SyntheticReadFailure() : base("Synthetic read failure") { } }
sealed class EnvironmentFixture(string name) : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "OfflineMedicalRead";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
