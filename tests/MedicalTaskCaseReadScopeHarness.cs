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
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;

try {
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
services.AddLogging();
services.AddRouting();
services.AddPermissionAuthorization();
services.AddAuthorization(options => options.AddFirstBatchPermissionPolicies());
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
rows.CaseFailure = null;
var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
var readService = new QuestionnaireService(new EfQuestionnaireStore(db), TimeProvider.System);
rows.CaseVisible = true;
var injected = ActivatorUtilities.CreateInstance<QuestionnairesController>(scope.ServiceProvider,
    readService, new OfflineEnvironment("Development"));
injected.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
Check(await injected.Tasks(rows.CaseId, default) is OkObjectResult,
    "DI constructor activation resolves optional auth dependencies from the existing request scope");
QuestionnairesController Controller(string env = "Development", ClaimsPrincipal? user = null, bool wired = true) =>
    new(readService, db, new OfflineEnvironment(env), wired ? authorization : null, wired ? adapter : null) {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user ?? principal } }
    };
foreach (var completion in new[] { false, true })
{
    async Task<IActionResult> Read(QuestionnairesController controller, Guid? id = null) => completion
        ? await controller.Completion(id ?? rows.CaseId, default)
        : await controller.Tasks(id ?? rows.CaseId, default);
    var name = completion ? "Completion" : "Tasks";
    rows.CaseVisible = true;
    before = rows.Requests.Count;
    var allowed = await Read(Controller());
    Check(allowed is OkObjectResult && rows.Requests.Count > before,
        name + " real Controller/PermissionService/CaseAccess/EF read allows scoped Development fixture");
    Check(completion
        ? ((OkObjectResult)allowed).Value is EarlyInterventionCare.Api.DTOs.Questionnaires.CaseTasksCompletionResponse { TotalTaskCount: 0, AllRequiredSubmitted: false }
        : ((OkObjectResult)allowed).Value is IReadOnlyList<EarlyInterventionCare.Api.DTOs.Questionnaires.CaseTaskResponse> { Count: 0 },
        name + " real read preserves empty-task contract");
    rows.Permission = false; before = rows.Requests.Count;
    Check(await Read(Controller()) is ObjectResult { StatusCode: 403 } &&
        !rows.Requests.Skip(before).Any(r => rows.Sql[r.Index].Contains("FROM `cases`")),
        name + " missing task permission denies before CaseScope or task read");
    rows.Permission = true;
    foreach (var reason in new[] { "cross-organization", "missing Case" }) {
        rows.CaseVisible = false; before = rows.Requests.Count;
        var deniedRead = await Read(Controller());
        Check(deniedRead is NotFoundObjectResult { StatusCode: 404 } &&
            System.Text.Json.JsonSerializer.Serialize(((ObjectResult)deniedRead).Value).Contains("CASE_NOT_FOUND") &&
            !rows.Sql.Skip(before).Any(s => s.Contains("FROM `case_questionnaires`")),
            name + " synthetic " + reason + " scope=false masks existence before task read");
    }
    rows.CaseVisible = true;
    foreach (var user in new[] { new ClaimsPrincipal(), new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", rows.UserId.ToString())])),
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", "invalid")], "synthetic")) }) {
        before = rows.Requests.Count;
        var deniedRead = await Read(Controller(user: user));
        Check(deniedRead is UnauthorizedObjectResult && rows.Requests.Count == before &&
            System.Text.Json.JsonSerializer.Serialize(((ObjectResult)deniedRead).Value).Contains("UNAUTHENTICATED"),
            name + " untrusted/malformed identity is 401 without DB access");
    }
    rows.ActorActive = false;
    Check(await Read(Controller()) is UnauthorizedObjectResult, name + " disabled account 401");
    rows.ActorActive = true; rows.OrganizationActive = false;
    Check(await Read(Controller()) is UnauthorizedObjectResult, name + " real CaseAccess inactive organization 401 translated safely");
    rows.OrganizationActive = true;
    foreach (var role in new[] { "MEDICAL_STAFF", "PARENT" }) {
        rows.Role = role;
        Check(await Read(Controller()) is ForbidResult, name + " original role restriction: " + role);
    }
    rows.Role = "CASE_MANAGER";
    foreach (var env in new[] { "IsolatedTest", "Production" })
        Check(await Read(Controller(env)) is ObjectResult { StatusCode: 403 }, name + " original environment gate: " + env);
    Check(await Read(Controller(), Guid.NewGuid()) is ObjectResult { StatusCode: 403 }, name + " other Development Case still gated");
    Check(await Read(Controller(wired: false)) is ObjectResult { StatusCode: 503 }, name + " missing manual DI fails closed");
    rows.CaseFailure = new ScopeQueryException();
    await Throws<ScopeQueryException>(() => Read(Controller()));
    Check(true, name + " scope DB failure propagates, never empty success");
    rows.CaseFailure = null;
}
rows.Role = "MEDICAL_STAFF";
Check(await Controller().Assign(rows.CaseId, new(), default) is ForbidResult, "POST original MEDICAL_STAFF gate unchanged");
rows.Role = "CASE_MANAGER";
Check(await Controller("Production").Assign(rows.CaseId, new(), default) is ObjectResult { StatusCode: 403 }, "POST original environment gate unchanged");
rows.Permission = false; rows.CaseVisible = false;
var postValidation = await Throws<QuestionnaireOperationException>(() => Controller().Assign(rows.CaseId, new(), default));
Check(postValidation.Code == "INVALID_INPUT", "POST retains old input validation without adopting GET permission/scope");
Console.WriteLine("BLOCKED real JWT/Cookie HTTP pipeline, cross-org MySQL engine and common Schema acceptance; offline rows are not a SQL engine");
Console.WriteLine($"PASS: {checks} medical task case scope checks with offline ADO.");
return 2;
}
catch (Exception error) { Console.WriteLine("FAIL " + error.GetType().Name + ": " + error.Message); return 1; }

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
    public Guid CaseId { get; } = CoreDevelopmentSeed.CaseId;
    private Guid OrganizationId { get; } = Guid.NewGuid();
    public bool CaseVisible { get; set; } = true;
    public ScopeQueryException? CaseFailure { get; set; }
    public bool Permission { get; set; } = true;
    public bool ActorActive { get; set; } = true;
    public bool OrganizationActive { get; set; } = true;
    public string Role { get; set; } = "CASE_MANAGER";
    public List<string> Sql { get; } = [];
    public List<(DbContext? Context, CancellationToken Token, int Index)> Requests { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Connection?.State != ConnectionState.Closed)
            throw new InvalidOperationException("The offline connection must remain closed.");
        Requests.Add((eventData.Context, cancellationToken, Sql.Count));
        Sql.Add(command.CommandText);
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
                ["name"] = Role
            };
            var selectEnd = sql.IndexOf("FROM", StringComparison.Ordinal);
            var columns = Regex.Matches(sql[..selectEnd], @"`[^`]+`\.`(?<column>[^`]+)`")
                .Select(match => match.Groups["column"].Value).ToArray();
            foreach (var column in columns)
                table.Columns.Add(column, column.EndsWith("_id", StringComparison.Ordinal) ? typeof(Guid) :
                    column.EndsWith("_utc", StringComparison.Ordinal) ? typeof(DateTime) : typeof(string));
            if (ActorActive && OrganizationActive) table.Rows.Add(columns.Select(column => row[column] ?? DBNull.Value).ToArray());
        }
        else if (sql.Contains("FROM `users`") && sql.Contains("JOIN `permissions`")) {
            table.Columns.Add("status", typeof(string)); table.Columns.Add("name", typeof(string));
            if (Permission && ActorActive) table.Rows.Add("ACTIVE", "questionnaire.task.read");
        }
        else if (sql.Contains("FROM `users`") && sql.Contains("JOIN `roles`")) {
            table.Columns.Add("status", typeof(string)); table.Columns.Add("name", typeof(string));
            if (ActorActive) table.Rows.Add("ACTIVE", Role);
        }
        else if (sql.Contains("FROM `case_questionnaires`")) {
            if (!parameters.Contains(CaseId) || !sql.Contains("WHERE")) throw new InvalidOperationException("Unscoped Task query");
            var columns = Regex.Matches(sql[..sql.IndexOf("FROM", StringComparison.Ordinal)], @"`[^`]+`\.`([^`]+)`").Select(m => m.Groups[1].Value).ToArray();
            var entity = eventData.Context!.Model.FindEntityType(typeof(EarlyInterventionCare.Api.Data.Entities.QuestionnaireTask))!;
            var properties = entity.GetProperties().ToDictionary(p => p.GetColumnName()!);
            for (int i = 0; i < columns.Length; i++) table.Columns.Add("c" + i, columns[i] is "title" or "version_number" ? typeof(string) : Nullable.GetUnderlyingType(properties[columns[i]].ClrType) ?? properties[columns[i]].ClrType);
        }
        else if (sql.Contains("FROM `cases`") && !sql.Contains("`organization_id`")) {
            table.Columns.Add("exists", typeof(bool)); table.Rows.Add(CaseVisible && parameters.Contains(CaseId));
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

sealed class OfflineEnvironment(string name) : IWebHostEnvironment {
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "G3Offline";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
