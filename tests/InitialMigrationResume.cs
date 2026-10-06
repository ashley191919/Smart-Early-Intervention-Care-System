using System.Text.RegularExpressions;
using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class InitialMigrationResume
{
    // Only resume the unpublished initial migration on matching, empty local core tables.
    // MySQL DDL can remain after a failure; never drop those tables as a retry strategy.
    public static async Task RunAsync(ApplicationDbContext context, HashSet<string> existing)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        if (applied.Length != 0 || pending.Length == 0 || !pending[0].EndsWith("_InitialSharedCore", StringComparison.Ordinal) || existing.Contains("audit_logs"))
            throw new InitialMigrationResumeException("Resume only supports the unapplied InitialSharedCore migration.");
        // Validate partial initial tables against that migration, not today's expanded model.
        var migrations = context.GetService<IMigrationsAssembly>();
        var initialModel = migrations.CreateMigration(migrations.Migrations[pending[0]], context.Database.ProviderName!).TargetModel;
        foreach (var entity in initialModel.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            if (!existing.Contains(table)) continue;
            await using var count = context.Database.GetDbConnection().CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM `{table}`";
            if (Convert.ToInt64(await count.ExecuteScalarAsync()) != 0)
                throw new InitialMigrationResumeException("Partial initial table contains data; automatic resume refused.");
            await using var columns = context.Database.GetDbConnection().CreateCommand();
            columns.CommandText = "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE FROM information_schema.COLUMNS " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table";
            var parameter = columns.CreateParameter(); parameter.ParameterName = "@table"; parameter.Value = table;
            columns.Parameters.Add(parameter);
            var actual = new Dictionary<string, (string Type, bool Nullable)>(StringComparer.Ordinal);
            await using (var reader = await columns.ExecuteReaderAsync())
                while (await reader.ReadAsync()) actual.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2) == "YES"));
            if (actual.Count != entity.GetProperties().Count())
                throw new InitialMigrationResumeException("Existing table columns do not match initial schema.");
            foreach (var property in entity.GetProperties())
            {
                var name = property.GetColumnName()!;
                if (!actual.TryGetValue(name, out var column) ||
                    Normalize(column.Type) != Normalize(property.GetColumnType()!) || column.Nullable != property.IsNullable)
                    throw new InitialMigrationResumeException("Existing column definition does not match initial schema.");
            }
        }
        var indexes = new Dictionary<(string Table, string Index), (bool Unique, string Columns)>();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) " +
                "FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() " +
                "GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                indexes.Add((reader.GetString(0), reader.GetString(1)), (reader.GetInt32(2) == 0, reader.GetString(3)));
        }
        // Validate existing PK/alternate keys and FKs before skipping a CREATE TABLE.
        foreach (var entity in initialModel.GetEntityTypes().Where(e => existing.Contains(e.GetTableName()!)))
        {
            var table = entity.GetTableName()!;
            foreach (var key in entity.GetKeys())
            {
                var name = key.IsPrimaryKey() ? "PRIMARY" : key.GetName()!;
                var columns = string.Join(',', key.Properties.Select(p => p.GetColumnName()));
                if (!indexes.TryGetValue((table, name), out var actual) || !actual.Unique || actual.Columns != columns)
                    throw new InitialMigrationResumeException("Existing key does not match initial schema.");
            }
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT CONSTRAINT_NAME FROM information_schema.REFERENTIAL_CONSTRAINTS " +
                "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = @table";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@table"; parameter.Value = table;
            command.Parameters.Add(parameter);
            var actualNames = new HashSet<string>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) actualNames.Add(reader.GetString(0));
            if (!actualNames.SetEquals(entity.GetForeignKeys().Select(f => f.GetConstraintName()!)))
                throw new InitialMigrationResumeException("Existing foreign keys do not match initial schema.");
        }
        var script = context.GetService<IMigrator>().GenerateScript("0", pending[0]);
        var statements = Regex.Split(script, @";\s*(?:\r?\n|$)").Select(s => s.Trim()).Where(s => s.Length != 0).ToArray();
        // Plan and validate every skipped index before running any remaining statement.
        var plan = new List<string>();
        foreach (var statement in statements)
        {
            if (statement is "START TRANSACTION" or "COMMIT") continue;
            var create = Regex.Match(statement, @"^CREATE TABLE `([^`]+)`");
            if (create.Success && existing.Contains(create.Groups[1].Value)) continue;
            var index = Regex.Match(statement, @"^CREATE (UNIQUE )?INDEX `([^`]+)` ON `([^`]+)` \(([^)]+)\)$");
            if (index.Success && indexes.TryGetValue((index.Groups[3].Value, index.Groups[2].Value), out var actual))
            {
                var columns = Regex.Replace(index.Groups[4].Value, @"[`\s]", "");
                if (actual.Unique != index.Groups[1].Success || actual.Columns != columns)
                    throw new InitialMigrationResumeException("Existing index does not match initial schema.");
                continue;
            }
            plan.Add(statement);
        }
        foreach (var statement in plan) await context.Database.ExecuteSqlRawAsync(statement);
        Console.WriteLine("PASS: matching empty initial tables retained; remaining migration statements completed.");
    }

    private static string Normalize(string type) => Regex.Replace(type, @"\s", "").ToLowerInvariant();
}

internal sealed class InitialMigrationResumeException(string message) : InvalidOperationException(message);
