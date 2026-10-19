using System.Net;
using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Services.Authentication;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// Invoked after real Email OTP -> JWT validation -> invitation redemption in CaseGuardianHarness.
// SQLite is disposable; no SMTP, User Secrets or production database access.
internal static class ParentDefinitionIntegrationChecks
{
    public static async Task<int> RunAsync(HttpClient client, DbContextOptions<ApplicationDbContext> options,
        Guid parentId, Guid caseId, Guid guardianId, JwtTokenService tokens)
    {
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Definition integration: " + label); count++; }
        await using var db = new ApplicationDbContext(options);
        var child = await db.Cases.AsNoTracking().SingleAsync(c => c.CaseId == caseId);
        var form = new Questionnaire { QuestionnaireId = Guid.NewGuid(), QuestionnaireCode = "DEFINITION_HTTP", Title = "Catalog title" };
        const string snapshot = """
        {"title":"Assigned parent form","instructions":"Read carefully","respondentRole":"PARENT",
         "sourceReference":"SECRET_SOURCE","questions":[
           {"questionId":"q2","order":2,"text":"Second question","type":"singleChoice","required":false,"options":[{"value":"0","label":"Never","score":999}]},
           {"questionId":"q1","order":1,"text":"First question","type":"singleChoice","required":true,"options":[{"value":"1","label":"Sometimes","score":888}]}]}
        """;
        var assigned = new QuestionnaireVersion { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = form.QuestionnaireId,
            RespondentRole = "PARENT", VersionNumber = "assigned-v1", VersionStatus = "RETIRED", DefinitionSnapshot = snapshot, ScoringDefinition = "SECRET_SCORING" };
        var latest = new QuestionnaireVersion { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = form.QuestionnaireId,
            RespondentRole = "PARENT", VersionNumber = "latest-v2", VersionStatus = "PUBLISHED", DefinitionSnapshot = snapshot.Replace("Assigned parent form", "LATEST_DO_NOT_USE") };
        var teacherVersion = new QuestionnaireVersion { QuestionnaireVersionId = Guid.NewGuid(), QuestionnaireId = form.QuestionnaireId,
            RespondentRole = "TEACHER", VersionNumber = "teacher-v1", VersionStatus = "PUBLISHED", DefinitionSnapshot = snapshot.Replace("PARENT", "TEACHER") };
        var foreignCase = new CaseRecord { CaseId = Guid.NewGuid(), OrganizationId = child.OrganizationId,
            CaseCode = "DEFINITION_FOREIGN", ChildName = "Foreign child", BirthDate = new(2020, 1, 1),
            NationalId = "A123456789", Address = "Synthetic address", CaseStatus = "NEW", Sex = "UNKNOWN" };
        QuestionnaireTask TaskFor(Guid id, Guid versionId, string role, uint round = 1) => new() {
            TaskId = Guid.NewGuid(), CaseId = id, QuestionnaireId = form.QuestionnaireId,
            QuestionnaireVersionId = versionId, RespondentRole = role, AssignmentRound = round, TaskStatus = "PENDING" };
        var task = TaskFor(caseId, assigned.QuestionnaireVersionId, "PARENT");
        var foreignTask = TaskFor(foreignCase.CaseId, assigned.QuestionnaireVersionId, "PARENT");
        var teacherTask = TaskFor(caseId, teacherVersion.QuestionnaireVersionId, "TEACHER");
        db.Questionnaires.Add(form); db.QuestionnaireVersions.AddRange(assigned, latest, teacherVersion);
        db.Cases.Add(foreignCase); db.QuestionnaireTasks.AddRange(task, foreignTask, teacherTask); await db.SaveChangesAsync();
        var path = $"/api/parent/tasks/{task.TaskId}/questionnaire";
        var originalToken = client.DefaultRequestHeaders.Authorization;
        async Task Reject(HttpStatusCode status, string? code = "TASK_NOT_FOUND", string? uri = null)
        {
            using var response = await client.GetAsync(uri ?? path);
            var body = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}");
            Check(!body.Contains("Assigned parent form") && !body.Contains("SECRET") && !body.Contains("ScoringDefinition"), "Denied response hides definition and internals");
            if (code is not null)
            {
                using var document = JsonDocument.Parse(body);
                Check(document.RootElement.GetProperty("code").GetString() == code &&
                    !string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()), "Stable code and traceId");
                Check(response.Headers.CacheControl?.NoStore == true, "Denied response is not cached");
            }
        }
        using (var response = await client.GetAsync(path))
        {
            var body = await response.Content.ReadAsStringAsync(); using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Check(response.IsSuccessStatusCode && response.Headers.CacheControl?.NoStore == true, "Email JWT + real EF adapter no-store success");
            Check(root.GetProperty("questionnaireVersionId").GetGuid() == assigned.QuestionnaireVersionId && root.GetProperty("versionNumber").GetString() == "assigned-v1", "Fixed RETIRED version, not latest");
            Check(root.GetProperty("title").GetString() == "Assigned parent form" && !body.Contains("LATEST_DO_NOT_USE"), "Assigned snapshot title");
            var questions = root.GetProperty("questions");
            Check(questions.GetArrayLength() == 2 && questions[0].GetProperty("questionId").GetString() == "q1", "All questions ordered");
            Check(!questions[1].GetProperty("snapshotRequired").GetBoolean() && questions[1].GetProperty("requiredOnSubmit").GetBoolean(), "Snapshot/effective required remain distinct");
            Check(questions[0].GetProperty("options")[0].GetProperty("label").GetString() == "Sometimes", "Option text");
            Check(!body.Contains("SECRET") && !body.Contains("999") && !body.Contains("888") && !body.Contains("answers") && !body.Contains("author") && !body.Contains("scor", StringComparison.OrdinalIgnoreCase), "No answers, authors, source or scores");
            Check(root.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[]{"taskId","questionnaireId","questionnaireVersionId","title","versionNumber","instructions","questions"}.Order()), "Exact shared response contract");
        }
        using (var list = await client.GetAsync($"/api/parent/cases/{caseId}/tasks"))
        {
            using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Check(list.IsSuccessStatusCode && body.RootElement.EnumerateArray().Any(t => t.GetProperty("taskId").GetGuid() == task.TaskId), "Existing Task List unchanged");
            Check(body.RootElement.EnumerateArray().All(t => t.GetProperty("respondentRole").GetString() == "PARENT"), "Task List excludes teacher tasks");
        }
        await Reject(HttpStatusCode.NotFound, uri: $"/api/parent/tasks/{foreignTask.TaskId}/questionnaire?userId={parentId}&caseId={caseId}&questionnaireVersionId={latest.QuestionnaireVersionId}");
        await Reject(HttpStatusCode.NotFound, uri: $"/api/parent/tasks/{teacherTask.TaskId}/questionnaire");
        await Reject(HttpStatusCode.NotFound, uri: $"/api/parent/tasks/{Guid.NewGuid()}/questionnaire");
        await Reject(HttpStatusCode.NotFound, uri: "/api/parent/tasks/00000000-0000-0000-0000-000000000000/questionnaire");
        client.DefaultRequestHeaders.Authorization = null; await Reject(HttpStatusCode.Unauthorized, null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-signature"); await Reject(HttpStatusCode.Unauthorized, null);
        var medicalId = await (from u in db.Users join r in db.Roles on u.RoleId equals r.Id where r.Name == "CASE_MANAGER" select u.Id).FirstAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.CreateToken(medicalId, "forged-parent-role", "PARENT").AccessToken);
        await Reject(HttpStatusCode.NotFound); // Current DB role, not JWT role alone.
        client.DefaultRequestHeaders.Authorization = originalToken;
        foreach (var status in new[]{"IN_PROGRESS", "SUBMITTED"})
        {
            await db.QuestionnaireTasks.Where(t => t.TaskId == task.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskStatus, status));
            Check((await client.GetAsync(path)).IsSuccessStatusCode, "Readable " + status);
        }
        await db.QuestionnaireTasks.Where(t => t.TaskId == task.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskStatus, "CANCELLED")); await Reject(HttpStatusCode.NotFound);
        await db.QuestionnaireTasks.Where(t => t.TaskId == task.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskStatus, "PENDING"));
        await db.QuestionnaireVersions.Where(v => v.QuestionnaireVersionId == assigned.QuestionnaireVersionId).ExecuteUpdateAsync(s => s.SetProperty(v => v.VersionStatus, "DRAFT")); await Reject(HttpStatusCode.ServiceUnavailable, "DEFINITION_UNAVAILABLE");
        await db.QuestionnaireVersions.Where(v => v.QuestionnaireVersionId == assigned.QuestionnaireVersionId).ExecuteUpdateAsync(s => s.SetProperty(v => v.VersionStatus, "RETIRED").SetProperty(v => v.DefinitionSnapshot, snapshot.Replace("\"q2\"", "\"q1\"")));
        await Reject(HttpStatusCode.ServiceUnavailable, "DEFINITION_UNAVAILABLE");
        await db.QuestionnaireVersions.Where(v => v.QuestionnaireVersionId == assigned.QuestionnaireVersionId).ExecuteUpdateAsync(s => s.SetProperty(v => v.DefinitionSnapshot, snapshot));
        await db.Users.Where(u => u.Id == parentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "DISABLED")); await Reject(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await db.Users.Where(u => u.Id == parentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "ACTIVE").SetProperty(u => u.EmailVerifiedAtUtc, (DateTime?)null)); await Reject(HttpStatusCode.NotFound);
        await db.Users.Where(u => u.Id == parentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerifiedAtUtc, DateTime.UtcNow));
        await db.Organizations.Where(o => o.Id == child.OrganizationId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "DISABLED")); await Reject(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await db.Organizations.Where(o => o.Id == child.OrganizationId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "ACTIVE"));
        await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId).ExecuteUpdateAsync(s => s.SetProperty(l => l.BoundAtUtc, (DateTime?)null)); await Reject(HttpStatusCode.NotFound);
        await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId).ExecuteUpdateAsync(s => s.SetProperty(l => l.BoundAtUtc, DateTime.UtcNow));

        var scope = new GuardianQuestionnaireDefinitionAccess(new CaseAccessService(db));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("userId", parentId.ToString())], "Bearer"));
        var realStore = new EfQuestionnaireDefinitionStore(db);
        var revokeDuringRead = new AfterReadStore(realStore, async () => {
            await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId).ExecuteUpdateAsync(s => s.SetProperty(l => l.LinkStatus, "REVOKED"));
        });
        var controller = new ParentQuestionnaireDefinitionsController(new(revokeDuringRead, scope, new()), scope)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };
        try { await controller.Definition(task.TaskId, default); throw new Exception("Expected mid-read revocation rejection"); }
        catch (QuestionnaireWorkflowException e) { Check(e.Code == "TASK_NOT_FOUND", "Second scope check catches revocation committed during read"); }
        await Reject(HttpStatusCode.NotFound);
        await db.CaseGuardians.Where(l => l.CaseId == caseId && l.GuardianId == guardianId).ExecuteUpdateAsync(s => s.SetProperty(l => l.LinkStatus, "ACTIVE"));
        Check((await client.GetAsync(path)).IsSuccessStatusCode, "Scope restored only with active bound link");
        Check(await db.QuestionnaireDrafts.CountAsync(d => d.TaskId == task.TaskId) == 0 && await db.QuestionnaireResponses.CountAsync(r => r.TaskId == task.TaskId) == 0, "Definition reads never create drafts or responses");
        Console.WriteLine($"PASS: {count} parent Definition integration checks; actual Email-issued JWT, Guardian adapter, EF SQLite store, HTTP and mid-read revoke check. Real MySQL not run.");
        return count;
    }

    private sealed class AfterReadStore(IQuestionnaireDefinitionStore inner, Func<Task> afterRead) : IQuestionnaireDefinitionStore
    {
        public async Task<QuestionnaireDefinitionSource?> FindAsync(Guid taskId, CancellationToken ct)
        { var row = await inner.FindAsync(taskId, ct); await afterRead(); return row; }
    }
}
