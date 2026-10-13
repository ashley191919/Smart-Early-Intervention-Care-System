using Earlycare.CloudMigration;
using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using MySqlConnector;

namespace Earlycare.CloudMigration;
public static class DeploymentEntry
{
public static async Task<int> RunAsync(string[] args, DeploymentDependencies dependencies)
{
try
{
    var mode = DeploymentPolicy.Mode(args);
    if (mode == "help")
    {
        Console.WriteLine("Modes: check | history | preflight | script --output <new-file> | apply (blocked pending Guardian review). No mode opens a connection by default.");
        return 0;
    }
    // Reject before reading credentials or opening any connection.
    if (mode == "apply") DeploymentPolicy.RequireApplyApproval();
    using var offline = dependencies.OfflineContext();
    if (offline.Database.HasPendingModelChanges()) throw new DeploymentException("SCHEMA_DRIFT");
    var known = offline.Database.GetMigrations().ToArray();
    var assembly = offline.GetService<IMigrationsAssembly>();
    if (mode == "script")
    {
        // Offline full baseline script. No history inference, credentials or database connection.
        var sql = offline.GetService<IMigrator>().GenerateScript("0", known.Last());
        try { dependencies.WriteScript(args[2], sql); }
        catch (DeploymentException) { throw; }
        catch { throw new DeploymentException("SQL_SCRIPT_WRITE_FAILED"); }
        Console.WriteLine("Generated EF baseline review script (0 -> latest); never execute blindly against a nonempty database.");
        return 0;
    }
    string? Env(string name) => dependencies.EnvironmentValue("EARLYCARE_DEPLOY_" + name);
    DeploymentPolicy.ValidateEndpoint(Env("HOST"), Env("PORT"), Env("DATABASE"), Env("USER"));
    var password = Env("PASSWORD");
    if (string.IsNullOrEmpty(password)) password = dependencies.PasswordInput();
    using var db = dependencies.DatabaseContext(password);
    await dependencies.OpenConnection(db);
    async Task<List<string[]>> Read(string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = sql;
        cmd.CommandTimeout = 30;
        var rows = new List<string[]>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!).ToArray());
        return rows;
    }
    var server = (await Read("SELECT VERSION(), DATABASE(), @@lower_case_table_names"))[0];
    DeploymentPolicy.ValidateServer(server[0], server[1], server[2]);
    Console.WriteLine("PASS: target version 8.4.11 / earlycare_dev / case-sensitive table names.");
    if (mode == "check") return 0;
    var tables = (await Read("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"))
        .Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
    var applied = Array.Empty<string>();
    if (tables.Contains("__EFMigrationsHistory"))
    {
        applied = (await Read("SELECT MigrationId FROM `__EFMigrationsHistory` ORDER BY MigrationId")).Select(r => r[0]).ToArray();
    }
    else if (tables.Count > 0) throw new DeploymentException("MIGRATION_HISTORY_MISMATCH");
    DeploymentPolicy.ValidateHistory(known, applied);
    if (applied.Length == 0 && tables.Any(t => t != "__EFMigrationsHistory"))
        throw new DeploymentException("SCHEMA_DRIFT");
    foreach (var id in applied) Console.WriteLine("Applied: " + id);
    foreach (var id in known.Skip(applied.Length)) Console.WriteLine("Pending: " + id);
    if (mode == "history") return 0;
    if (applied.Length > 0)
    {
        // Compare with the applied migration's target, not the future/runtime model.
        var model = assembly.CreateMigration(assembly.Migrations[applied.Last()], offline.Database.ProviderName!).TargetModel;
        var expected = model.GetEntityTypes().Select(e => e.GetTableName()!).ToHashSet(StringComparer.Ordinal);
        if (!tables.SetEquals(expected.Append("__EFMigrationsHistory"))) throw new DeploymentException("SCHEMA_DRIFT");
        var actualColumns = await Read("SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE()");
        if (actualColumns.Count(r => r[0] != "__EFMigrationsHistory") != model.GetEntityTypes().Sum(e => e.GetProperties().Count()))
            throw new DeploymentException("SCHEMA_DRIFT");
        foreach (var entity in model.GetEntityTypes()) foreach (var property in entity.GetProperties())
        {
            var row = actualColumns.SingleOrDefault(r => r[0] == entity.GetTableName() && r[1] == property.GetColumnName());
            if (row is null || row[2].Replace(" ", "").ToLowerInvariant() != property.GetColumnType()!.Replace(" ", "").ToLowerInvariant()
                || (row[3] == "YES") != property.IsNullable) throw new DeploymentException("SCHEMA_DRIFT");
        }
        var shapeColumns = (await Read("SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE,COALESCE(COLUMN_DEFAULT,'__SQL_NULL__'),CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME <> '__EFMigrationsHistory'"))
            .Select(r => new ColumnShape(r[0], r[1], ExpectedSchema.Type(r[2]), r[3] == "YES",
                r[4] == "__SQL_NULL__" ? null : r[4], r[5] == "" ? null : r[5], r[6] == "" ? null : r[6])).ToArray();
        var shapeFks = (await Read("SELECT k.TABLE_NAME,k.CONSTRAINT_NAME,GROUP_CONCAT(k.COLUMN_NAME ORDER BY k.ORDINAL_POSITION),k.REFERENCED_TABLE_NAME,GROUP_CONCAT(k.REFERENCED_COLUMN_NAME ORDER BY k.ORDINAL_POSITION),r.DELETE_RULE,r.UPDATE_RULE FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.TABLE_NAME=k.TABLE_NAME AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME WHERE k.CONSTRAINT_SCHEMA=DATABASE() GROUP BY k.TABLE_NAME,k.CONSTRAINT_NAME,k.REFERENCED_TABLE_NAME,r.DELETE_RULE,r.UPDATE_RULE"))
            .Select(r => string.Join('|', r)).ToArray();
        var shapeIndexes = (await Read("SELECT TABLE_NAME,INDEX_NAME,IF(NON_UNIQUE=0,'1','0'),GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME <> '__EFMigrationsHistory' GROUP BY TABLE_NAME,INDEX_NAME,NON_UNIQUE"))
            .Select(r => string.Join('|', r)).ToArray();
        SchemaComparison.RequireEqual(ExpectedSchema.FromModel(model), new(shapeColumns, shapeFks, shapeIndexes));
        // Metadata output contains no business rows or credentials; operator reviews full definitions.
        foreach (var row in await Read("SELECT TABLE_NAME,CONSTRAINT_NAME,REFERENCED_TABLE_NAME,DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE()"))
            Console.WriteLine("FK: " + string.Join(" / ", row));
        foreach (var row in await Read("SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,SEQ_IN_INDEX,COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX"))
            Console.WriteLine("Index: " + string.Join(" / ", row));
        foreach (var row in await Read("SELECT TABLE_NAME,COLUMN_NAME,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND COLLATION_NAME IS NOT NULL"))
            Console.WriteLine("Encoding: " + string.Join(" / ", row));
    }
    Console.WriteLine("Read-only preflight complete; FK/index/encoding metadata requires manual review.");
    return 0;
}
catch (Exception exception)
{
    // Never emit provider/IO exception messages: these can contain credentials or environment values.
    Console.Error.WriteLine(exception is DeploymentException safe ? safe.Code : "CHECK_FAILED");
    Console.Error.WriteLine("No Migration was executed; Apply is blocked.");
    return 1;
}

}
}
