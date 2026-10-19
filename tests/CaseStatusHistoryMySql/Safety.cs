using System.Text.RegularExpressions;

namespace HistoryAcceptance;

internal sealed class AcceptanceFailure(string code) : Exception
{
    internal string Code { get; } = code;
}

internal static class Safety
{
    internal const string Host = "127.0.0.1";
    internal const uint Port = 33328;
    internal const string Account = "earlycare_history_acceptance";
    internal const string EmptyDatabase = "earlycare_history_empty_20261019";
    internal const string UpgradeDatabase = "earlycare_history_upgrade_20261019";
    internal const string Confirmation = "APPROVED ISOLATED HISTORY MYSQL 33328";
    internal static readonly string[] Databases = [EmptyDatabase, UpgradeDatabase];
    internal static readonly string[] Migrations =
    [
        "20261005152359_InitialSharedCore",
        "20261005180101_AddAuditLogs",
        "20261005184434_AddSubmissionReceiptWindow",
        "20261006074424_IntegrateIdentityAndAuthorization",
        "20261019131342_AddCaseStatusHistories"
    ];

    internal static Guid ParseMySqlArgs(string[] args)
    {
        if (args.Length != 3 || args[0] != "--mysql" || args[1] != "--server-uuid" ||
            !Guid.TryParseExact(args[2], "D", out var uuid) || uuid == Guid.Empty)
            throw new AcceptanceFailure("INVALID_ARGUMENTS");
        return uuid;
    }

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw new AcceptanceFailure(code);
    }

    internal static void ValidateIdentity(string[] identity, string database, Guid uuid)
    {
        Require(Databases.Contains(database, StringComparer.Ordinal) && identity.Length == 9, "INVALID_PREFLIGHT_TARGET");
        Require(identity[0] == database && identity[1].StartsWith(Account + "@", StringComparison.Ordinal), "SERVER_ACCOUNT_DATABASE_MISMATCH");
        Require(Guid.TryParse(identity[2], out var actual) && actual == uuid && identity[3].Split('-')[0] == "8.0.46", "SERVER_IDENTITY_VERSION_MISMATCH");
        Require(identity[4] == "1", "FOREIGN_KEYS_DISABLED");
        Require(identity[5] == "1" && identity[6] == "1" && identity[7] == "0", "UNSAFE_SESSION_SETTINGS");
        Require(identity[8].Split(',').Any(mode => mode is "STRICT_TRANS_TABLES" or "STRICT_ALL_TABLES"), "STRICT_SQL_MODE_REQUIRED");
    }

    internal static void ValidateGrants(IEnumerable<string> grants)
    {
        var allowed = new HashSet<string>(["SELECT", "INSERT", "UPDATE", "DELETE", "CREATE", "ALTER", "INDEX", "REFERENCES"], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in grants)
        {
            // Fail closed on roles, PROXY, global rights, table grants and GRANT OPTION.
            var match = Regex.Match(line, "^GRANT (.+) ON (.+) TO `earlycare_history_acceptance`@`[^`]+`$", RegexOptions.CultureInvariant);
            Require(match.Success, "UNEXPECTED_ACCOUNT_GRANT");
            var privileges = match.Groups[1].Value;
            var scope = match.Groups[2].Value;
            if (privileges == "USAGE" && scope == "*.*") continue;
            // partial_revokes=OFF is required by ValidateIdentity. All underscores must be literal.
            var database = Databases.SingleOrDefault(db => scope == $"`{db.Replace("_", "\\_")}`.*");
            Require(database is not null, "NON_ISOLATED_GRANT");
            var actual = privileges.Split(", ", StringSplitOptions.None).ToHashSet(StringComparer.Ordinal);
            Require(actual.SetEquals(allowed), "UNEXPECTED_DATABASE_PRIVILEGES");
            seen.Add(database!);
        }
        Require(seen.SetEquals(Databases), "MISSING_DATABASE_GRANT");
    }

    internal const string LockName = "earlycare_history_acceptance_20261019";
    internal static string Scenario { get; set; } = "ENTRY";
    internal static string Step { get; set; } = "ARGUMENTS";
    internal static string Failure(string code)
    {
        static string Safe(string value) => Regex.IsMatch(value, "^[A-Z0-9_]+$") ? value : "UNEXPECTED";
        return $"FAIL scenario={Safe(Scenario)} step={Safe(Step)} code={Safe(code)}; PARTIAL_STATE_POSSIBLE: Schema, Migration or synthetic data may remain. Stop and inspect; no automatic cleanup.";
    }

    internal static void ValidateObservation(string[] identity, IEnumerable<string> grants, string database, Guid uuid, bool ownsLease)
    {
        ValidateIdentity(identity, database, uuid);
        ValidateGrants(grants);
        Require(ownsLease, "MUTEX_LOST");
    }

    internal static async Task WithLeaseAsync(Func<Task<bool>> acquire, Func<Task> release, Func<Task> body)
    {
        Require(await acquire(), "MUTEX_UNAVAILABLE");
        try { await body(); }
        finally { await release(); }
    }

    internal static void RequireEmpty(long objects) => Require(objects == 0, "DATABASE_NOT_EMPTY");

    internal static void ValidatePrefix(IReadOnlyList<string> applied, int expected)
    {
        Require(expected >= 0 && expected <= Migrations.Length && applied.Count == expected &&
            applied.SequenceEqual(Migrations.Take(expected)), "UNEXPECTED_MIGRATION_PREFIX");
    }

    // No state-driven EF Migrate/Down dispatch. The injected executor can only select one fixed forward script.
    internal static async Task ApplyForwardAsync(int expected, Func<Task<string[]>> read, Func<int, Task> execute)
    {
        ValidatePrefix(await read(), expected);
        if (expected == Migrations.Length) return;
        await execute(expected);
        ValidatePrefix(await read(), expected + 1);
    }

    internal static string ForwardSql(int index)
    {
        Require(index >= 0 && index < Migrations.Length, "FORWARD_INDEX_INVALID");
        using var db = new EarlyInterventionCare.Api.Data.ApplicationDbContextFactory().CreateDbContext([]);
        Require(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetMigrations(db.Database).SequenceEqual(Migrations), "COMPILED_MIGRATION_CHAIN");
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db.Database);
        var sql = migrator.GenerateScript(index == 0 ? "0" : Migrations[index - 1], Migrations[index]);
        Require(!Regex.IsMatch(sql, @"(?im)^\s*(DROP|TRUNCATE|DELETE|UPDATE|USE)\b"), "FORBIDDEN_FORWARD_SQL");
        return sql;
    }
}
