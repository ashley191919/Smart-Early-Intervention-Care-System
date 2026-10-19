using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HistoryAcceptance;

internal static class OfflineChecks
{
    internal static async Task RunAsync()
    {
        var count = 0;
        void Check(bool condition, string code) { Safety.Require(condition, code); count++; }
        var mysqlCalls = 0;
        Task Spy(Guid _) { mysqlCalls++; return Task.CompletedTask; }
        var originalError = Console.Error;
        using var expectedErrors = new StringWriter();
        try
        {
            Console.SetError(expectedErrors);
            foreach (var invalid in new[]
            {
                new[] { "--mysql" }, new[] { "--apply" }, new[] { "--database", "earlycare_dev" },
                new[] { "--mysql", "--server-uuid", Guid.Empty.ToString() },
                new[] { "--mysql", "--server-uuid", Guid.NewGuid().ToString(), "--password", "synthetic-secret-marker" },
                new[] { "--mysql", "--server-uuid", "not-a-uuid" }
            })
                Check(await Entry.RunAsync(invalid, Spy) == 1 && mysqlCalls == 0, "ARGUMENT_GATE");
        }
        finally { Console.SetError(originalError); }
        Check(!expectedErrors.ToString().Contains("synthetic-secret-marker"), "ARGUMENT_SECRET_NOT_LOGGED");
        Check(await Entry.RunAsync(["--help"], Spy) == 0 && mysqlCalls == 0, "HELP_GATE");
        var uuid = Guid.NewGuid();
        Check(await Entry.RunAsync(["--mysql", "--server-uuid", uuid.ToString()], Spy) == 0 && mysqlCalls == 1, "INJECTED_DISPATCH_ONLY");
        var privileges = "SELECT, INSERT, UPDATE, DELETE, CREATE, REFERENCES, INDEX, ALTER";
        string Grant(string rights, string scope) => $"GRANT {rights} ON {scope} TO `earlycare_history_acceptance`@`127.0.0.1`";
        var valid = Safety.Databases.Select(db => Grant(privileges, $"`{db.Replace("_", "\\_")}`.*")).ToArray();
        Safety.ValidateGrants(valid); count++;
        Safety.ValidateGrants(Safety.Databases.Select(db => Grant(privileges, $"`{db.Replace("_", "\\_")}`.*"))); count++;
        foreach (var extra in new[]
        {
            Grant("ALL PRIVILEGES", "*.*"), Grant(privileges, "`earlycare_dev`.*"),
            Grant(privileges, $"`{Safety.EmptyDatabase}`.*"), Grant(privileges, "`earlycare%`.*"),
            Grant("SELECT", "*.*"), valid[0] + " WITH GRANT OPTION", "GRANT `some_role` TO `earlycare_history_acceptance`@`127.0.0.1`"
        })
        {
            try { Safety.ValidateGrants(valid.Append(extra)); throw new Exception("Gate unexpectedly accepted grant."); }
            catch (AcceptanceFailure) { count++; }
        }
        try { Safety.ValidateGrants(valid.Take(1)); throw new Exception("Missing grant accepted."); }
        catch (AcceptanceFailure) { count++; }
        Safety.ValidateGrants(valid.Append(Grant("USAGE", "*.*"))); count++;
        var identity = new[] { Safety.EmptyDatabase, Safety.Account + "@localhost", uuid.ToString(), "8.0.46", "1", "1", "1", "0", "STRICT_TRANS_TABLES" };
        Safety.ValidateIdentity(identity, Safety.EmptyDatabase, uuid); count++;
        foreach (var change in new[] { (0, "earlycare_dev"), (1, "root@localhost"), (1, "earlycare_dev_user@localhost"),
            (2, Guid.NewGuid().ToString()), (3, "8.4.11"), (3, "8.0.460"), (4, "0"),
            (5, "0"), (6, "0"), (7, "1"), (8, "NO_ENGINE_SUBSTITUTION") })
        {
            var invalid = (string[])identity.Clone(); invalid[change.Item1] = change.Item2;
            try { Safety.ValidateObservation(invalid, valid, Safety.EmptyDatabase, uuid, true); throw new Exception("Unsafe identity accepted."); }
            catch (AcceptanceFailure) { count++; }
        }
        try { Safety.ValidateIdentity(identity, "earlycare_dev", uuid); throw new Exception("Unsafe target accepted."); }
        catch (AcceptanceFailure) { count++; }

        Safety.ValidateObservation(identity, valid, Safety.EmptyDatabase, uuid, true); count++;
        try { Safety.ValidateObservation(identity, valid, Safety.EmptyDatabase, uuid, false); throw new Exception("Lost lease accepted."); }
        catch (AcceptanceFailure) { count++; }
        Safety.RequireEmpty(0); count++;
        try { Safety.RequireEmpty(1); throw new Exception("Post-preflight objects accepted."); }
        catch (AcceptanceFailure) { count++; }
        var releases = 0; var bodies = 0;
        try
        {
            await Safety.WithLeaseAsync(() => Task.FromResult(false), () => { releases++; return Task.CompletedTask; }, () => { bodies++; return Task.CompletedTask; });
            throw new Exception("Failed mutex accepted.");
        }
        catch (AcceptanceFailure) { Check(releases == 0 && bodies == 0, "MUTEX_FAIL_NO_BODY"); }
        await Safety.WithLeaseAsync(() => Task.FromResult(true), () => { releases++; return Task.CompletedTask; }, () => { bodies++; return Task.CompletedTask; });
        Check(releases == 1 && bodies == 1, "MUTEX_SUCCESS_RELEASE");
        try
        {
            await Safety.WithLeaseAsync(() => Task.FromResult(true), () => { releases++; return Task.CompletedTask; }, () => throw new AcceptanceFailure("INJECTED"));
        }
        catch (AcceptanceFailure) { Check(releases == 2, "MUTEX_EXCEPTION_RELEASE"); }

        foreach (var bad in new[] { new[] { "UNKNOWN" }, Safety.Migrations.Take(2).ToArray(), new[] { Safety.Migrations[1], Safety.Migrations[0] } })
        {
            var writes = 0;
            try
            {
                await Safety.ApplyForwardAsync(1, () => Task.FromResult(bad), _ => { writes++; return Task.CompletedTask; });
                throw new Exception("Unexpected prefix accepted.");
            }
            catch (AcceptanceFailure) { Check(writes == 0, "PREFIX_REJECTS_BEFORE_WRITE"); }
        }
        for (var i = 0; i < Safety.Migrations.Length; i++)
        {
            var applied = Safety.Migrations.Take(i).ToArray(); var selected = -1;
            await Safety.ApplyForwardAsync(i, () => Task.FromResult(applied), index =>
            {
                selected = index; _ = Safety.ForwardSql(index); applied = Safety.Migrations.Take(index + 1).ToArray(); return Task.CompletedTask;
            });
            Check(selected == i, "FIXED_FORWARD_SCRIPT");
        }
        var noopWrites = 0;
        await Safety.ApplyForwardAsync(5, () => Task.FromResult(Safety.Migrations), _ => { noopWrites++; return Task.CompletedTask; });
        Check(noopWrites == 0, "LATEST_NOOP_NO_DISPATCH");
        // External intervention AFTER the prefix check cannot cause a Down dispatch: executor is fixed forward SQL.
        var reads = 0; var forwardOnly = false;
        try
        {
            await Safety.ApplyForwardAsync(1, () => Task.FromResult(++reads == 1 ? Safety.Migrations.Take(1).ToArray() : Safety.Migrations), index =>
            {
                forwardOnly = index == 1 && !Safety.ForwardSql(index).Contains("DROP TABLE");
                return Task.CompletedTask;
            });
            throw new Exception("External history change accepted.");
        }
        catch (AcceptanceFailure) { Check(forwardOnly && reads == 2, "RACE_NO_DOWN_AND_POSTCHECK_FAILS"); }
        originalError = Console.Error;
        using var failures = new StringWriter();
        try
        {
            Console.SetError(failures);
            foreach (var error in new Exception[] { new AcceptanceFailure("INJECTED"), new Exception("synthetic-secret-marker"), new AcceptanceFailure("synthetic-secret-marker") })
                Check(await Entry.RunAsync(["--mysql", "--server-uuid", uuid.ToString()], _ => throw error) == 1, "SAFE_FAILURE_EXIT");
        }
        finally { Console.SetError(originalError); }
        Check(failures.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line =>
            line.Contains("scenario=") && line.Contains("step=") && line.Contains("PARTIAL_STATE_POSSIBLE") && !line.Contains("synthetic-secret-marker")), "SAFE_FAILURE_FORMAT");

        // Exercise the actual EF interceptors with CLOSED connection objects. No Open or password input.
        await using (var closed = new VerifiedSession("Server=127.0.0.1;Port=33328;Pooling=false", Safety.EmptyDatabase, uuid, null))
        {
            async Task Denied(Func<Task> operation, string code)
            {
                try { await operation(); throw new Exception("Unsafe operation accepted."); }
                catch (AcceptanceFailure failure) { Check(failure.Code == code, "ACTUAL_INTERCEPTOR_GATE"); }
            }
            await Denied(() => closed.GuardAsync(), "CONNECTION_LOST_NO_RECONNECT");
            using var ownCommand = closed.Connection.CreateCommand();
            var guard = new CommandGuard(closed);
            await Denied(async () => { await guard.ReaderExecutingAsync(ownCommand, null!, default); }, "CONNECTION_LOST_NO_RECONNECT");
            using var other = new MySqlConnector.MySqlConnection("Server=127.0.0.1;Pooling=false");
            using var foreignCommand = other.CreateCommand();
            await Denied(async () => { await guard.NonQueryExecutingAsync(foreignCommand, null!, default); }, "UNVERIFIED_CONNECTION");
            await Denied(async () => { await new RejectReopen().ConnectionOpeningAsync(closed.Connection, null!, default); }, "REOPEN_FORBIDDEN");
            await Denied(async () => { await new TransactionGuard(closed).TransactionStartingAsync(other, null!, default); }, "UNVERIFIED_CONNECTION");
            await Denied(() => { guard.ScalarExecuting(ownCommand, null!, default); return Task.CompletedTask; }, "SYNC_COMMAND_FORBIDDEN");
        }

        await using var db = new ApplicationDbContextFactory().CreateDbContext([]);
        Check(db.Database.GetMigrations().SequenceEqual(Safety.Migrations), "MIGRATION_CHAIN");
        Check(!db.Database.HasPendingModelChanges(), "MODEL_SNAPSHOT");
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[Safety.Migrations[^1]], db.Database.ProviderName!);
        var up = migration.UpOperations;
        Check(up.Count == 3 && up.OfType<CreateTableOperation>().Count() == 1 && up.OfType<CreateIndexOperation>().Count() == 2, "MINIMAL_UP");
        var table = up.OfType<CreateTableOperation>().Single();
        Check(table.Name == "case_status_histories" && table.Columns.Count == 8 && table.PrimaryKey!.Columns.SequenceEqual(new[] { "history_id" }), "HISTORY_TABLE");
        Check(table.ForeignKeys.Count == 2 && table.ForeignKeys.All(f => f.OnDelete == ReferentialAction.Restrict), "RESTRICT_FOREIGN_KEYS");
        Check(table.ForeignKeys.Any(f => f.Name == "fk_case_status_histories_case" && f.Columns.SequenceEqual(new[] { "case_id" }) &&
            f.PrincipalTable == "cases" && f.PrincipalColumns!.SequenceEqual(new[] { "case_id" })) &&
            table.ForeignKeys.Any(f => f.Name == "fk_case_status_histories_changed_by_user" && f.Columns.SequenceEqual(new[] { "changed_by_user_id" }) &&
            f.PrincipalTable == "users" && f.PrincipalColumns!.SequenceEqual(new[] { "user_id" })), "FK_TARGETS");
        var indexes = up.OfType<CreateIndexOperation>().ToArray();
        Check(indexes.All(i => i.Table == table.Name && !i.IsUnique) &&
            indexes.Any(i => i.Name == "ix_case_status_histories_case_time" && i.Columns.SequenceEqual(new[] { "case_id", "changed_at_utc", "history_id" })) &&
            indexes.Any(i => i.Name == "ix_case_status_histories_changed_by_user" && i.Columns.SequenceEqual(new[] { "changed_by_user_id" })), "INDEX_OPERATIONS");
        Check(migration.DownOperations.Count == 1 && migration.DownOperations.Single() is DropTableOperation { Name: "case_status_histories" }, "MINIMAL_DOWN");
        var migrator = db.GetService<IMigrator>();
        var sql = migrator.GenerateScript(Safety.Migrations[^2], Safety.Migrations[^1]);
        Check(sql.Contains("CREATE TABLE `case_status_histories`") && !sql.Contains("ALTER TABLE") && !sql.Contains("DROP TABLE") &&
            !sql.Contains("INSERT INTO `roles`") && !sql.Contains("UPDATE `"), "DELTA_SQL");
        Check(Safety.Databases.All(name => name != "earlycare_dev") && Safety.Port != 3306 && Safety.Account != "root", "FIXED_ISOLATION");
        Console.WriteLine($"PASS OFFLINE: {count} checks; no credential input or physical MySQL connection opened. MYSQL ACCEPTANCE: NOT RUN.");
    }
}
