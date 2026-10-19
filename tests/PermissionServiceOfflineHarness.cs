using System.Data;
using System.Data.Common;
using System.Security.Claims;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

// Executes the production service and Pomelo translation/materialization. Command results
// are controlled at the ADO boundary; this is not a MySQL server/integration test.
int passed = 0, failed = 0;
async Task Test(string name, Func<Fixture, Task<bool>> check)
{
    using var fixture = new Fixture();
    try
    {
        if (!await check(fixture)) throw new InvalidOperationException("Unexpected result");
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {error.GetType().Name}");
    }
}
ClaimsPrincipal Principal(Guid id, bool authenticated = true) => new(new ClaimsIdentity(
    new[] { new Claim("userId", id.ToString()), new Claim("role", "ADMIN"), new Claim("permission", "case.update") },
    authenticated ? "offline" : null));
async Task<bool> Allows(Fixture f, ClaimsPrincipal user, string policy = "read") =>
    (await f.Authorization.AuthorizeAsync(user, null, policy)).Succeeded;

await Test("ACTIVE account returns current permissions", async f =>
    (await f.Service.GetPermissionsAsync(f.Id)).SequenceEqual(new[] { "case.read" }));
await Test("assigned exact code succeeds", f => f.Service.HasPermissionAsync(f.Id, "case.read"));
await Test("different code denied", async f => !await f.Service.HasPermissionAsync(f.Id, "case.update"));
await Test("code comparison is case sensitive", async f => !await f.Service.HasPermissionAsync(f.Id, "Case.Read"));
await Test("padded permission does not match", async f => !await f.Service.HasPermissionAsync(f.Id, "case.read "));
await Test("blank code denied without querying", async f =>
    !await f.Service.HasPermissionAsync(f.Id, " ") && f.Rows.Reads == 0);
await Test("empty user ID denied without querying", async f =>
    !await f.Service.HasPermissionAsync(Guid.Empty, "case.read") && f.Rows.Reads == 0);
await Test("disabled account denied by single lookup", async f =>
{
    f.Rows.Status = "DISABLED";
    return !await f.Service.HasPermissionAsync(f.Id, "case.read") && f.Rows.Reads == 1;
});
await Test("lowercase active is not ACTIVE", async f =>
{
    f.Rows.Status = "active";
    return !await f.Service.HasPermissionAsync(f.Id, "case.read");
});
await Test("missing account denied", async f => !await f.Service.HasPermissionAsync(Guid.NewGuid(), "case.read"));
await Test("missing role permission configuration denies ADMIN too", async f =>
{
    f.Rows.Names.Clear();
    return !await f.Service.HasPermissionAsync(f.Id, "case.read") && !await Allows(f, Principal(f.Id));
});
await Test("permission results filtered deduplicated and sorted", async f =>
{
    f.Rows.Names = new() { "z.read", "case.read", "case.read", "", " ", "a.read" };
    return (await f.Service.GetPermissionsAsync(f.Id)).SequenceEqual(new[] { "a.read", "case.read", "z.read" });
});
await Test("revocation re-queries in same scope", async f =>
{
    if (!await Allows(f, Principal(f.Id))) return false;
    f.Rows.Names.Clear();
    return !await Allows(f, Principal(f.Id)) && f.Rows.Reads == 2;
});
await Test("different existing user cannot inherit previous result", async f =>
    await Allows(f, Principal(f.Id)) && !await Allows(f, Principal(f.Rows.OtherId)) &&
    await f.Service.HasPermissionAsync(f.Rows.OtherId, "case.update"));
await Test("changed RoleId uses new role permissions", async f =>
{
    if (!await f.Service.HasPermissionAsync(f.Id, "case.read")) return false;
    f.Rows.CurrentRoleId = f.Rows.OtherRoleId;
    return !await f.Service.HasPermissionAsync(f.Id, "case.read") &&
        await f.Service.HasPermissionAsync(f.Id, "case.update");
});
await Test("account disabled after success denies next lookup", async f =>
{
    if (!await Allows(f, Principal(f.Id))) return false;
    f.Rows.Status = "DISABLED";
    return !await Allows(f, Principal(f.Id));
});
await Test("unauthenticated principal denied without service lookup", async f =>
    !await Allows(f, Principal(f.Id, false)) && f.Rows.Reads == 0);
await Test("authenticated principal without permission denied", async f => !await Allows(f, Principal(f.Id), "write"));
await Test("authenticated principal with permission allowed", f => Allows(f, Principal(f.Id)));
await Test("query/body resource userId cannot override principal", async f =>
    !(await f.Authorization.AuthorizeAsync(Principal(Guid.NewGuid()),
        new { userId = f.Id }, "read")).Succeeded);
await Test("claims on unauthenticated identity cannot override userId", async f =>
{
    var user = Principal(Guid.NewGuid());
    user.AddIdentity(new ClaimsIdentity(new[] { new Claim("userId", f.Id.ToString()) }));
    return !await Allows(f, user);
});
foreach (var failure in new Exception[] { new OfflineDatabaseException(), new TimeoutException("OFFLINE_PRIVATE_DETAIL") })
{
    await Test($"{failure.GetType().Name} never authorizes or exposes provider details", async f =>
    {
        f.Rows.Failure = failure;
        try { await Allows(f, Principal(f.Id)); return false; }
        catch (PermissionLookupException error)
        {
            return error.InnerException is null && error.Message == "Permission lookup is unavailable." &&
                !error.ToString().Contains("OFFLINE_PRIVATE_DETAIL");
        }
    });
}
await Test("pre-cancelled lookup propagates cancellation", async f =>
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try { await f.Service.GetPermissionsAsync(f.Id, cancellation.Token); return false; }
    catch (OperationCanceledException) { return f.Rows.Reads == 0; }
});
await Test("production DI uses scoped service handler and context", f =>
{
    using var another = f.Provider.CreateScope();
    var handler = f.Scope.ServiceProvider.GetServices<IAuthorizationHandler>()
        .OfType<PermissionAuthorizationHandler>().Single();
    return Task.FromResult(f.Service is PermissionService &&
        ReferenceEquals(f.Service, f.Scope.ServiceProvider.GetRequiredService<IPermissionService>()) &&
        !ReferenceEquals(f.Service, another.ServiceProvider.GetRequiredService<IPermissionService>()) &&
        ReferenceEquals(f.Db, f.Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()) &&
        !ReferenceEquals(f.Db, another.ServiceProvider.GetRequiredService<ApplicationDbContext>()) &&
        ReferenceEquals(handler, f.Scope.ServiceProvider.GetServices<IAuthorizationHandler>()
            .OfType<PermissionAuthorizationHandler>().Single()) &&
        !ReferenceEquals(handler, another.ServiceProvider.GetServices<IAuthorizationHandler>()
            .OfType<PermissionAuthorizationHandler>().Single()) &&
        f.Scope.ServiceProvider.GetServices<IAuthorizationHandler>().OfType<PermissionAuthorizationHandler>().Count() == 1);
});
await Test("scoped dependency cannot resolve from root", f =>
{
    try { f.Provider.GetRequiredService<IPermissionService>(); return Task.FromResult(false); }
    catch (InvalidOperationException) { return Task.FromResult(true); }
});
await Test("duplicate policy rejected without overwriting original", _ =>
{
    var options = new AuthorizationOptions();
    options.AddPermissionPolicy("Permission:read", "case.read");
    try { options.AddPermissionPolicy("Permission:read", "case.update"); return Task.FromResult(false); }
    catch (InvalidOperationException)
    {
        return Task.FromResult(options.GetPolicy("Permission:read")!.Requirements
            .OfType<PermissionRequirement>().Single().Permission == "case.read");
    }
});
await Test("case variant policy name collision rejected", _ =>
{
    var options = new AuthorizationOptions();
    options.AddPermissionPolicy("Permission:read", "case.read");
    try { options.AddPermissionPolicy("permission:READ", "case.update"); return Task.FromResult(false); }
    catch (InvalidOperationException) { return Task.FromResult(true); }
});
await Test("existing ordinary policy cannot be overwritten by permission extension", _ =>
{
    var options = new AuthorizationOptions();
    options.AddPolicy("existing", policy => policy.RequireAuthenticatedUser());
    try { options.AddPermissionPolicy("existing", "case.read"); return Task.FromResult(false); }
    catch (InvalidOperationException) { return Task.FromResult(true); }
});
await Test("padded policy name rejected", _ =>
{
    try { new AuthorizationOptions().AddPermissionPolicy(" read ", "case.read"); return Task.FromResult(false); }
    catch (ArgumentException) { return Task.FromResult(true); }
});
await Test("single EF query materializes no tracked entities", async f =>
    await f.Service.HasPermissionAsync(f.Id, "case.read") && f.Rows.Reads == 1 && !f.Db.ChangeTracker.Entries().Any());
foreach (var successFirst in new[] { true, false })
{
    await Test($"explicit denial overrides success (success first: {successFirst})", async f =>
    {
        var requirement = new PermissionRequirement("case.update");
        var context = new AuthorizationHandlerContext(new[] { requirement }, Principal(f.Id), null);
        if (successFirst) context.Succeed(requirement);
        await f.Scope.ServiceProvider.GetServices<IAuthorizationHandler>()
            .OfType<PermissionAuthorizationHandler>().Single().HandleAsync(context);
        if (!successFirst) context.Succeed(requirement);
        return context.HasFailed && !context.HasSucceeded;
    });
}
await Test("lookup failure marks context failed even after earlier handler success", async f =>
{
    var requirement = new PermissionRequirement("case.read");
    var context = new AuthorizationHandlerContext(new[] { requirement }, Principal(f.Id), null);
    context.Succeed(requirement);
    f.Rows.Failure = new OfflineDatabaseException();
    try
    {
        await f.Scope.ServiceProvider.GetServices<IAuthorizationHandler>()
            .OfType<PermissionAuthorizationHandler>().Single().HandleAsync(context);
        return false;
    }
    catch (PermissionLookupException) { return context.HasFailed && !context.HasSucceeded; }
});
Console.WriteLine($"PermissionService offline: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public CommandRows Rows { get; }
    public ServiceProvider Provider { get; }
    public IServiceScope Scope { get; }
    public IPermissionService Service => Scope.ServiceProvider.GetRequiredService<IPermissionService>();
    public ApplicationDbContext Db => Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    public IAuthorizationService Authorization => Scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
    public Fixture()
    {
        Rows = new CommandRows(Id);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        // Deliberately unusable endpoint. Opening is suppressed; all unhandled commands throw.
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseMySql("Server=invalid.invalid;Database=offline_rbac;User=offline;Pooling=False;",
                new MySqlServerVersion(new Version(8, 0, 46)))
            .AddInterceptors(new NoNetwork(), Rows));
        services.AddPermissionAuthorization();
        services.AddPermissionAuthorization();
        services.AddAuthorization(options => options.AddPermissionPolicy("read", "case.read")
            .AddPermissionPolicy("write", "case.update"));
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Scope = Provider.CreateScope();
    }
    public void Dispose() { Scope.Dispose(); Provider.Dispose(); }
}

sealed class NoNetwork : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result) => throw new InvalidOperationException("Synchronous connection forbidden");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(InterceptionResult.Suppress());
}

sealed class OfflineDatabaseException : DbException
{
    public OfflineDatabaseException() : base("OFFLINE_PRIVATE_DETAIL") { }
}

sealed class CommandRows(Guid userId) : DbCommandInterceptor
{
    public int Reads { get; private set; }
    public Exception? Failure { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public List<string> Names { get; set; } = new() { "case.read" };
    public Guid OtherId { get; } = Guid.NewGuid();
    public Guid OtherRoleId { get; } = Guid.NewGuid();
    private readonly Guid grantedRoleId = Guid.NewGuid();
    public Guid? CurrentRoleId { get; set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        if (Failure is not null) throw Failure;
        var table = new DataTable();
        var sql = command.CommandText;
        var parameter = command.Parameters.Cast<DbParameter>().Single().Value;
        if (sql.Contains("FROM `users`") && sql.Contains("INNER JOIN `roles`") &&
            sql.Contains("INNER JOIN `role_permissions`") && sql.Contains("INNER JOIN `permissions`") &&
            sql.Contains("`role_id` =") && sql.Contains("`permission_id` =") &&
            sql.Contains("WHERE") && sql.Contains("`user_id` ="))
        {
            table.Columns.Add("status", typeof(string));
            table.Columns.Add("name", typeof(string));
            var requestedId = Guid.Parse(parameter!.ToString()!);
            if (requestedId == userId || requestedId == OtherId)
            {
                var roleId = requestedId == userId ? CurrentRoleId ?? grantedRoleId : OtherRoleId;
                var names = roleId == grantedRoleId ? Names : roleId == OtherRoleId ? new List<string> { "case.update" } : new();
                foreach (var name in names) table.Rows.Add(requestedId == userId ? Status : "ACTIVE", name);
            }
        }
        else throw new InvalidOperationException("Unexpected offline query; execution forbidden");
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result) => throw new InvalidOperationException("Synchronous command forbidden");
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Writes forbidden");
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Scalar commands forbidden");
}
