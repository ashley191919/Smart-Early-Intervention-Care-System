using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Authorization;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

int pass = 0, fail = 0;
void Assert(bool value) { if (!value) throw new InvalidOperationException("Contract assertion failed"); }
async Task Check(string name, Func<Task> action)
{
    try { await action(); pass++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { fail++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
}
Task Pure(string name, Action action) => Check(name, () => { action(); return Task.CompletedTask; });
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var actor = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", ReadFixture.Actor.ToString())], "synthetic"));
foreach (var (method, route, dto) in new[] {
    ("Tasks", "api/cases/{caseId:guid}/tasks", typeof(CaseTaskResponse)),
    ("Completion", "api/cases/{caseId:guid}/tasks/completion", typeof(CaseTasksCompletionResponse)) })
    await Pure("formal GET route and parameters " + method, () => {
        var m = typeof(QuestionnairesController).GetMethod(method)!;
        Assert(m.GetCustomAttribute<HttpGetAttribute>()!.Template == route);
        Assert(m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Guid), typeof(CancellationToken) }));
        Assert(dto.Assembly == typeof(QuestionnaireService).Assembly);
    });
await Pure("formal Controller retains Authorize/no-store attributes; no read policy falsely assumed", () => {
    var t = typeof(QuestionnairesController);
    Assert(t.GetCustomAttribute<AuthorizeAttribute>() is { Policy: null });
    Assert(t.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    Assert(!t.GetConstructors().Single().GetParameters().Any(p => p.ParameterType == typeof(ScopedReadAuthorization)));
});
await Check("default Case/Task scope remains Deny (not Controller integration)", async () => {
    var scope = new DenyCaseTaskReadScope();
    Assert(!await scope.CanReadCaseAsync(actor, ReadFixture.Actor, default));
    Assert(!await scope.CanReadTaskAsync(actor, ReadFixture.Actor, ReadFixture.Actor, default));
});
await Pure("Task DTO exact CLR types and nullable contract", () => {
    var ctor = typeof(CaseTaskResponse).GetConstructors().Single();
    Assert(ctor.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] {
        typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(string), typeof(string),
        typeof(string), typeof(uint), typeof(bool), typeof(string), typeof(DateTime), typeof(DateTime), typeof(DateTime?) }));
    var info = new NullabilityInfoContext();
    Assert(ctor.GetParameters().Where(p => p.ParameterType == typeof(string)).All(p => info.Create(p).ReadState == NullabilityState.NotNull));
});
await Pure("Completion DTO exact CLR types and no round/answers field", () => {
    Assert(typeof(CaseTasksCompletionResponse).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
        .SequenceEqual(new[] { typeof(Guid), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) }));
});
await Check("actual EF Store materialization, exact Task JSON and nullable timestamps", async () => {
    using var f = new ReadFixture(); f.Rows.Add(f.Row(1)); f.Rows.Add(f.Row(2, status: "SUBMITTED", role: "TEACHER"));
    var rows = await f.Service.ListTasksAsync(f.CaseId);
    Assert(rows.Count == 2 && rows[0].TaskId == f.Rows[0].TaskId && rows[0].CaseId == f.CaseId);
    Assert(rows[0].QuestionnaireVersionId == f.Rows[0].QuestionnaireVersionId && rows[0].QuestionnaireId == f.Rows[0].QuestionnaireId);
    Assert(rows[0].QuestionnaireTitle == "Synthetic questionnaire" && rows[0].VersionNumber == "1.0");
    Assert(rows[0].SubmittedAtUtc is null && rows[1].SubmittedAtUtc!.Value.Kind == DateTimeKind.Utc);
    Assert(rows.All(r => r.CreatedAtUtc.Kind == DateTimeKind.Utc && r.UpdatedAtUtc.Kind == DateTimeKind.Utc));
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(rows, jsonOptions));
    var first = json.RootElement[0];
    Assert(first.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "taskId", "caseId", "questionnaireId", "questionnaireVersionId", "questionnaireTitle", "versionNumber", "respondentRole", "assignmentRound", "isRequired", "taskStatus", "createdAtUtc", "updatedAtUtc", "submittedAtUtc" }));
    Assert(first.GetProperty("assignmentRound").ValueKind == JsonValueKind.Number && first.GetProperty("assignmentRound").GetUInt32() == 1);
    Assert(first.GetProperty("isRequired").ValueKind == JsonValueKind.True && first.GetProperty("submittedAtUtc").ValueKind == JsonValueKind.Null);
    Assert(first.GetProperty("createdAtUtc").GetString()!.EndsWith("Z", StringComparison.Ordinal));
    Assert(typeof(CaseTaskResponse).GetProperty("SubmittedAtUtc")!.PropertyType == typeof(DateTime?));
    Assert(!f.Db.ChangeTracker.Entries().Any() && f.Connection.State == ConnectionState.Closed && f.Events.Count == 0);
});
await Check("actual provider SQL orders by round/code/role/task; DTO keeps supplied provider order", async () => {
    using var f = new ReadFixture();
    f.Rows.Add(f.Row(1, 1, "SUBMITTED")); f.Rows.Add(f.Row(2, 1, role: "TEACHER")); f.Rows.Add(f.Row(3, 2, "CANCELLED"));
    var rows = await f.Service.ListTasksAsync(f.CaseId);
    var command = f.Commands.Single(c => c.Sql.Contains("FROM `case_questionnaires`", StringComparison.Ordinal));
    var order = command.Sql[(command.Sql.IndexOf("ORDER BY", StringComparison.Ordinal) + 8)..];
    Assert(Regex.Matches(order, @"`[^`]+`\.`([^`]+)`").Select(m => m.Groups[1].Value).SequenceEqual(new[] { "assignment_round", "questionnaire_code", "respondent_role", "task_id" }));
    Assert(rows.Select(r => r.TaskId).SequenceEqual(f.Rows.Select(r => r.TaskId)));
    Assert(rows.Select(r => r.TaskStatus).SequenceEqual(new[] { "SUBMITTED", "PENDING", "CANCELLED" }));
    Assert(command.Sql.Contains("INNER JOIN `questionnaire_versions`", StringComparison.Ordinal) && command.Sql.Contains("INNER JOIN `questionnaires`", StringComparison.Ordinal));
    var where = command.Sql.Split("WHERE", StringSplitOptions.None)[1].Split("ORDER BY", StringSplitOptions.None)[0];
    Assert(where.Contains("`case_id`", StringComparison.Ordinal) && !where.Contains("assignment_round", StringComparison.Ordinal) && !where.Contains("task_status", StringComparison.Ordinal));
    Assert(command.Parameters.Contains(f.CaseId));
    Assert(!command.Sql.Contains("answers_json", StringComparison.Ordinal) && !command.Sql.Contains("questionnaire_responses", StringComparison.Ordinal));
});
await Check("existing case with no tasks returns empty array and false completion", async () => {
    using var f = new ReadFixture();
    Assert(JsonSerializer.Serialize(await f.Service.ListTasksAsync(f.CaseId), jsonOptions) == "[]");
    Assert(await f.Service.CompletionAsync(f.CaseId) == new CaseTasksCompletionResponse(f.CaseId, 0, 0, 0, 0, 0, false));
});
await Check("service missing Case error is not HTTP 404 acceptance", async () => {
    using var f = new ReadFixture(); f.CaseExists = false;
    try { await f.Service.ListTasksAsync(f.CaseId); throw new InvalidOperationException("Expected error"); }
    catch (QuestionnaireOperationException e) { Assert(e.Code == "CASE_NOT_FOUND" && e.Status == 404); }
    Assert(f.Commands.Count == 1 && !f.Commands.Any(c => c.Sql.Contains("FROM `case_questionnaires`", StringComparison.Ordinal)));
});
await Check("empty Case Guid rejected before EF query", async () => {
    using var f = new ReadFixture();
    try { await f.Service.CompletionAsync(Guid.Empty); throw new InvalidOperationException("Expected error"); }
    catch (QuestionnaireOperationException e) { Assert(e.Code == "INVALID_INPUT" && e.Status == 400); }
    Assert(f.Commands.Count == 0);
});
var cases = new[] {
    ("all required submitted", new[] { (true, "SUBMITTED", 1u), (true, "SUBMITTED", 1u), (false, "PENDING", 1u) }, new[] { 3, 2, 2, 0, 0 }, true),
    ("partial submitted", new[] { (true, "SUBMITTED", 1u), (true, "PENDING", 1u) }, new[] { 2, 2, 1, 1, 0 }, false),
    ("in progress", new[] { (true, "IN_PROGRESS", 1u) }, new[] { 1, 1, 0, 0, 1 }, false),
    ("zero required", new[] { (false, "SUBMITTED", 1u) }, new[] { 1, 0, 0, 0, 0 }, false),
    ("cancelled required still counted", new[] { (true, "SUBMITTED", 1u), (true, "CANCELLED", 1u) }, new[] { 2, 2, 1, 0, 0 }, false),
    ("cancelled optional does not block", new[] { (true, "SUBMITTED", 1u), (false, "CANCELLED", 1u) }, new[] { 2, 1, 1, 0, 0 }, true),
    ("old pending blocks newer submitted round", new[] { (true, "PENDING", 1u), (true, "SUBMITTED", 2u) }, new[] { 2, 2, 1, 1, 0 }, false),
    ("all rounds submitted", new[] { (true, "SUBMITTED", 1u), (true, "SUBMITTED", 2u) }, new[] { 2, 2, 2, 0, 0 }, true)
};
foreach (var (name, input, counts, all) in cases)
    await Check("actual Service + EF Completion: " + name, async () => {
        using var f = new ReadFixture();
        foreach (var (required, state, round) in input) f.Rows.Add(f.Row(f.Rows.Count + 1, round, state, required));
        var other = f.Row(99); other.CaseId = ReadFixture.Actor; f.Rows.Add(other);
        var c = await f.Service.CompletionAsync(f.CaseId);
        Assert(c == new CaseTasksCompletionResponse(f.CaseId, counts[0], counts[1], counts[2], counts[3], counts[4], all));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(c, jsonOptions));
        Assert(json.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "caseId", "totalTaskCount", "requiredTaskCount", "requiredSubmittedCount", "requiredPendingCount", "requiredInProgressCount", "allRequiredSubmitted" }));
        Assert(json.RootElement.GetProperty("allRequiredSubmitted").GetBoolean() == all);
        Assert(f.Commands.Count == 2 && f.Events.Count == 0 && !f.Db.ChangeTracker.Entries().Any());
    });
foreach (var method in new[] { "Tasks", "Completion" })
{
    async Task<IActionResult> Call(ReadFixture f, string env, Guid? id = null, ClaimsPrincipal? principal = null) {
        var c = f.Controller(env, principal);
        return method == "Tasks" ? await c.Tasks(id ?? f.CaseId, default) : await c.Completion(id ?? f.CaseId, default);
    }
    foreach (var role in new[] { "ADMIN", "CASE_MANAGER" })
        await Check($"direct Controller {method}: {role} + Development fixture reaches real service", async () => {
            using var f = new ReadFixture(); f.Role = role;
            Assert(await Call(f, "Development") is OkObjectResult);
            Assert(f.Commands.Count == 3);
        });
    foreach (var role in new[] { "MEDICAL_STAFF", "PARENT", "TEACHER" })
        await Check($"direct Controller {method}: {role} remains forbidden", async () => {
            using var f = new ReadFixture(); f.Role = role;
            Assert(await Call(f, "Development") is ForbidResult && f.Commands.Count == 1);
        });
    foreach (var env in new[] { "IsolatedTest", "Production" })
        await Check($"direct Controller {method}: {env} stays gated before task query", async () => {
            using var f = new ReadFixture(); var r = await Call(f, env);
            Assert(r is ObjectResult { StatusCode: 403 } && f.Commands.Count == 1);
        });
    await Check($"direct Controller {method}: different Development Case stays gated", async () => {
        using var f = new ReadFixture(); Assert(await Call(f, "Development", ReadFixture.Actor) is ObjectResult { StatusCode: 403 });
        Assert(f.Commands.Count == 1);
    });
    await Check($"direct Controller {method}: inactive account denies before task query", async () => {
        using var f = new ReadFixture(); f.Status = "DISABLED";
        Assert(await Call(f, "Development") is UnauthorizedResult && f.Commands.Count == 1);
    });
    foreach (var claims in new[] { Array.Empty<Claim>(), new[] { new Claim("userId", "invalid") }, new[] { new Claim("userId", ReadFixture.Actor.ToString()), new Claim("userId", ReadFixture.Actor.ToString()) } })
        await Check($"direct Controller {method}: missing/invalid/duplicate identity denies without DB query", async () => {
            using var f = new ReadFixture();
            Assert(await Call(f, "Development", principal: new ClaimsPrincipal(new ClaimsIdentity(claims, "synthetic"))) is UnauthorizedResult && f.Commands.Count == 0);
        });
}
await Check("actual EF read failure propagates, never empty successful result", async () => {
    using var f = new ReadFixture(); f.FailRead = true;
    try { await f.Service.CompletionAsync(f.CaseId); throw new InvalidOperationException("Expected DB error"); }
    catch (SyntheticReadFailure) { Assert(f.Commands.Count == 1 && f.Events.Count == 0); }
});
await Check("task query failure after successful Case lookup does not become empty completion", async () => {
    using var f = new ReadFixture(); f.FailTaskRead = true;
    try { await f.Service.CompletionAsync(f.CaseId); throw new InvalidOperationException("Expected task error"); }
    catch (SyntheticReadFailure) { Assert(f.Commands.Count == 2 && f.Events.Count == 0 && !f.Db.ChangeTracker.Entries().Any()); }
});
await Pure("formal exception Filter maps read failure safely (not HTTP acceptance)", () => {
    var ctx = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>()) { Exception = new SyntheticReadFailure() };
    new QuestionnaireErrorsAttribute().OnException(ctx);
    Assert(ctx.ExceptionHandled && ctx.Result is ObjectResult { StatusCode: 503 });
    var text = JsonSerializer.Serialize(((ObjectResult)ctx.Result!).Value);
    Assert(text.Contains("SERVICE_UNAVAILABLE", StringComparison.Ordinal) && !text.Contains("Synthetic read failure", StringComparison.Ordinal));
});
var blocked = new[] {
    "HTTP pipeline 401/403/404, JWT/cookies, route matching, MVC serialization/options and actual endpoint invocation not exercised",
    "07 trusted Case/Task Scope and medical read Permission/Controller wiring not integrated; role gates are not formal Scope",
    "Effective round, cancellation/zero-required policy and Workflow are not approved by this cross-round basic summary",
    "Real MySQL ordering/joins/filtering, constraints and common Schema/Migration not accepted; synthetic ADO is NOT a SQL engine",
    "Safe global exception pipeline remains unimplemented/unaccepted; a known exception Filter is not a universal handler"
};
foreach (var reason in blocked) Console.WriteLine("BLOCKED " + reason);
Console.WriteLine($"RESULT: {pass} PASS, {fail} FAIL, {blocked.Length} BLOCKED; formal Controller/Service/EF + socket-free ADO only");
return fail > 0 ? 1 : 2;
