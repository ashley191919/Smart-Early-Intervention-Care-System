using Earlycare.CloudMigration;
using EarlyInterventionCare.Api.Data;

public static class EntryTests
{
    public static async Task Run()
    {
        const string secret = "SYNTHETIC-DO-NOT-PRINT-92817";
        var checks = 0;
        var originalOut = Console.Out; var originalError = Console.Error;
        var folder = Path.Combine(Path.GetTempPath(), "earlycare-entry-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        async Task Run(string[] args, Spy spy, int exit)
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            try
            {
                Console.SetOut(output); Console.SetError(error);
                if (await DeploymentEntry.RunAsync(args, spy) != exit) throw new Exception("Unexpected exit");
                if ((output.ToString() + error).Contains(secret) || (output.ToString() + error).Contains("Password="))
                    throw new Exception("Secret leak");
                if (args.Length == 1 && args[0] == "apply" && !error.ToString().Contains("APPLY_BLOCKED")) throw new Exception("Missing safe code");
                if (exit == 1 && !error.ToString().Contains(args.Length > 1 && args[0] != "script" ? "INVALID_ARGUMENT" :
                    args[0] == "script" ? "SQL_SCRIPT_WRITE_FAILED" : args[0] == "check" ? "INVALID_ENDPOINT" : "APPLY_BLOCKED"))
                    throw new Exception("Unexpected safe error code");
            }
            finally { Console.SetOut(originalOut); Console.SetError(originalError); }
            checks++;
        }
        foreach (var args in new[] { Array.Empty<string>(), new[] { "help" }, new[] { "apply" },
            new[] { "apply", "--force" }, new[] { "apply", "--guardian-approved" }, new[] { "--password", secret } })
        {
            var spy = new Spy(secret); await Run(args, spy, args.Length == 0 || args[0] == "help" ? 0 : 1);
            if (spy.EnvironmentReads != 0 || spy.PasswordReads != 0 || spy.OfflineCreates != 0 || spy.DatabaseCreates != 0 || spy.Opens != 0)
                throw new Exception("Early refusal touched dependencies");
        }
        var path = Path.Combine(folder, "review.sql");
        var script = new Spy(secret); await Run(["script", "--output", path], script, 0);
        if (script.OfflineCreates != 1 || script.EnvironmentReads != 0 || script.DatabaseCreates != 0 || script.Opens != 0 || !File.Exists(path))
            throw new Exception("Script not offline");
        var content = File.ReadAllText(path);
        await Run(["script", "--output", path], new Spy(secret), 1);
        if (File.ReadAllText(path) != content) throw new Exception("Output overwritten");
        var partial = Path.Combine(folder, "partial.sql");
        try { SafeScript.Write(partial, secret, (stream, value) => { stream.WriteByte(1); throw new IOException(secret); }); }
        catch (DeploymentException ex) { if (ex.Code != "SQL_SCRIPT_WRITE_FAILED") throw; checks++; }
        if (File.Exists(partial)) throw new Exception("Partial file retained");
        var failure = new Spy(secret) { FailWrite = true }; await Run(["script", "--output", partial], failure, 1);
        var invalid = new Spy(secret) { InvalidEndpoint = true }; await Run(["check"], invalid, 1);
        if (invalid.DatabaseCreates != 0 || invalid.PasswordReads != 0) throw new Exception("Endpoint validation too late");
        var column = new ColumnShape("tasks", "id", "char(36)", false, null, "ascii", "ascii_bin");
        var shape = new SchemaShape([column], ["fk|tasks|case_id|cases|id|RESTRICT"], ["PRIMARY|1|id"]);
        SchemaComparison.RequireEqual(shape, shape); checks++;
        void Code(Action action, string expected)
        {
            try { action(); throw new Exception("Expected safe error"); }
            catch (DeploymentException error) { if (error.Code != expected) throw; checks++; }
        }
        Code(() => DeploymentPolicy.ValidateServer("8.0.46", "earlycare_dev", "0"), "INVALID_DATABASE_VERSION");
        Code(() => DeploymentPolicy.ValidateHistory(["a", "b"], ["b"]), "MIGRATION_HISTORY_MISMATCH");
        foreach (var altered in new[] {
            shape with { Columns = [column, column with { Name = "extra" }] },
            shape with { Columns = [column with { Default = "wrong" }] },
            shape with { Columns = [column with { Charset = "utf8mb4" }] },
            shape with { Columns = [column with { Collation = "ascii_general_ci" }] },
            shape with { ForeignKeys = [] }, shape with { ForeignKeys = ["fk|tasks|case_id|other|id|CASCADE"] },
            shape with { Indexes = ["PRIMARY|0|id"] }, shape with { Indexes = ["PRIMARY|1|case_id,id"] } })
        {
            try { SchemaComparison.RequireEqual(shape, altered); throw new Exception("Drift accepted"); }
            catch (DeploymentException ex) { if (ex.Code != "SCHEMA_DRIFT") throw; checks++; }
        }
        File.Delete(path); Directory.Delete(folder);
        Console.WriteLine($"PASS: {checks} real-entry/script/schema offline scenarios; injected connection factory never called.");
    }
    private sealed class Spy(string secret) : DeploymentDependencies
    {
        public int EnvironmentReads, PasswordReads, OfflineCreates, DatabaseCreates, Opens;
        public bool FailWrite, InvalidEndpoint;
        public override string? EnvironmentValue(string name)
        {
            EnvironmentReads++;
            return name.EndsWith("HOST") ? (InvalidEndpoint ? "unsafe" : "127.0.0.1") : name.EndsWith("PORT") ? "13306" :
                name.EndsWith("DATABASE") ? "earlycare_dev" : name.EndsWith("USER") ? "earlycare_migration" : secret;
        }
        public override string PasswordInput() { PasswordReads++; throw new Exception(secret); }
        public override ApplicationDbContext OfflineContext() { OfflineCreates++; return base.OfflineContext(); }
        public override ApplicationDbContext DatabaseContext(string password) { DatabaseCreates++; throw new Exception(secret); }
        public override Task OpenConnection(ApplicationDbContext db) { Opens++; throw new Exception(secret); }
        public override void WriteScript(string path, string sql)
        { if (FailWrite) throw new IOException(secret); base.WriteScript(path, sql); }
    }
}
