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
        builder.Services.AddSingleton<ITeacherGrantService, InMemoryTeacherGrantService>();
    }
    var app = builder.Build();
    app.UseDevelopmentTeacherAccess();
    app.MapControllers();
    await app.StartAsync();
    return app;
}

static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };

static async Task<CreateTeacherGrantResponse> Create(HttpClient client, int seconds)
{
    var response = await client.PostAsJsonAsync("/api/dev/teacher-grants", new { expiresInSeconds = seconds });
    Check(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true,
        "creation succeeds and forbids caching");
    return (await response.Content.ReadFromJsonAsync<CreateTeacherGrantResponse>())!;
}

static async Task Invalid(HttpClient client, string path, string label)
{
    var response = await client.GetAsync(path);
    var html = await response.Content.ReadAsStringAsync();
    Check(response.StatusCode == HttpStatusCode.NotFound && html.Contains("連結無效或已到期")
        && !html.Contains("<fieldset>") && response.Headers.CacheControl?.NoStore == true, label);
}

var dev = await Start("Development");
string oldUrl;
try
{
    using var client = Client(dev);
    var defaultResponse = await client.PostAsJsonAsync("/api/dev/teacher-grants", new { });
    var defaultGrant = (await defaultResponse.Content.ReadFromJsonAsync<CreateTeacherGrantResponse>())!;
    Check(defaultGrant.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(290), "default lifetime is 300 seconds");
    var grant = await Create(client, 300);
    oldUrl = grant.TeacherFormUrl;
    Check(oldUrl.StartsWith("/teacher/test-form?token="), "link is same-origin relative path");
    var valid = await client.GetAsync(oldUrl);
    var html = await valid.Content.ReadAsStringAsync();
    Check(valid.IsSuccessStatusCode && html.Split("<fieldset>").Length == 3
        && html.Contains("開發測試：回覆僅暫存於記憶體，服務重啟後清除") && html.Contains("type=\"submit\"")
        && valid.Headers.CacheControl?.NoStore == true, "valid link shows two required fixture questions with submit");
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
    foreach (var seconds in new[] { 0, 3601 })
    {
        var bad = await client.PostAsJsonAsync("/api/dev/teacher-grants", new { expiresInSeconds = seconds });
        Check(bad.StatusCode == HttpStatusCode.BadRequest &&
            (await bad.Content.ReadAsStringAsync()).Contains("ExpiresInSeconds"), "out-of-range field validation");
    }
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
    var concurrent = await Create(client, 300);
    var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
        Submit(concurrent.TeacherFormUrl, ("question1", "no"), ("question2", "yes"))));
    Check(results.Count(r => r.StatusCode == HttpStatusCode.OK) == 1
        && results.Count(r => r.StatusCode == HttpStatusCode.NotFound) == 19
        && store.Responses.Count(r => r.GrantId == concurrent.GrantId) == 1,
        "20 concurrent submissions save exactly one response");
    var opened = await Create(client, 1);
    Check((await client.GetAsync(opened.TeacherFormUrl)).IsSuccessStatusCode, "open form before expiration");
    await Task.Delay(1200);
    Check((await Submit(opened.TeacherFormUrl, ("question1", "yes"), ("question2", "yes"))).StatusCode == HttpStatusCode.NotFound
        && !store.Responses.Any(r => r.GrantId == opened.GrantId), "expiration after opening rejects submission");
    var quick = await Create(client, 1);
    await Task.Delay(1200);
    await Invalid(client, quick.TeacherFormUrl, "expired grant rejected");
    var clock = new TestClock();
    var service = new InMemoryTeacherGrantService(clock);
    var equality = service.Create(1);
    clock.Now = equality.ExpiresAtUtc;
    Check(service.Submit(equality.TeacherFormUrl.Split("token=")[1], "yes", "no") == SubmissionResult.InvalidGrant && service.Validate(equality.TeacherFormUrl.Split("token=")[1]) is null,
        "exact expiration boundary is invalid");
    var retry = service.Create(300);
    var retryToken = retry.TeacherFormUrl.Split("token=")[1];
    clock.ReadsUntilFailure = 2; // Fail while preparing submission time, before atomic save.
    Check(service.Submit(retryToken, "yes", "no") == SubmissionResult.SaveFailed
        && service.Responses.Length == 0 && service.Validate(retryToken) != null,
        "save preparation failure preserves grant and stores no response");
    Check(service.Submit(retryToken, "yes", "no") == SubmissionResult.Success
        && service.Responses.Length == 1, "retry after save failure succeeds once");
}
finally { await dev.StopAsync(); await dev.DisposeAsync(); }

var restarted = await Start("Development");
try
{
    using var client = Client(restarted);
    await Invalid(client, oldUrl, "fresh host/store rejects previous grant");
}
finally { await restarted.StopAsync(); await restarted.DisposeAsync(); }

var production = await Start("Production");
try
{
    using var client = Client(production);
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
