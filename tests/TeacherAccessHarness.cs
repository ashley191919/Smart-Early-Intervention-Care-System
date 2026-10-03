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
        && html.Contains("開發測試：尚未提供提交功能") && !html.Contains("type=\"submit\"")
        && valid.Headers.CacheControl?.NoStore == true, "valid link shows two read-only fixture questions");
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
    var quick = await Create(client, 1);
    await Task.Delay(1200);
    await Invalid(client, quick.TeacherFormUrl, "expired grant rejected");
    var clock = new TestClock();
    var service = new InMemoryTeacherGrantService(clock);
    var equality = service.Create(1);
    clock.Now = equality.ExpiresAtUtc;
    Check(service.Validate(equality.TeacherFormUrl.Split("token=")[1]) is null,
        "exact expiration boundary is invalid");
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
    public override DateTimeOffset GetUtcNow() => Now;
}
