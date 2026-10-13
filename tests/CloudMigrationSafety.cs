using Earlycare.CloudMigration;
using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;

var count = 0;
void Check(bool ok) { if (!ok) throw new Exception("Safety check failed"); count++; }
void Reject(Action action) { try { action(); } catch (InvalidOperationException) { count++; return; } throw new Exception("Unsafe input accepted"); }
Check(DeploymentPolicy.Mode([]) == "help");
foreach (var mode in new[] { "check", "history", "preflight", "apply" }) Check(DeploymentPolicy.Mode([mode]) == mode);
Check(DeploymentPolicy.Mode(["script", "--output", "review.sql"]) == "script");
foreach (var input in new[] { new[] { "--seed" }, new[] { "--configure" }, new[] { "apply", "--force" }, new[] { "history", "--apply" }, new[] { "--password", "synthetic" } })
    Reject(() => DeploymentPolicy.Mode(input));
DeploymentPolicy.ValidateEndpoint("127.0.0.1", "13306", "earlycare_dev", "earlycare_migration"); count++;
foreach (var host in new[] { "localhost", "35.212.247.112", "127.0.0.1,other", "::1", "" })
    Reject(() => DeploymentPolicy.ValidateEndpoint(host, "13306", "earlycare_dev", "earlycare_migration"));
Reject(() => DeploymentPolicy.ValidateEndpoint("127.0.0.1", "3306", "earlycare_dev", "earlycare_migration"));
Reject(() => DeploymentPolicy.ValidateEndpoint("127.0.0.1", "13306", "production", "earlycare_migration"));
Reject(() => DeploymentPolicy.ValidateEndpoint("127.0.0.1", "13306", "earlycare_dev", "root"));
Reject(DeploymentPolicy.RequireApplyApproval);
DeploymentPolicy.ValidateHistory(["a", "b"], []); count++;
DeploymentPolicy.ValidateHistory(["a", "b"], ["a"]); count++;
foreach (var history in new[] { new[] { "b" }, new[] { "a", "a" }, new[] { "a", "b", "c" } })
    Reject(() => DeploymentPolicy.ValidateHistory(["a", "b"], history));
using var context = new ApplicationDbContextFactory().CreateDbContext([]);
Check(context.Database.GetMigrations().Count() == 4);
Check(!context.Database.HasPendingModelChanges());
// No ConnectionOpening or database calls: policy and actual EF model checks are offline.
Console.WriteLine($"PASS: {count} offline deployment policy/model checks; no database connections.");
await EntryTests.Run();
