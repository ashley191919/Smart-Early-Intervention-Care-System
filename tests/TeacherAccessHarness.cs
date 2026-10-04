// Standalone HTTP verification of the actual C-module files, without EF/MySQL.
// This does not replace building/running the full API project.
using System.Net;
using System.Net.Http.Json;
using System.Text;
using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Development;
using EarlyInterventionCare.Api.Services;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}

static async Task<WebApplication> Start(string environment)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        EnvironmentName = environment,
        Args = Array.Empty<string>()
    });
    builder.Logging.ClearProviders(); // Never emit bearer query strings.
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddControllers().AddApplicationPart(typeof(TeacherTestFormController).Assembly);
    if (environment == "Development")
    {
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IAuditLogService, InMemoryAuditLogService>();
        builder.Services.AddSingleton<ITeacherGrantService, InMemoryTeacherGrantService>();
    }
    var app = builder.Build();
    app.UseDevelopmentTeacherAccess();
    app.MapControllers();
    await app.StartAsync();
    return app;
}

static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };

static async Task<CreateTeacherGrantResponse> Create(HttpClient client)
{
    var response = await client.PostAsJsonAsync("/api/dev/teacher-grants", new { });
    Check(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true,
        "creation succeeds and forbids caching");
    return (await response.Content.ReadFromJsonAsync<CreateTeacherGrantResponse>())!;
}

static async Task Invalid(HttpClient client, string path, string label)
{
    var response = await client.GetAsync(path);
    var html = await response.Content.ReadAsStringAsync();
    Check(response.StatusCode == HttpStatusCode.NotFound && html.Contains("連結無效或已失效")
        && !html.Contains("<fieldset>") && response.Headers.CacheControl?.NoStore == true, label);
}

var dev = await Start("Development");
string oldUrl;
try
{
    using var client = Client(dev);
    var defaultResponse = await client.PostAsJsonAsync("/api/dev/teacher-grants", new { });
    var defaultGrant = (await defaultResponse.Content.ReadFromJsonAsync<CreateTeacherGrantResponse>())!;
    Check(! (await defaultResponse.Content.ReadAsStringAsync()).Contains("expires", StringComparison.OrdinalIgnoreCase), "creation has no expiration fields");
    var grant = await Create(client);
    oldUrl = grant.TeacherFormUrl;
    Check(oldUrl.StartsWith("/teacher/test-form?token="), "link is same-origin relative path");
    var valid = await client.GetAsync(oldUrl);
    var html = await valid.Content.ReadAsStringAsync();
    Check(valid.IsSuccessStatusCode && html.Split("<fieldset>").Length == 3
        && html.Contains("開發測試：回覆僅暫存於記憶體，服務重啟後清除") && html.Contains("type=\"submit\"")
        && html.Contains("填答不限時") && valid.Headers.CacheControl?.NoStore == true, "valid link shows two required fixture questions with submit");
    Check((await client.GetStringAsync(oldUrl)) == html, "GET does not consume grant");
    Check((await client.GetStringAsync(oldUrl + "&caseId=other&formId=other&taskId=other")) == html,
        "extra identifiers cannot change scope");
    await Invalid(client, "/teacher/test-form", "missing token rejected");
    await Invalid(client, "/teacher/test-form?token=invalid", "malformed token rejected");
    await Invalid(client, "/teacher/test-form?token=" + new string('A', 43), "unknown token rejected");
    var last = oldUrl[^1] == 'A' ? 'B' : 'A';
    await Invalid(client, oldUrl[..^1] + last, "modified token rejected");
    await Invalid(client, "/teacher/test-form?token=" + new string('A', 1000), "oversized token rejected");
    await Invalid(client, oldUrl + "&token=invalid", "duplicate token rejected");
    async Task<HttpResponseMessage> Submit(string url, params (string, string)[] answers) =>
        await client.PostAsync(url, new FormUrlEncodedContent(answers.Select(a => new KeyValuePair<string, string>(a.Item1, a.Item2))));
    var store = (InMemoryTeacherGrantService)dev.Services.GetRequiredService<ITeacherGrantService>();
    foreach (var answers in new[] {
        new[] { ("question1", "yes") },
        new[] { ("question1", "invalid"), ("question2", "no") },
        new[] { ("question1", "yes"), ("question1", "no"), ("question2", "no") } })
    {
        var bad = await Submit(oldUrl, answers);
        Check(bad.StatusCode == HttpStatusCode.BadRequest && bad.Headers.CacheControl?.NoStore == true
            && store.Responses.Length == 0 && (await client.GetAsync(oldUrl)).IsSuccessStatusCode,
            "missing/illegal/duplicate answers do not consume grant");
    }
    var sent = await Submit(oldUrl, ("question1", "yes"), ("question2", "sometimes"),
        ("caseId", "other"), ("taskId", "other"), ("questionnaireVersionId", "other"));
    Check(sent.IsSuccessStatusCode && (await sent.Content.ReadAsStringAsync()).Contains("已成功送出，此連結已失效")
        && sent.Headers.CacheControl?.NoStore == true, "legal submission succeeds");
    var saved = store.Responses.Single();
    Check(saved.ResponseId != Guid.Empty && saved.GrantId == grant.GrantId && saved.CaseId == "dev-case-001"
        && saved.TaskId == "dev-task-001" && saved.TaskVersionId == "dev-task-v1"
        && saved.QuestionnaireVersionId == "dev-questionnaire-v1" && saved.Question1 == "yes"
        && saved.Question2 == "sometimes" && saved.SubmittedAtUtc.Offset == TimeSpan.Zero,
        "response metadata and UTC time come from grant");
    await Invalid(client, oldUrl, "used link cannot reopen form");
    Check((await Submit(oldUrl, ("question1", "yes"), ("question2", "yes"))).StatusCode == HttpStatusCode.NotFound
        && store.Responses.Length == 1, "used link cannot submit again");
    var concurrent = await Create(client);
    var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
        Submit(concurrent.TeacherFormUrl, ("question1", "no"), ("question2", "yes"))));
    Check(results.Count(r => r.StatusCode == HttpStatusCode.OK) == 1
        && results.Count(r => r.StatusCode == HttpStatusCode.NotFound) == 19
        && store.Responses.Count(r => r.GrantId == concurrent.GrantId) == 1,
        "20 concurrent submissions save exactly one response");
    var clock = new TestClock();
    var service = new InMemoryTeacherGrantService(clock, new InMemoryAuditLogService(clock));
    var longLived = service.Create();
    var longToken = longLived.TeacherFormUrl.Split("token=")[1];
    clock.Now = clock.Now.AddYears(10);
    Check(service.Validate(longToken) != null, "grant remains valid after ten years");
    Check(service.Submit(longToken, "yes", "no") == SubmissionResult.Success,
        "submission after ten years succeeds");
    clock.Now = clock.Now.AddYears(10);
    Check(service.Validate(longToken) == null && service.Submit(longToken, "yes", "no") == SubmissionResult.InvalidGrant,
        "elapsed time never restores a used grant");
    Check(service.Responses.Single().SubmittedAtUtc == clock.Now.AddYears(-10), "delayed submission preserves UTC timestamp");
    var beforeRetry = service.Responses.Length;
    var retry = service.Create();
    var retryToken = retry.TeacherFormUrl.Split("token=")[1];
    clock.ReadsUntilFailure = 1; // Fail while preparing submission time, before atomic save.
    Check(service.Submit(retryToken, "yes", "no") == SubmissionResult.SaveFailed
        && service.Responses.Length == beforeRetry && service.Validate(retryToken) != null,
        "save preparation failure preserves grant and stores no response");
    Check(service.Submit(retryToken, "yes", "no") == SubmissionResult.Success
        && service.Responses.Length == beforeRetry + 1, "retry after save failure succeeds once");

    async Task<HttpResponseMessage> Revoke(Guid id) =>
        await client.PostAsync($"/api/dev/teacher-grants/{id}/revoke", null);
    var revocable = await Create(client);
    Check((await client.GetAsync(revocable.TeacherFormUrl)).IsSuccessStatusCode,
        "form opens before revocation");
    var revokedHttp = await Revoke(revocable.GrantId);
    var revoked = (await revokedHttp.Content.ReadFromJsonAsync<RevokeTeacherGrantResponse>())!;
    Check(revokedHttp.StatusCode == HttpStatusCode.OK && revokedHttp.Headers.CacheControl?.NoStore == true
        && revoked.GrantId == revocable.GrantId && revoked.Status == "REVOKED"
        && revoked.RevokedAtUtc?.Offset == TimeSpan.Zero, "revoke returns REVOKED and UTC timestamp without token");
    await Invalid(client, revocable.TeacherFormUrl, "revoked GET rejects form");
    Check((await Submit(revocable.TeacherFormUrl, ("question1", "yes"), ("question2", "no"))).StatusCode == HttpStatusCode.NotFound
        && !store.Responses.Any(r => r.GrantId == revocable.GrantId), "revocation first rejects opened form submission without response");
    var repeatHttp = await Revoke(revocable.GrantId);
    Check(repeatHttp.StatusCode == HttpStatusCode.OK
        && await repeatHttp.Content.ReadFromJsonAsync<RevokeTeacherGrantResponse>() == revoked,
        "repeat revocation preserves identical result and first timestamp");
    var usedHttp = await Revoke(grant.GrantId);
    Check(usedHttp.StatusCode == HttpStatusCode.Conflict
        && (await usedHttp.Content.ReadFromJsonAsync<RevokeTeacherGrantResponse>())!.Status == "USED"
        && store.Responses.Single(r => r.GrantId == grant.GrantId) == saved,
        "submission first returns USED conflict and preserves original response");
    var unknownHttp = await Revoke(Guid.NewGuid());
    Check(unknownHttp.StatusCode == HttpStatusCode.NotFound && unknownHttp.Headers.CacheControl?.NoStore == true,
        "unknown grant ID returns uncached 404");
    Check((await client.PostAsync("/api/dev/teacher-grants/not-a-guid/revoke", null)).StatusCode == HttpStatusCode.BadRequest,
        "Development malformed grant ID returns 400");
    var longRevocable = service.Create();
    var longRevokeToken = longRevocable.TeacherFormUrl.Split("token=")[1];
    clock.Now = clock.Now.AddYears(10);
    var longRevoked = service.Revoke(longRevocable.GrantId);
    Check(longRevoked?.Status == "REVOKED", "unused grant can be revoked after ten years");
    clock.Now = clock.Now.AddYears(10);
    Check(service.Revoke(longRevocable.GrantId) == longRevoked && service.Validate(longRevokeToken) == null
        && service.Submit(longRevokeToken, "yes", "no") == SubmissionResult.InvalidGrant,
        "elapsed time preserves first revocation and never restores access");
    for (var i = 0; i < 30; i++)
    {
        var race = await Create(client);
        var revokeTask = Revoke(race.GrantId);
        var submitTask = Submit(race.TeacherFormUrl, ("question1", "yes"), ("question2", "no"));
        await Task.WhenAll(revokeTask, submitTask);
        var revokeResult = (await revokeTask.Result.Content.ReadFromJsonAsync<RevokeTeacherGrantResponse>())!.Status;
        var count = store.Responses.Count(r => r.GrantId == race.GrantId);
        Check((revokeTask.Result.StatusCode == HttpStatusCode.OK && revokeResult == "REVOKED"
                && submitTask.Result.StatusCode == HttpStatusCode.NotFound && count == 0)
            || (revokeTask.Result.StatusCode == HttpStatusCode.Conflict && revokeResult == "USED"
                && submitTask.Result.StatusCode == HttpStatusCode.OK && count == 1),
            "concurrent revoke/submit has one valid terminal outcome");
    }
    var audit = dev.Services.GetRequiredService<IAuditLogService>();
    var grantEvents = audit.Query("TeacherGrant", grant.GrantId.ToString(), 200);
    Check(grantEvents.Count(e => e.Action == "TeacherGrant.Create" && e.Result == "Success") == 1
        && grantEvents.Count(e => e.Action == "TeacherResponse.Submit" && e.Result == "Success") == 1
        && grantEvents.Any(e => e.Result == "Rejected:USED"), "create, submit success and USED rejection events");
    Check(audit.Query("TeacherGrant", concurrent.GrantId.ToString(), 200)
        .Count(e => e.Action == "TeacherResponse.Submit" && e.Result == "Success") == 1,
        "20 parallel submissions emit exactly one success event");
    var revokeEvents = audit.Query("TeacherGrant", revocable.GrantId.ToString(), 200);
    Check(revokeEvents.Count(e => e.Action == "TeacherGrant.Revoke") == 1
        && revokeEvents.Any(e => e.Result == "Rejected:REVOKED"),
        "first revoke only and revoked rejection events");
    Check(grantEvents.All(e => e.EventId != Guid.Empty && e.OccurredAtUtc.Offset == TimeSpan.Zero
        && !string.IsNullOrWhiteSpace(e.RequestCorrelationId) && e.ResourceId == grant.GrantId.ToString())
        && grantEvents.Where(e => e.Action == "TeacherGrant.Create").All(e => e.ActorType == "DevelopmentTestOperator")
        && grantEvents.Where(e => e.Action == "TeacherResponse.Submit").All(e => e.ActorType == "TeacherGrantBearer" && e.ActorId == grant.GrantId.ToString()),
        "event identity, UTC, correlation and honest actor labels");
    var queryHttp = await client.GetAsync($"/api/dev/audit-logs?grantId={grant.GrantId}&limit=2");
    var queryJson = System.Text.Json.JsonDocument.Parse(await queryHttp.Content.ReadAsStringAsync());
    Check(queryHttp.IsSuccessStatusCode && queryHttp.Headers.CacheControl?.NoStore == true
        && queryJson.RootElement.GetProperty("events").GetArrayLength() == 2
        && queryJson.RootElement.GetProperty("notice").GetString()!.Contains("尚非正式持久化稽核"), "Development query filter/limit/no-store and memory notice");
    foreach (var badQuery in new[] { "limit=0", "limit=201", "grantId=bad" })
        Check((await client.GetAsync("/api/dev/audit-logs?" + badQuery)).StatusCode == HttpStatusCode.BadRequest,
            "audit query validates bounds and grant ID");
    var isolatedAudit = new InMemoryAuditLogService(TimeProvider.System);
    var isolated = new InMemoryTeacherGrantService(TimeProvider.System, isolatedAudit);
    var secretGrant = isolated.Create("safe-correlation");
    var secretToken = secretGrant.TeacherFormUrl.Split("token=")[1];
    isolated.Submit(secretToken, "yes", "sometimes", "safe-correlation");
    var beforeUnknown = isolatedAudit.Query().Count;
    isolated.Submit(new string('A', 43), "password-OTP-sensitive-answer", "no");
    var json = System.Text.Json.JsonSerializer.Serialize(isolatedAudit.Query());
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(secretToken)));
    Check(isolatedAudit.Query().Count == beforeUnknown && !json.Contains(secretToken) && !json.Contains(hash)
        && !json.Contains(secretGrant.TeacherFormUrl) && !json.Contains("dev-case-001")
        && !json.Contains("sometimes") && !json.Contains("password-OTP-sensitive-answer"), "audit excludes unknown token, hash, URL, answers and case content");
    var failureStore = new InMemoryTeacherGrantService(TimeProvider.System, new ThrowingAudit());
    var failureGrant = failureStore.Create();
    var failureToken = failureGrant.TeacherFormUrl.Split("token=")[1];
    Check(failureStore.Submit(failureToken, "yes", "no") == SubmissionResult.Success
        && failureStore.Validate(failureToken) == null && failureStore.Responses.Length == 1,
        "throwing audit cannot fail saved submission or restore USED grant");
    var failureRevoke = failureStore.Create();
    Check(failureStore.Revoke(failureRevoke.GrantId)?.Status == "REVOKED"
        && failureStore.Validate(failureRevoke.TeacherFormUrl.Split("token=")[1]) == null,
        "throwing audit cannot restore revoked grant");
    var sharedAudit = new InMemoryAuditLogService(TimeProvider.System);
    await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => {
        sharedAudit.TryWrite(new("DevelopmentTestOperator", "test", "Test.Action", "TestResource", i.ToString(), "Success", "test-request"));
        sharedAudit.Query(limit: 200);
    })));
    Check(sharedAudit.Query(limit: 200).Count == 100, "shared audit concurrent writes and reads are safe");
}
finally { await dev.StopAsync(); await dev.DisposeAsync(); }

var restarted = await Start("Development");
try
{
    using var client = Client(restarted);
    Check(restarted.Services.GetRequiredService<IAuditLogService>().Query().Count == 0, "restart clears audit records");
    await Invalid(client, oldUrl, "fresh host/store rejects previous grant");
}
finally { await restarted.StopAsync(); await restarted.DisposeAsync(); }

var production = await Start("Production");
try
{
    using var client = Client(production);
    foreach (var query in new[] { "", "?grantId=bad&limit=invalid" })
    {
        var disabledAudit = await client.GetAsync("/api/dev/audit-logs" + query);
        Check(disabledAudit.StatusCode == HttpStatusCode.NotFound && disabledAudit.Headers.CacheControl?.NoStore == true,
            "Production audit query including malformed query is uncached 404");
    }
    foreach (var id in new[] { Guid.NewGuid().ToString(), "not-a-guid" })
    {
        var disabled = await client.PostAsync($"/api/dev/teacher-grants/{id}/revoke",
            new StringContent("{bad", Encoding.UTF8, "application/json"));
        Check(disabled.StatusCode == HttpStatusCode.NotFound && disabled.Headers.CacheControl?.NoStore == true,
            "Production revoke including malformed route/body is uncached 404");
    }
    Check((await client.PostAsync(oldUrl, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("question1", "yes") }))).StatusCode == HttpStatusCode.NotFound, "Production submission disabled");
    Check((await client.GetAsync(oldUrl)).StatusCode == HttpStatusCode.NotFound, "Production GET is 404 without grant service registration");
    Check((await client.PostAsJsonAsync("/api/dev/teacher-grants", new { })).StatusCode == HttpStatusCode.NotFound,
        "Production POST is 404 without grant service registration");
    Check((await client.PostAsync("/api/dev/teacher-grants", new StringContent("{bad", Encoding.UTF8, "application/json")))
        .StatusCode == HttpStatusCode.NotFound, "Production malformed POST remains 404");
}
finally { await production.StopAsync(); await production.DisposeAsync(); }

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public int ReadsUntilFailure { get; set; } = -1;
    public override DateTimeOffset GetUtcNow()
    {
        if (ReadsUntilFailure > 0 && --ReadsUntilFailure == 0)
            throw new InvalidOperationException("Simulated preparation failure");
        return Now;
    }
}

sealed class ThrowingAudit : IAuditLogService
{
    public bool TryWrite(AuditLogRequest request) => throw new InvalidOperationException("Audit unavailable");
    public IReadOnlyList<AuditLogEvent> Query(string? resourceType = null, string? resourceId = null, int limit = 50) => Array.Empty<AuditLogEvent>();
}
