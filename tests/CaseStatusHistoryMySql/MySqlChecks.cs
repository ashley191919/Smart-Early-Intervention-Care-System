using System.Globalization;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Models.Authorization;
using EarlyInterventionCare.Api.Services.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace HistoryAcceptance;

// A session owns one physical connection. Contexts borrow it; reconnect/retry is not permitted.
internal sealed class VerifiedSession : IAsyncDisposable
{
    internal MySqlConnection Connection { get; }
    private readonly string database;
    private readonly Guid uuid;
    internal long? OwnerId { get; private set; }
    internal VerifiedSession(string connection, string database, Guid uuid, long? ownerId)
    { Connection = new MySqlConnection(connection); this.database = database; this.uuid = uuid; OwnerId = ownerId; }

    internal static async Task<VerifiedSession> OpenAsync(string connection, string database, Guid uuid, long? ownerId = null)
    {
        var session = new VerifiedSession(connection, database, uuid, ownerId);
        try
        {
            await session.Connection.OpenAsync();
            await session.GuardAsync(requireLease: ownerId.HasValue);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    internal async Task GuardAsync(DbTransaction? transaction = null, bool requireLease = true)
    {
        Safety.Require(Connection.State == ConnectionState.Open, "CONNECTION_LOST_NO_RECONNECT");
        var identity = (await MySqlChecks.RawQueryAsync(Connection,
            "SELECT DATABASE(),CURRENT_USER(),@@server_uuid,@@version,@@foreign_key_checks,@@unique_checks,@@autocommit,@@global.partial_revokes,@@sql_mode", transaction)).Single();
        var grants = (await MySqlChecks.RawQueryAsync(Connection, "SHOW GRANTS FOR CURRENT_USER()", transaction)).Select(r => r[0]);
        var owns = !requireLease;
        if (requireLease && OwnerId.HasValue)
        {
            var owner = (await MySqlChecks.RawQueryAsync(Connection, "SELECT IS_USED_LOCK(@name)", transaction, ("@name", Safety.LockName))).Single()[0];
            owns = owner == OwnerId.Value.ToString(CultureInfo.InvariantCulture);
        }
        Safety.ValidateObservation(identity, grants, database, uuid, owns);
    }

    internal async Task<bool> AcquireAsync()
    {
        await GuardAsync(requireLease: false);
        var result = (await MySqlChecks.RawQueryAsync(Connection, "SELECT GET_LOCK(@name,0),CONNECTION_ID()", null, ("@name", Safety.LockName))).Single();
        if (result[0] != "1") return false;
        OwnerId = long.Parse(result[1], CultureInfo.InvariantCulture);
        return true;
    }

    internal async Task ReleaseAsync()
    {
        // Cleanup never reconnects. Server releases connection-scoped locks on disconnect.
        if (Connection.State != ConnectionState.Open) return;
        var result = (await MySqlChecks.RawQueryAsync(Connection, "SELECT RELEASE_LOCK(@name)", null, ("@name", Safety.LockName))).Single()[0];
        Safety.Require(result == "1", "MUTEX_RELEASE_FAILED");
    }
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}

internal sealed class RejectReopen : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        => throw new AcceptanceFailure("REOPEN_FORBIDDEN");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => throw new AcceptanceFailure("REOPEN_FORBIDDEN");
}

internal sealed class CommandGuard(VerifiedSession session) : DbCommandInterceptor
{
    private async Task Check(DbCommand command)
    {
        Safety.Require(ReferenceEquals(command.Connection, session.Connection), "UNVERIFIED_CONNECTION");
        await session.GuardAsync(command.Transaction);
    }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { await Check(command); return result; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { await Check(command); return result; }
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { await Check(command); return result; }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        => throw new AcceptanceFailure("SYNC_COMMAND_FORBIDDEN");
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        => throw new AcceptanceFailure("SYNC_COMMAND_FORBIDDEN");
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        => throw new AcceptanceFailure("SYNC_COMMAND_FORBIDDEN");
}

internal sealed class TransactionGuard(VerifiedSession session) : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        Safety.Require(ReferenceEquals(connection, session.Connection), "UNVERIFIED_CONNECTION");
        await session.GuardAsync(); return result;
    }
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    { await session.GuardAsync(transaction); return result; }
    public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        => throw new AcceptanceFailure("SYNC_TRANSACTION_FORBIDDEN");
    public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        => throw new AcceptanceFailure("SYNC_TRANSACTION_FORBIDDEN");
}

internal static class MySqlChecks
{
    private static readonly DateTime SyntheticTime = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

    internal static async Task RunAsync(Guid expectedServerUuid)
    {
        Safety.Scenario = "MYSQL"; Safety.Step = "CONFIRMATION";
        Safety.Require(!Console.IsInputRedirected, "INTERACTIVE_CONFIRMATION_REQUIRED");
        Console.WriteLine($"ISOLATED WRITE TEST: {Safety.Host}:{Safety.Port}; account={Safety.Account}; databases={string.Join(',', Safety.Databases)}");
        Console.WriteLine($"Expected MySQL 8.0.46 server UUID: {expectedServerUuid:D}");
        Console.WriteLine($"Type exactly: {Safety.Confirmation}");
        Safety.Require(Console.ReadLine() == Safety.Confirmation, "CONFIRMATION_REQUIRED");
        Safety.Step = "PASSWORD_INPUT";
        // Reached only after explicit mode, UUID and operator confirmation. Never read env/config secrets.
        Console.Write("Dedicated isolated account password (not echoed): ");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Escape) throw new AcceptanceFailure("CANCELLED");
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
        Console.WriteLine();
        Safety.Require(password.Length > 0, "PASSWORD_REQUIRED");
        var connections = Safety.Databases.Select(database => new MySqlConnectionStringBuilder
        {
            Server = Safety.Host, Port = Safety.Port, Database = database, UserID = Safety.Account,
            Password = password.ToString(), Pooling = false, ConnectionTimeout = 5, DefaultCommandTimeout = 30,
            DateTimeKind = MySqlDateTimeKind.Utc, PersistSecurityInfo = false,
            AllowUserVariables = false, AllowLoadLocalInfile = false, AllowPublicKeyRetrieval = false
        }.ConnectionString).ToArray();
        password.Clear();
        Safety.Scenario = "PREFLIGHT"; Safety.Step = "OPEN_FIRST";
        await using var first = await VerifiedSession.OpenAsync(connections[0], Safety.EmptyDatabase, expectedServerUuid);
        Safety.Step = "ACQUIRE_MUTEX";
        await Safety.WithLeaseAsync(first.AcquireAsync, first.ReleaseAsync, async () =>
        {
            Safety.Step = "OPEN_SECOND";
            await using var second = await VerifiedSession.OpenAsync(connections[1], Safety.UpgradeDatabase, expectedServerUuid, first.OwnerId);
            Safety.Step = "BOTH_EMPTY";
            await PreflightAsync(first);
            await PreflightAsync(second);
            await RunScenarioAsync(first, upgrade: false);
            await RunScenarioAsync(second, upgrade: true);
        });
        Console.WriteLine("PASS MYSQL ACCEPTANCE: retained synthetic schemas; no cleanup or database drop.");
    }

    private static ApplicationDbContext Context(VerifiedSession session) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseMySql(session.Connection, new MySqlServerVersion(new Version(8, 0, 46)), mysql => mysql.CommandTimeout(30))
        .AddInterceptors(new CommandGuard(session), new TransactionGuard(session), new RejectReopen()).Options);

    internal static async Task<List<string[]>> RawQueryAsync(MySqlConnection connection, string sql,
        DbTransaction? transaction = null, params (string, object)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection, (MySqlTransaction?)transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var rows = new List<string[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!;
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<List<string[]>> QueryAsync(VerifiedSession session, string sql, params (string, object)[] parameters)
    {
        await session.GuardAsync();
        return await RawQueryAsync(session.Connection, sql, null, parameters);
    }

    private static async Task PreflightAsync(VerifiedSession session)
    {
        var objects = await QueryAsync(session,
            "SELECT (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()) + " +
            "(SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE()) + " +
            "(SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE()) + " +
            "(SELECT COUNT(*) FROM information_schema.EVENTS WHERE EVENT_SCHEMA=DATABASE())");
        Safety.RequireEmpty(long.Parse(objects.Single()[0], CultureInfo.InvariantCulture));
    }

    private static async Task<string[]> ReadAppliedAsync(VerifiedSession session)
    {
        var exists = await QueryAsync(session, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='__EFMigrationsHistory'");
        if (exists.Single()[0] == "0") return [];
        return (await QueryAsync(session, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId")).Select(r => r[0]).ToArray();
    }

    private static async Task ApplyAsync(VerifiedSession session, int expected)
    {
        await Safety.ApplyForwardAsync(expected, () => ReadAppliedAsync(session), async index =>
        {
            var sql = Safety.ForwardSql(index);
            await session.GuardAsync();
            await using var command = new MySqlCommand(sql, session.Connection) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        });
    }

    private static async Task RunScenarioAsync(VerifiedSession connection, bool upgrade)
    {
        var scenario = upgrade ? "UPGRADE_FOUR_TO_FIVE" : "EMPTY_ZERO_TO_FIVE";
        Safety.Scenario = scenario; Safety.Step = "EMPTY_RECHECK";
        await PreflightAsync(connection);
        var checks = 0;
        void Check(bool result, string code) { Safety.Require(result, code); checks++; Console.WriteLine($"PASS {scenario} {code}"); }
        await using var db = Context(connection);
        Check(db.Database.GetMigrations().SequenceEqual(Safety.Migrations), "COMPILED_MIGRATION_CHAIN");
        var initialCount = upgrade ? 4 : 5;
        for (var i = 0; i < initialCount; i++)
        {
            Safety.Step = $"MIGRATION_{i + 1}";
            await ApplyAsync(connection, i);
            Check((await ReadAppliedAsync(connection)).SequenceEqual(Safety.Migrations.Take(i + 1)), $"MIGRATION_{i + 1}_ORDER");
        }
        Safety.Step = "SYNTHETIC_FIXTURE";
        var fixture = await SeedSyntheticAsync(db);
        var oldTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Where(t => t != "case_status_histories").Order(StringComparer.Ordinal).ToArray();
        var dataBefore = await DataFingerprintAsync(connection, oldTables);
        var schemaBefore = await SchemaFingerprintAsync(connection, oldTables);
        if (upgrade) { Safety.Step = "MIGRATION_5"; await ApplyAsync(connection, 4); }
        Safety.Step = "UPGRADE_METADATA";
        Check(await DataFingerprintAsync(connection, oldTables) == dataBefore, "EXISTING_15_TABLE_ROWS_UNCHANGED");
        Check(await SchemaFingerprintAsync(connection, oldTables) == schemaBefore, "EXISTING_15_TABLE_SCHEMA_UNCHANGED");
        Check(await db.CaseStatusHistories.CountAsync() == 0, "NO_HISTORY_BACKFILL");
        await VerifyMetadataAsync(connection);
        Check(true, "HISTORY_COLUMNS_PK_INDEXES_RESTRICT_FKS_AND_ENGINES");
        Check((await ReadAppliedAsync(connection)).SequenceEqual(Safety.Migrations), "FIVE_APPLIED_NONE_PENDING");
        var allTables = oldTables.Append("case_status_histories").ToArray();
        var schemaAfter = await SchemaFingerprintAsync(connection, allTables);
        var allData = await DataFingerprintAsync(connection, allTables);
        Safety.Step = "REPEAT_FORWARD_NOOP";
        await ApplyAsync(connection, 5);
        await ApplyAsync(connection, 5);
        Check(await SchemaFingerprintAsync(connection, allTables) == schemaAfter &&
            await DataFingerprintAsync(connection, allTables) == allData &&
            (await ReadAppliedAsync(connection)).SequenceEqual(Safety.Migrations), "REPEATED_MIGRATION_IS_NOOP");
        db.ChangeTracker.Clear();
        Safety.Step = "FK_REJECTION";
        await ExpectMissingForeignKeyAsync(connection, Guid.NewGuid(), fixture.UserId, "fk_case_status_histories_case");
        Check(true, "MISSING_CASE_REJECTED_1452");
        await ExpectMissingForeignKeyAsync(connection, fixture.CaseId, Guid.NewGuid(), "fk_case_status_histories_changed_by_user");
        Check(true, "MISSING_USER_REJECTED_1452");
        Check(await db.CaseStatusHistories.CountAsync() == 0, "FK_FAILURES_LEAVE_NO_HISTORY");
        Safety.Step = "TRANSACTIONS";
        await ExerciseTransactionsAsync(connection, fixture.CaseId, fixture.UserId);
        Check(true, "REAL_EF_STORE_COMMIT_DISPOSE_ROLLBACK_AND_FK_FAILURE_ROLLBACK");
        await using (var nullable = Context(connection))
        {
            await using var tx = await new EfCaseStatusStore(nullable).OpenAsync(fixture.CaseId, default)
                ?? throw new AcceptanceFailure("CASE_MISSING");
            tx.Case.CaseStatus = "APPOINTED";
            var entry = History(fixture.CaseId, null);
            entry.OldStatus = "TO_CONTACT"; entry.NewStatus = "APPOINTED";
            tx.AddHistory(entry);
            await tx.CommitAsync(default);
        }
        Check(await db.CaseStatusHistories.CountAsync(h => h.ChangedByUserId == null && h.Reason == null) == 1, "NULL_SYSTEM_ACTOR_AND_REASON_ACCEPTED");
        Console.WriteLine($"PASS {scenario}: {checks} checks; synthetic data only. No production API/auth acceptance claimed.");
    }

    private static CaseStatusHistory History(Guid caseId, Guid? userId) => new()
    {
        HistoryId = Guid.NewGuid(), CaseId = caseId, OldStatus = "NEW", NewStatus = "TO_CONTACT",
        ChangedAtUtc = SyntheticTime, ChangedByUserId = userId,
        TriggerSource = userId.HasValue ? "MANUAL" : "SYSTEM_WORKFLOW", Reason = null
    };

    private static async Task ExpectMissingForeignKeyAsync(VerifiedSession connection, Guid caseId, Guid userId, string constraint)
    {
        await using var db = Context(connection);
        db.CaseStatusHistories.Add(History(caseId, userId));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is MySqlException mysql && mysql.Number == 1452 &&
            mysql.Message.Contains(constraint, StringComparison.Ordinal)) { return; }
        throw new AcceptanceFailure("EXPECTED_MYSQL_FK_REJECTION");
    }

    private static async Task ExerciseTransactionsAsync(VerifiedSession connection, Guid caseId, Guid userId)
    {
        var rollbackId = Guid.NewGuid();
        // Persist both changes to the active transaction, then exercise the real Store's disposal rollback.
        await using (var db = Context(connection))
        {
            await using var tx = await new EfCaseStatusStore(db).OpenAsync(caseId, default)
                ?? throw new AcceptanceFailure("CASE_MISSING");
            tx.Case.CaseStatus = "TO_CONTACT";
            var entry = History(caseId, userId); entry.HistoryId = rollbackId; tx.AddHistory(entry);
            await db.SaveChangesAsync();
            Safety.Require(await db.CaseStatusHistories.AnyAsync(h => h.HistoryId == rollbackId), "STAGED_HISTORY_MISSING");
            // No Commit; disposal must roll back the database writes, not just tracked objects.
        }
        await using (var verify = Context(connection))
            Safety.Require(await verify.Cases.Where(c => c.CaseId == caseId).Select(c => c.CaseStatus).SingleAsync() == "NEW" &&
                !await verify.CaseStatusHistories.AnyAsync(h => h.HistoryId == rollbackId), "DISPOSAL_ROLLBACK_FAILED");

        await using (var db = Context(connection))
        {
            await using var tx = await new EfCaseStatusStore(db).OpenAsync(caseId, default)
                ?? throw new AcceptanceFailure("CASE_MISSING");
            tx.Case.CaseStatus = "TO_CONTACT";
            await db.SaveChangesAsync(); // Force the Case update before the failing History insert.
            tx.AddHistory(History(caseId, Guid.NewGuid()));
            var rejected = false;
            try { await tx.CommitAsync(default); }
            catch (DbUpdateException e) when (e.InnerException is MySqlException { Number: 1452 }) { rejected = true; }
            Safety.Require(rejected, "FAULT_INJECTION_NOT_REJECTED");
        }
        await using (var verify = Context(connection))
            Safety.Require(await verify.Cases.Where(c => c.CaseId == caseId).Select(c => c.CaseStatus).SingleAsync() == "NEW" &&
                await verify.CaseStatusHistories.CountAsync() == 0, "FK_FAILURE_PARTIAL_COMMIT");

        Guid committedId;
        await using (var db = Context(connection))
        {
            await using var tx = await new EfCaseStatusStore(db).OpenAsync(caseId, default)
                ?? throw new AcceptanceFailure("CASE_MISSING");
            tx.Case.CaseStatus = "TO_CONTACT";
            var entry = History(caseId, userId); committedId = entry.HistoryId; tx.AddHistory(entry);
            await tx.CommitAsync(default);
        }
        await using (var verify = Context(connection))
            Safety.Require(await verify.Cases.Where(c => c.CaseId == caseId).Select(c => c.CaseStatus).SingleAsync() == "TO_CONTACT" &&
                await verify.CaseStatusHistories.AnyAsync(h => h.HistoryId == committedId && h.CaseId == caseId && h.ChangedByUserId == userId), "ATOMIC_COMMIT_FAILED");
    }

    private static async Task<(Guid CaseId, Guid UserId)> SeedSyntheticAsync(ApplicationDbContext db)
    {
        // Explicitly synthetic persistence fixture; no clinical questionnaire, OTP, grant code or live credential.
        var caseId = Guid.NewGuid(); var userId = Guid.NewGuid(); var org = Guid.NewGuid();
        var questionnaire = Guid.NewGuid(); var version = Guid.NewGuid(); var task = Guid.NewGuid(); var grant = Guid.NewGuid();
        var role = await db.Roles.Where(r => r.Name == "ADMIN").Select(r => r.Id).SingleAsync();
        var permission = Guid.NewGuid();
        db.Organizations.Add(new Organization { Id = org, Code = "SYNTHETIC_HISTORY_ORG", Name = "SYNTHETIC TEST ONLY", CreatedAtUtc = SyntheticTime, UpdatedAtUtc = SyntheticTime });
        db.Users.Add(new User { Id = userId, OrganizationId = org, RoleId = role, Username = "synthetic_history_user", PasswordHash = "NOT_A_LOGIN_HASH_SYNTHETIC", Status = "INACTIVE", CreatedAtUtc = SyntheticTime, UpdatedAtUtc = SyntheticTime });
        db.Permissions.Add(new Permission { Id = permission, Name = "synthetic.history.test" });
        db.RolePermissions.Add(new RolePermission { RoleId = role, PermissionId = permission });
        db.Cases.Add(new CaseRecord { CaseId = caseId, CaseCode = "SYNTHETIC-HISTORY-ONLY", ChildName = "SYNTHETIC TEST ONLY", BirthDate = new DateOnly(2020, 1, 1), Sex = "UNKNOWN", CaseStatus = "NEW", CreatedAtUtc = SyntheticTime, UpdatedAtUtc = SyntheticTime });
        db.Questionnaires.Add(new Questionnaire { QuestionnaireId = questionnaire, QuestionnaireCode = "SYNTH_HISTORY", Title = "SYNTHETIC NON-CLINICAL", CreatedAtUtc = SyntheticTime });
        db.QuestionnaireVersions.Add(new QuestionnaireVersion { QuestionnaireVersionId = version, QuestionnaireId = questionnaire, RespondentRole = "TEACHER", VersionNumber = "synthetic-1", VersionStatus = "PUBLISHED", DefinitionSnapshot = "{\"title\":\"SYNTHETIC\",\"questions\":[]}", CreatedAtUtc = SyntheticTime });
        db.QuestionnaireTasks.Add(new QuestionnaireTask { TaskId = task, CaseId = caseId, QuestionnaireId = questionnaire, QuestionnaireVersionId = version, RespondentRole = "TEACHER", AssignmentRound = 1, IsRequired = true, TaskStatus = "SUBMITTED", CreatedAtUtc = SyntheticTime, UpdatedAtUtc = SyntheticTime, SubmittedAtUtc = SyntheticTime });
        db.TeacherGrants.Add(new TeacherGrant { GrantId = grant, CaseId = caseId, RespondentRole = "TEACHER", CodeHash = SHA256.HashData("synthetic-no-code"u8), GrantStatus = "USED", IssuedByUserId = userId, IsDevelopment = true, CreatedAtUtc = SyntheticTime, UsedAtUtc = SyntheticTime });
        db.TeacherGrantTasks.Add(new TeacherGrantTask { GrantId = grant, TaskId = task, CaseId = caseId, RespondentRole = "TEACHER" });
        db.TeacherSessions.Add(new TeacherSession { SessionHash = SHA256.HashData("synthetic-no-session"u8), GrantId = grant, SessionStatus = "REVOKED", CreatedAtUtc = SyntheticTime, LastSeenAtUtc = SyntheticTime, SessionExpiresAtUtc = SyntheticTime, RevokedAtUtc = SyntheticTime });
        db.QuestionnaireDrafts.Add(new QuestionnaireDraft { TaskId = task, QuestionnaireVersionId = version, AnswersJson = "[]", Revision = 1, LastSavedByGrantId = grant, UpdatedAtUtc = SyntheticTime });
        db.QuestionnaireResponses.Add(new QuestionnaireResponse { ResponseId = Guid.NewGuid(), TaskId = task, QuestionnaireVersionId = version, RespondentName = "SYNTHETIC", FilledOn = new DateOnly(2026, 10, 19), AnswersJson = "[]", SubmittedByGrantId = grant, IdempotencyKey = Guid.NewGuid(), PayloadHash = SHA256.HashData("synthetic-answer"u8), SubmittedAtUtc = SyntheticTime });
        db.AuditLogs.Add(new AuditRecord { EventId = Guid.NewGuid(), OccurredAtUtc = SyntheticTime, ActorType = "SyntheticTest", ActorId = "synthetic", Action = "Synthetic.History", ResourceType = "Synthetic", ResourceId = "synthetic", Result = "Test", RequestCorrelationId = "synthetic-history" });
        await db.SaveChangesAsync();
        return (caseId, userId);
    }

    private static string Hash(IEnumerable<string> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows.Order(StringComparer.Ordinal)))));

    private static async Task<string> DataFingerprintAsync(VerifiedSession connection, string[] tables)
    {
        var fingerprints = new List<string>();
        foreach (var table in tables)
        {
            // Names come only from the compiled EF model; never from arguments/configuration.
            await connection.GuardAsync();
            await using var command = new MySqlCommand($"SELECT * FROM `{table}`", connection.Connection);
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount]; reader.GetValues(values);
                rows.Add(JsonSerializer.Serialize(values.Select(v => v is DBNull ? null : v).ToArray()));
            }
            fingerprints.Add(table + ":" + Hash(rows));
        }
        return Hash(fingerprints); // No fixture rows or hashes are logged.
    }

    private static async Task<string> SchemaFingerprintAsync(VerifiedSession connection, string[] tables)
    {
        var results = new List<string>();
        var queries = new[]
        {
            "SELECT TABLE_NAME,ENGINE,TABLE_COLLATION FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()",
            "SELECT TABLE_NAME,COLUMN_NAME,ORDINAL_POSITION,COLUMN_TYPE,IS_NULLABLE,COLUMN_DEFAULT,EXTRA,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE()",
            "SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,SEQ_IN_INDEX,COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE()",
            "SELECT TABLE_NAME,CONSTRAINT_NAME,COLUMN_NAME,ORDINAL_POSITION,REFERENCED_TABLE_NAME,REFERENCED_COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE()",
            "SELECT TABLE_NAME,CONSTRAINT_NAME,REFERENCED_TABLE_NAME,UPDATE_RULE,DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE()"
        };
        for (var i = 0; i < queries.Length; i++)
            results.AddRange((await QueryAsync(connection, queries[i])).Where(r => tables.Contains(r[0], StringComparer.Ordinal)).Select(r => i + ":" + JsonSerializer.Serialize(r)));
        return Hash(results);
    }

    private static async Task VerifyMetadataAsync(VerifiedSession connection)
    {
        var columns = await QueryAsync(connection, "SELECT COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='case_status_histories' ORDER BY ORDINAL_POSITION");
        var statuses = "enum('NEW','TO_CONTACT','APPOINTED','WAITING_FORM','FORM_COMPLETED','COMPLETED')";
        var expected = new[]
        {
            new[] { "history_id", "char(36)", "NO" }, new[] { "case_id", "char(36)", "NO" },
            new[] { "old_status", statuses, "NO" }, new[] { "new_status", statuses, "NO" },
            new[] { "changed_at_utc", "datetime(6)", "NO" }, new[] { "changed_by_user_id", "char(36)", "YES" },
            new[] { "trigger_source", "enum('MANUAL','QUESTIONNAIRE_COMPLETION','SYSTEM_WORKFLOW')", "NO" }, new[] { "reason", "text", "YES" }
        };
        Safety.Require(JsonSerializer.Serialize(columns) == JsonSerializer.Serialize(expected), "HISTORY_COLUMN_METADATA");
        var indexes = await QueryAsync(connection, "SELECT INDEX_NAME,NON_UNIQUE,GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ',') FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='case_status_histories' GROUP BY INDEX_NAME,NON_UNIQUE");
        var expectedIndexes = new[] { new[] { "PRIMARY", "0", "history_id" }, new[] { "ix_case_status_histories_case_time", "1", "case_id,changed_at_utc,history_id" }, new[] { "ix_case_status_histories_changed_by_user", "1", "changed_by_user_id" } };
        Safety.Require(Hash(indexes.Select(row => JsonSerializer.Serialize(row))) == Hash(expectedIndexes.Select(row => JsonSerializer.Serialize(row))), "HISTORY_INDEX_METADATA");
        var fks = await QueryAsync(connection, "SELECT k.CONSTRAINT_NAME,k.COLUMN_NAME,k.REFERENCED_TABLE_NAME,k.REFERENCED_COLUMN_NAME,r.DELETE_RULE FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME AND r.TABLE_NAME=k.TABLE_NAME WHERE k.TABLE_SCHEMA=DATABASE() AND k.TABLE_NAME='case_status_histories'");
        var expectedFks = new[] { new[] { "fk_case_status_histories_case", "case_id", "cases", "case_id", "RESTRICT" }, new[] { "fk_case_status_histories_changed_by_user", "changed_by_user_id", "users", "user_id", "RESTRICT" } };
        Safety.Require(Hash(fks.Select(row => JsonSerializer.Serialize(row))) == Hash(expectedFks.Select(row => JsonSerializer.Serialize(row))), "HISTORY_FK_METADATA");
        var tables = await QueryAsync(connection, "SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()");
        await using var db = Context(connection);
        var expectedTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Append("__EFMigrationsHistory").ToHashSet(StringComparer.OrdinalIgnoreCase);
        Safety.Require(tables.Count == expectedTables.Count && tables.All(t => expectedTables.Contains(t[0]) && t[1] == "InnoDB"), "TABLE_SET_OR_ENGINE");
    }
}
