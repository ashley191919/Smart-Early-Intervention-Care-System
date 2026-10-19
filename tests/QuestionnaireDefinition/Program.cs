using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Questionnaires;

var id = Guid.NewGuid();
var form = Guid.NewGuid();
var version = Guid.NewGuid();
const string snapshot = """
{"title":"Assigned title","instructions":"Instructions","scoringDefinition":"SECRET",
"questions":[{"questionId":"q2","order":2,"text":"Second","type":"singleChoice","required":false,
"options":[{"value":"0","label":"Never","score":999}]},
{"questionId":"q1","order":1,"text":"First","options":[{"value":"1","label":"Often"}]}]}
""";
var baseline = new QuestionnaireDefinitionSource(id, form, version, "PARENT", "PENDING", form,
    "PARENT", "assigned-v1", "PUBLISHED", snapshot);
var store = new Store { Row = baseline };
var access = new Access();
var service = new QuestionnaireDefinitionService(store, access, new QuestionnaireAnswerValidator());
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", Guid.NewGuid().ToString())], "test"));
var count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine($"PASS {label}"); }
async Task Error(string code, int status, ClaimsPrincipal? caller = null)
{
    try { await service.GetForParentAsync(caller ?? principal, id); throw new Exception("Expected rejection"); }
    catch (QuestionnaireWorkflowException ex) { Check(ex.Code == code && ex.Status == status, $"{code}/{status}"); }
}
var result = await service.GetForParentAsync(principal, id);
Check(result.QuestionnaireVersionId == version && result.TaskId == id && result.QuestionnaireId == form, "Fixed task version");
Check(result.Title == "Assigned title" && result.VersionNumber == "assigned-v1" && result.Instructions == "Instructions", "Metadata");
Check(result.Questions.Select(q => q.QuestionId).SequenceEqual(["q1", "q2"]), "Question order");
Check(result.Questions.All(q => q.Type == "singleChoice" && q.RequiredOnSubmit), "Effective validator rule");
Check(result.Questions[0].SnapshotRequired is null && result.Questions[1].SnapshotRequired == false, "Legacy vs explicit required=false");
Check(result.Questions[1].Options.Single() == new QuestionnaireDefinitionOption("0", "Never"), "Option projection");
var json = JsonSerializer.Serialize(result);
Check(!json.Contains("SECRET") && !json.Contains("999") && !json.Contains("Scoring") && !json.Contains("Author") && !json.Contains("Answers"), "No sensitive fields");
store.Row = baseline with { VersionStatus = "RETIRED" };
Check((await service.GetForParentAsync(principal, id)).QuestionnaireVersionId == version, "Assigned retired version retained");
foreach (var state in new[] { "IN_PROGRESS", "SUBMITTED" })
{
    store.Row = baseline with { TaskStatus = state };
    Check((await service.GetForParentAsync(principal, id)).TaskId == id, $"Read {state}");
}
store.Calls = 0;
await Error("AUTHENTICATION_REQUIRED", 401, new ClaimsPrincipal(new ClaimsIdentity()));
Check(store.Calls == 0, "Anonymous denied before read");
// Fake adapter only: these are not Guardian/Email integration tests.
access.Allow = false;
await Error("TASK_NOT_FOUND", 404);
Check(store.Calls == 0, "Denied/revoked adapter prevents read");
access.Allow = true;
store.Row = null;
await Error("TASK_NOT_FOUND", 404);
foreach (var row in new[] { baseline with { TaskStatus = "CANCELLED" }, baseline with { RespondentRole = "TEACHER" }, baseline with { TaskId = Guid.NewGuid() } })
{ store.Row = row; await Error("TASK_NOT_FOUND", 404); }
foreach (var row in new[] { baseline with { VersionQuestionnaireId = Guid.NewGuid() }, baseline with { VersionStatus = "DRAFT" }, baseline with { VersionRole = "TEACHER" }, baseline with { DefinitionSnapshot = null }, baseline with { TaskStatus = "UNKNOWN" } })
{ store.Row = row; await Error("DEFINITION_UNAVAILABLE", 503); }
foreach (var broken in new[] { "{", "{}", snapshot.Replace("singleChoice", "freeText"), snapshot.Replace("\"q2\"", "\"q1\""), snapshot.Replace("\"required\":false", "\"required\":null"), snapshot.Replace("\"label\":\"Never\"", "\"label\":null"), snapshot.Replace("\"order\":2", "\"order\":1"), snapshot.Replace("\"order\":2", "\"order\":\"2\""), snapshot.Replace("\"title\":\"Assigned title\"", "\"title\":null"), snapshot.Replace("\"value\":\"0\",\"label\":\"Never\",\"score\":999", "\"value\":\"0\",\"label\":\"Never\"},{\"value\":\"0\",\"label\":\"Duplicate\"") })
{ store.Row = baseline with { DefinitionSnapshot = broken }; await Error("DEFINITION_UNAVAILABLE", 503); }
store.Row = baseline with { DefinitionSnapshot = snapshot.Replace("\"order\":2,", "").Replace("\"order\":1,", "") };
Check((await service.GetForParentAsync(principal, id)).Questions[0].QuestionId == "q2", "Legacy array order");
store.Row = baseline with { DefinitionSnapshot = snapshot.Replace("\"title\":", "\"respondentRole\":\"TEACHER\",\"title\":") };
await Error("DEFINITION_UNAVAILABLE", 503);
// Actual validator confirms required=false does not permit missing answers on submit.
try
{
    new QuestionnaireAnswerValidator().Validate(snapshot, new(version, "Synthetic", "2026-10-19", [new("q1", "1")], null), new(2026, 10, 19), true);
    throw new Exception("Validator unexpectedly accepted missing required=false answer");
}
catch (QuestionnaireWorkflowException) { Check(true, "Actual validator still requires every answer"); }
Console.WriteLine($"QuestionnaireDefinition: {count} passed; Fake Store/Access only; MySQL and Guardian integration NOT RUN.");

sealed class Store : IQuestionnaireDefinitionStore
{
    public QuestionnaireDefinitionSource? Row;
    public int Calls;
    public Task<QuestionnaireDefinitionSource?> FindAsync(Guid taskId, CancellationToken ct) { Calls++; return Task.FromResult(Row); }
}
sealed class Access : IParentQuestionnaireDefinitionAccess
{
    public bool Allow = true;
    public Task<bool> CanReadParentTaskAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct) => Task.FromResult(Allow);
}
