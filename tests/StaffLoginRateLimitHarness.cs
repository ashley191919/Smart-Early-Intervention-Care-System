using System.Net;
using System.Net.Http.Json;
using EarlyInterventionCare.Api.Controllers;
using EarlyInterventionCare.Api.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;

var checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}

// Verify the real MVC actions opt into the same production policy. The HTTP host
// below uses synthetic endpoints so it never initializes AuthenticationService or MySQL.
foreach (var action in new[] { typeof(AuthController).GetMethod(nameof(AuthController.Login))!,
             typeof(StaffSessionController).GetMethod(nameof(StaffSessionController.Create))! })
{
    var attribute = action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
        .Cast<EnableRateLimitingAttribute>().Single();
    Check(attribute.PolicyName == StaffLoginRateLimitPolicy.Name, "both real login actions use one policy");
}

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
var policy = new StaffLoginRateLimitPolicy();
string Partition(string ip)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
    return policy.GetPartition(context).PartitionKey;
}
var differentIp = Enumerable.Range(11, 200).Select(i => $"192.0.2.{i}")
    .First(ip => Partition(ip) != Partition("192.0.2.10"));
var partitions = Enumerable.Range(0, 10000)
    .Select(i => Partition($"10.{(i >> 16) & 255}.{(i >> 8) & 255}.{i & 255}"))
    .Distinct().Count();
Check(partitions <= StaffLoginRateLimitPolicy.BucketCount, "arbitrary source addresses cannot exceed the fixed bucket limit");
Check(Partition("::ffff:192.0.2.10") == Partition("192.0.2.10"), "IPv4 mapped address uses the same bucket");
try { _ = new StaffLoginRateLimitPolicy(0, 60); throw new Exception("invalid configuration accepted"); }
catch (ArgumentOutOfRangeException) { Check(true, "invalid configuration fails closed at startup"); }
builder.Services.AddRateLimiter(options =>
    options.AddPolicy(StaffLoginRateLimitPolicy.Name, policy));
await using var app = builder.Build();
app.UseRouting();
// Test-only transport simulation: this is not forwarded-header processing and
// is not installed in the application. It allows isolated connection IP partitions.
app.Use((context, next) =>
{
    if (IPAddress.TryParse(context.Request.Headers["X-Test-Connection-IP"], out var address))
        context.Connection.RemoteIpAddress = address;
    return next();
});
app.UseRateLimiter();
app.MapPost("/api/auth/login", () => Results.Ok(new { message = "synthetic" }))
    .RequireRateLimiting(StaffLoginRateLimitPolicy.Name);
app.MapPost("/api/auth/staff-session", () => Results.Ok(new { message = "synthetic" }))
    .RequireRateLimiting(StaffLoginRateLimitPolicy.Name);
app.MapPost("/unrelated", () => Results.NoContent());
await app.StartAsync();
var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };

async Task<HttpResponseMessage> Post(string route, string connectionIp, string? forwarded = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, route);
    request.Headers.Add("X-Test-Connection-IP", connectionIp);
    if (forwarded is not null) request.Headers.Add("X-Forwarded-For", forwarded);
    return await client.SendAsync(request);
}

for (var i = 0; i < StaffLoginRateLimitPolicy.DefaultPermitLimit; i++)
{
    var route = i % 2 == 0 ? "/api/auth/login" : "/api/auth/staff-session";
    using var response = await Post(route, "192.0.2.10", $"203.0.113.{i + 1}");
    Check(response.StatusCode == HttpStatusCode.OK, "within shared budget each route proceeds");
}

using (var blocked = await Post("/api/auth/login", "192.0.2.10", "198.51.100.1"))
{
    Check(blocked.StatusCode == HttpStatusCode.TooManyRequests, "changing X-Forwarded-For cannot bypass the shared budget");
    var body = await blocked.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Check(body?["code"] == "LOGIN_RATE_LIMITED" && body["message"].Contains("稍後"), "429 uses safe fixed message");
    Check(blocked.Headers.CacheControl?.NoStore == true, "429 is not cacheable");
}
using (var blockedOtherRoute = await Post("/api/auth/staff-session", "::ffff:192.0.2.10"))
    Check(blockedOtherRoute.StatusCode == HttpStatusCode.TooManyRequests, "mapped IPv4 and the other route share the budget");
using (var otherIp = await Post("/api/auth/staff-session", differentIp, "192.0.2.10"))
    Check(otherIp.StatusCode == HttpStatusCode.OK, "different non-colliding connection IP has a separate budget");
using (var unrelated = await Post("/unrelated", "192.0.2.10"))
    Check(unrelated.StatusCode == HttpStatusCode.NoContent, "unrelated endpoints are not affected");

await app.StopAsync();
Console.WriteLine($"PASS: {checks} offline staff login rate-limit checks; shared budget, IP partitions and untrusted forwarded headers.");
