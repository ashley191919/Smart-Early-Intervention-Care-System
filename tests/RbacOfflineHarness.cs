using System.Security.Claims;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Models.Authorization;
using EarlyInterventionCare.Api.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

// No host startup, database provider, connection, secrets or migrations.
// The model-backed test double exercises the handler/service contract, not EF SQL execution.
var fixture = new PermissionFixture();
var services = new ServiceCollection();
services.AddLogging();
services.AddScoped<IPermissionService>(_ => fixture);
services.AddPermissionAuthorization();
services.AddPermissionAuthorization(); // Registration must remain idempotent.
services.AddAuthorization(options =>
    options.AddPermissionPolicy("test.read", "case.read")
        .AddPermissionPolicy("test.write", "case.update"));
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using var scope = provider.CreateScope();
var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
var passed = 0;
var failed = 0;

ClaimsPrincipal Principal(Guid id, bool authenticated = true, string role = "CASE_MANAGER") =>
    new(new ClaimsIdentity(new[] { new Claim("userId", id.ToString()), new Claim("role", role) },
        authenticated ? "offline-test" : null));
async Task<bool> Allowed(ClaimsPrincipal user, string policy = "test.read") =>
    (await authorization.AuthorizeAsync(user, null, policy)).Succeeded;
async Task Test(string name, Func<Task<bool>> check)
{
    try
    {
        if (!await check()) throw new InvalidOperationException("Unexpected authorization result.");
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch
    {
        failed++;
        Console.WriteLine("FAIL " + name);
    }
}

await Test("assigned permission allows access", () => Allowed(Principal(fixture.Allowed.Id)));
await Test("missing permission denies access", async () => !await Allowed(Principal(fixture.Allowed.Id), "test.write"));
await Test("disabled account denies access", async () => !await Allowed(Principal(fixture.Disabled.Id)));
await Test("nonexistent account denies access", async () => !await Allowed(Principal(Guid.NewGuid())));
await Test("revocation takes effect on same principal and service scope", async () =>
{
    var user = Principal(fixture.Allowed.Id);
    if (!await Allowed(user)) return false;
    var assignment = fixture.Assignments.Single();
    fixture.Assignments.Clear();
    try { return !await Allowed(user); }
    finally { fixture.Assignments.Add(assignment); }
});
await Test("different users do not inherit permissions", async () =>
    await Allowed(Principal(fixture.Allowed.Id)) && !await Allowed(Principal(fixture.Other.Id)) &&
    await Allowed(Principal(fixture.Allowed.Id)));
await Test("unauthenticated user denied without service query", async () =>
{
    var calls = fixture.Calls;
    return !await Allowed(Principal(fixture.Allowed.Id, false)) && fixture.Calls == calls;
});
await Test("ADMIN has no implicit permission", async () => !await Allowed(Principal(fixture.Admin.Id, role: "ADMIN")));
await Test("forged permission and role claims cannot grant access", async () =>
{
    var user = Principal(fixture.Other.Id, role: "ADMIN");
    ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("permission", "case.read"));
    return !await Allowed(user);
});
await Test("missing userId denied", async () => !await Allowed(new ClaimsPrincipal(new ClaimsIdentity("offline-test"))));
await Test("malformed userId denied", async () => !await Allowed(new ClaimsPrincipal(
    new ClaimsIdentity(new[] { new Claim("userId", "invalid") }, "offline-test"))));
await Test("empty GUID denied", async () => !await Allowed(Principal(Guid.Empty)));
await Test("duplicate userId denied", async () =>
{
    var user = Principal(fixture.Allowed.Id);
    ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("userId", fixture.Other.Id.ToString()));
    return !await Allowed(user);
});
await Test("multiple authenticated identities denied", async () =>
{
    var user = Principal(fixture.Allowed.Id);
    user.AddIdentity((ClaimsIdentity)Principal(fixture.Other.Id).Identity!);
    return !await Allowed(user);
});
await Test("unauthenticated identity cannot supply userId", async () =>
{
    var user = new ClaimsPrincipal(new ClaimsIdentity("offline-test"));
    user.AddIdentity((ClaimsIdentity)Principal(fixture.Allowed.Id, false).Identity!);
    return !await Allowed(user);
});
await Test("account disabled after success is denied", async () =>
{
    var user = Principal(fixture.Allowed.Id);
    if (!await Allowed(user)) return false;
    fixture.Allowed.Status = "DISABLED";
    try { return !await Allowed(user); }
    finally { fixture.Allowed.Status = "ACTIVE"; }
});
await Test("role reassignment cannot reuse JWT role", async () =>
{
    var previous = fixture.Allowed.RoleId;
    fixture.Allowed.RoleId = fixture.Other.RoleId;
    try { return !await Allowed(Principal(fixture.Allowed.Id)); }
    finally { fixture.Allowed.RoleId = previous; }
});
await Test("service failure cannot produce success", async () =>
{
    fixture.ThrowOnRead = true;
    try { await Allowed(Principal(fixture.Allowed.Id)); return false; }
    catch (InvalidOperationException) { return true; }
    finally { fixture.ThrowOnRead = false; }
});
await Test("blank or padded requirement rejected", () =>
{
    foreach (var name in new[] { "", " ", " case.read", "case.read " })
    {
        try { _ = new PermissionRequirement(name); return Task.FromResult(false); }
        catch (ArgumentException) { }
    }
    return Task.FromResult(true);
});
await Test("unknown policy does not allow access", async () =>
{
    try { await Allowed(Principal(fixture.Allowed.Id), "unregistered"); return false; }
    catch (InvalidOperationException) { return true; }
});
await Test("handler registration is idempotent", () => Task.FromResult(
    scope.ServiceProvider.GetServices<IAuthorizationHandler>().OfType<PermissionAuthorizationHandler>().Count() == 1));
await Test("explicit failure overrides another handler success", async () =>
{
    var requirement = new PermissionRequirement("case.update");
    var context = new AuthorizationHandlerContext(new[] { requirement }, Principal(fixture.Allowed.Id), null);
    context.Succeed(requirement);
    await scope.ServiceProvider.GetServices<IAuthorizationHandler>()
        .OfType<PermissionAuthorizationHandler>().Single().HandleAsync(context);
    return context.HasFailed && !context.HasSucceeded;
});

Console.WriteLine($"RBAC offline: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class PermissionFixture : IPermissionService
{
    public User Allowed { get; } = new() { Id = Guid.NewGuid(), RoleId = Guid.NewGuid(), Status = "ACTIVE" };
    public User Disabled { get; }
    public User Other { get; } = new() { Id = Guid.NewGuid(), RoleId = Guid.NewGuid(), Status = "ACTIVE" };
    public User Admin { get; } = new() { Id = Guid.NewGuid(), RoleId = Guid.NewGuid(), Status = "ACTIVE" };
    public List<RolePermission> Assignments { get; } = new();
    public int Calls { get; private set; }
    public bool ThrowOnRead { get; set; }
    private readonly User[] users;
    private readonly Role[] roles;
    private readonly Permission permission = new() { Id = Guid.NewGuid(), Name = "case.read" };

    public PermissionFixture()
    {
        Disabled = new User { Id = Guid.NewGuid(), RoleId = Allowed.RoleId, Status = "DISABLED" };
        users = new[] { Allowed, Disabled, Other, Admin };
        roles = new[] { new Role { Id = Allowed.RoleId, Name = "CASE_MANAGER" },
            new Role { Id = Other.RoleId, Name = "MEDICAL_STAFF" }, new Role { Id = Admin.RoleId, Name = "ADMIN" } };
        Assignments.Add(new RolePermission { RoleId = Allowed.RoleId, PermissionId = permission.Id });
    }

    public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        if (ThrowOnRead) throw new InvalidOperationException("Offline simulated lookup failure.");
        var user = users.SingleOrDefault(user => user.Id == userId);
        IReadOnlyList<string> result = user is not null && user.Status == "ACTIVE" &&
            roles.Any(role => role.Id == user.RoleId) &&
            Assignments.Any(link => link.RoleId == user.RoleId && link.PermissionId == permission.Id)
            ? new[] { permission.Name } : Array.Empty<string>();
        return Task.FromResult(result);
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string required, CancellationToken cancellationToken = default) =>
        (await GetPermissionsAsync(userId, cancellationToken)).Contains(required, StringComparer.Ordinal);
}
