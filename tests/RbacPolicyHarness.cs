using System.Security.Claims;
using EarlyInterventionCare.Api.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Production policy registration/handler, mutable IPermissionService double.
// No database, API host or 07/11 controllers are used in this executable.
var codes = new[] { "case.status.update", "case.status_history.read", "case.read", "questionnaire.task.read" };
var fixture = new PolicyPermissions();
var services = new ServiceCollection();
services.AddLogging();
services.AddRouting();
services.AddScoped<IPermissionService>(_ => fixture);
services.AddPermissionAuthorization();
services.AddPermissionAuthorization();
services.AddAuthorization(options => options.AddFirstBatchPermissionPolicies());
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = provider.CreateScope();
var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
var defaults = new AuthorizationOptions();
int passed = 0, failed = 0;
ClaimsPrincipal Principal(bool authenticated = true) => new(new ClaimsIdentity(
    new[] { new Claim("userId", fixture.UserId.ToString()) }, authenticated ? "policy-test" : null));
async Task<bool> Allowed(string code, bool authenticated = true) =>
    (await authorization.AuthorizeAsync(Principal(authenticated), null, "Permission:" + code)).Succeeded;
async Task Test(string name, Func<Task<bool>> test)
{
    fixture.Active = true;
    fixture.Codes.Clear();
    fixture.Calls = 0;
    try
    {
        if (!await test()) throw new InvalidOperationException("Unexpected result");
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {error.GetType().Name}");
    }
}
foreach (var code in codes)
{
    await Test(code + " mapping and authenticated requirement", () =>
    {
        var policy = options.GetPolicy("Permission:" + code);
        return Task.FromResult(policy is not null && policy.Requirements.Count == 2 &&
            policy.Requirements.OfType<PermissionRequirement>().Single().Permission == code &&
            policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Count() == 1);
    });
    foreach (var granted in codes)
        await Test($"only {granted} granted; evaluating {code}", async () =>
        {
            fixture.Codes.Add(granted);
            return await Allowed(code) == (code == granted);
        });
    await Test(code + " anonymous denied before lookup", async () =>
    {
        fixture.Codes.Add(code);
        return !await Allowed(code, false) && fixture.Calls == 0;
    });
    await Test(code + " no configuration denies", async () => !await Allowed(code));
    await Test(code + " disabled account denies", async () =>
    {
        fixture.Codes.Add(code);
        fixture.Active = false;
        return !await Allowed(code);
    });
    await Test(code + " revoked permission denies on next evaluation", async () =>
    {
        fixture.Codes.Add(code);
        if (!await Allowed(code)) return false;
        fixture.Codes.Clear();
        return !await Allowed(code) && fixture.Calls == 2;
    });
    await Test(code + " duplicate rejected without changing policy", () =>
    {
        var candidate = new AuthorizationOptions().AddFirstBatchPermissionPolicies();
        try { candidate.AddPermissionPolicy("Permission:" + code, "other.permission"); return Task.FromResult(false); }
        catch (InvalidOperationException)
        {
            return Task.FromResult(candidate.GetPolicy("Permission:" + code)!.Requirements
                .OfType<PermissionRequirement>().Single().Permission == code);
        }
    });
}
await Test("DefaultPolicy and FallbackPolicy unchanged", () => Task.FromResult(
    options.FallbackPolicy == defaults.FallbackPolicy &&
    options.DefaultPolicy.AuthenticationSchemes.SequenceEqual(defaults.DefaultPolicy.AuthenticationSchemes) &&
    options.DefaultPolicy.Requirements.Select(x => x.GetType()).SequenceEqual(defaults.DefaultPolicy.Requirements.Select(x => x.GetType()))));
await Test("DI still has exactly one permission handler", () => Task.FromResult(
    scope.ServiceProvider.GetServices<IAuthorizationHandler>().OfType<PermissionAuthorizationHandler>().Count() == 1));
await Test("Program calls shared registrations exactly once without changing default policies", async () =>
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EarlyInterventionCare.Api", "Program.cs"))) directory = directory.Parent;
    if (directory is null) return false;
    var source = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "EarlyInterventionCare.Api", "Program.cs"));
    return source.Split("builder.Services.AddPermissionAuthorization();").Length == 2 &&
        source.Split("builder.Services.AddAuthorization(options => options.AddFirstBatchPermissionPolicies());").Length == 2 &&
        !source.Contains("TestOnly:") && !source.Contains("FallbackPolicy") && !source.Contains("DefaultPolicy");
});
Console.WriteLine($"RBAC policies: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class PolicyPermissions : IPermissionService
{
    public Guid UserId { get; } = Guid.NewGuid();
    public bool Active { get; set; } = true;
    public HashSet<string> Codes { get; } = new(StringComparer.Ordinal);
    public int Calls { get; set; }
    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        IReadOnlyList<string> result = Active && userId == UserId ? Codes.ToArray() : Array.Empty<string>();
        return Task.FromResult(result);
    }
    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(permission, StringComparer.Ordinal);
}
