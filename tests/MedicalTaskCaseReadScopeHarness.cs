using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}

static async Task<T> Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T error) { return error; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

var source = new DirectoryInfo(AppContext.BaseDirectory);
while (source is not null && !File.Exists(Path.Combine(source.FullName, "EarlyInterventionCare.Api", "Program.cs")))
    source = source.Parent;
if (source is null) throw new InvalidOperationException("Run from a checkout with the API source.");
var program = await File.ReadAllLinesAsync(Path.Combine(source.FullName, "EarlyInterventionCare.Api", "Program.cs"));
Check(program.Count(line => line.Trim() == "builder.Services.AddScoped<CaseAccessService>();") == 1 &&
    program.Count(line => line.Trim() == "builder.Services.AddScoped<IMedicalTaskCaseReadScope, CaseAccessMedicalTaskCaseReadScope>();") == 1,
    "API Program declares both Scoped registrations exactly once");

var rows = new ScopeRows();
var services = new ServiceCollection();
services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql("Server=invalid.invalid;Database=offline_scope;User=offline;Pooling=False;",
        new MySqlServerVersion(new Version(8, 0, 46)))
    .AddInterceptors(new NoNetwork(), rows));
services.AddScoped<CaseAccessService>();
services.AddScoped<IMedicalTaskCaseReadScope, CaseAccessMedicalTaskCaseReadScope>();
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true
});
await using var scope = provider.CreateAsyncScope();
await using var otherScope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var caseAccess = scope.ServiceProvider.GetRequiredService<CaseAccessService>();
var adapter = scope.ServiceProvider.GetRequiredService<IMedicalTaskCaseReadScope>();
Check(adapter is CaseAccessMedicalTaskCaseReadScope &&
    ReferenceEquals(adapter, scope.ServiceProvider.GetRequiredService<IMedicalTaskCaseReadScope>()) &&
    ReferenceEquals(caseAccess, scope.ServiceProvider.GetRequiredService<CaseAccessService>()),
    "validated DI resolves the adapter and CaseAccess within a request scope");
Check(!ReferenceEquals(adapter, otherScope.ServiceProvider.GetRequiredService<IMedicalTaskCaseReadScope>()) &&
    !ReferenceEquals(caseAccess, otherScope.ServiceProvider.GetRequiredService<CaseAccessService>()) &&
    !ReferenceEquals(db, otherScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()),
    "a separate scope receives separate adapter, CaseAccess and DbContext instances");
Check(db.Model.FindEntityType(typeof(User))!.GetIndexes()
        .Single(index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(User.NormalizedEmail))
        .IsUnique == false, "normalized email index stays non-unique");

var principal = new ClaimsPrincipal(new ClaimsIdentity(
    [new Claim("userId", rows.UserId.ToString("D")), new Claim("role", "PARENT")], "synthetic"));
using var cancellation = new CancellationTokenSource();
Check(await adapter.CanReadCaseAsync(principal, rows.CaseId, cancellation.Token),
    "CaseAccess returns true for the allowed case using the persisted medical role");
Check(rows.Requests.Count == 2 && rows.Requests.All(request =>
        request.Token == cancellation.Token && ReferenceEquals(request.Context, db)),
    "adapter forwards the cancellation token and queries use the scoped DbContext");
rows.CaseVisible = false;
Check(!await adapter.CanReadCaseAsync(principal, rows.CaseId, cancellation.Token),
    "adapter returns CaseAccess false unchanged");

var before = rows.Requests.Count;
var denied = await Throws<CaseAccessException>(() =>
    adapter.CanReadCaseAsync(new ClaimsPrincipal(), rows.CaseId, CancellationToken.None));
Check(denied.Status == 401 && denied.Code == "UNAUTHENTICATED" && rows.Requests.Count == before,
    "CaseAccess identity exception propagates without a query");
rows.CaseFailure = new ScopeQueryException();
var failure = await Throws<ScopeQueryException>(() =>
    adapter.CanReadCaseAsync(principal, rows.CaseId, cancellation.Token));
Check(ReferenceEquals(rows.CaseFailure, failure), "query failure propagates as the original exception");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
await Throws<OperationCanceledException>(() => adapter.CanReadCaseAsync(principal, rows.CaseId, cancelled.Token));
Check(db.Database.GetDbConnection().State == ConnectionState.Closed,
    "cancelled calls propagate and the connection remains closed");
Console.WriteLine($"PASS: {checks} medical task case scope checks with offline ADO.");

sealed class NoNetwork : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result) => throw new InvalidOperationException("Synchronous connection opening is forbidden.");

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(InterceptionResult.Suppress());
    }
}

sealed class ScopeQueryException() : DbException("Synthetic case lookup failure.");

sealed class ScopeRows : DbCommandInterceptor
{
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid CaseId { get; } = Guid.NewGuid();
    private Guid OrganizationId { get; } = Guid.NewGuid();
    public bool CaseVisible { get; set; } = true;
    public ScopeQueryException? CaseFailure { get; set; }
    public List<(DbContext? Context, CancellationToken Token)> Requests { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Connection?.State != ConnectionState.Closed)
            throw new InvalidOperationException("The offline connection must remain closed.");
        Requests.Add((eventData.Context, cancellationToken));
        var sql = command.CommandText;
        var parameters = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray();
        var table = new DataTable();
        if (sql.Contains("FROM `users`") && sql.Contains("JOIN `organizations`") && parameters.Contains(UserId))
        {
            var row = new Dictionary<string, object?>
            {
                ["user_id"] = UserId, ["role_id"] = Guid.NewGuid(), ["organization_id"] = OrganizationId,
                ["username"] = "fixture", ["password_hash"] = "unused", ["status"] = "ACTIVE",
                ["created_at_utc"] = DateTime.UnixEpoch, ["updated_at_utc"] = DateTime.UnixEpoch,
                ["last_login_at_utc"] = null, ["phone"] = null, ["phone_verified_at_utc"] = null,
                ["email"] = null, ["normalized_email"] = null, ["email_verified_at_utc"] = null,
                ["name"] = "CASE_MANAGER"
            };
            var selectEnd = sql.IndexOf("FROM", StringComparison.Ordinal);
            var columns = Regex.Matches(sql[..selectEnd], @"`[^`]+`\.`(?<column>[^`]+)`")
                .Select(match => match.Groups["column"].Value).ToArray();
            foreach (var column in columns)
                table.Columns.Add(column, column.EndsWith("_id", StringComparison.Ordinal) ? typeof(Guid) :
                    column.EndsWith("_utc", StringComparison.Ordinal) ? typeof(DateTime) : typeof(string));
            table.Rows.Add(columns.Select(column => row[column] ?? DBNull.Value).ToArray());
        }
        else if (sql.Contains("FROM `cases`") && sql.Contains("`organization_id`") &&
            sql.Contains("`case_id`") && parameters.Contains(OrganizationId) && parameters.Contains(CaseId))
        {
            if (CaseFailure is not null) throw CaseFailure;
            table.Columns.Add("exists", typeof(bool));
            table.Rows.Add(CaseVisible);
        }
        else throw new InvalidOperationException("Unexpected offline query.");
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Writes are forbidden in this harness.");
}
