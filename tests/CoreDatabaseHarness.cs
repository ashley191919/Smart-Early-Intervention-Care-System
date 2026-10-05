using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.Development;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

var expectedTables = new HashSet<string>(StringComparer.Ordinal)
{
    "cases", "questionnaires", "questionnaire_versions", "case_questionnaires", "teacher_grants",
    "teacher_grant_tasks", "teacher_sessions", "questionnaire_drafts", "questionnaire_responses", "audit_logs"
};
try
{
    if (args.Contains("--configure"))
    {
        DevelopmentConnectionSetup.Run();
        if (!args.Any(a => a is "--database" or "--apply" or "--seed")) return 0;
    }
    var apply = args.Contains("--apply");
    var seed = args.Contains("--seed");
    var database = apply || seed || args.Contains("--database");
    await using var context = new ApplicationDbContextFactory().CreateDbContext(database ? [] : ["--offline"]);
    var tables = context.Model.GetEntityTypes().Select(e => e.GetTableName()!).ToHashSet(StringComparer.Ordinal);
    if (!tables.SetEquals(expectedTables)) throw new InvalidOperationException("Expected nine core tables plus audit_logs.");
    var task = context.Model.FindEntityType(typeof(QuestionnaireTask))!;
    if (!task.GetForeignKeys().Any(f => f.Properties.Select(p => p.Name).SequenceEqual(new[]
        { "QuestionnaireVersionId", "QuestionnaireId", "RespondentRole" })))
        throw new InvalidOperationException("Task must enforce questionnaire version and role together.");
    var scope = context.Model.FindEntityType(typeof(TeacherGrantTask))!;
    if (scope.GetForeignKeys().Count() != 2 || scope.GetForeignKeys().Any(f => f.Properties.Count != 3))
        throw new InvalidOperationException("Grant scope must enforce matching case and role.");
    var draft = context.Model.FindEntityType(typeof(QuestionnaireDraft))!;
    if (!draft.FindProperty("Revision")!.IsConcurrencyToken)
        throw new InvalidOperationException("Draft revision must detect concurrent updates.");
    var response = context.Model.FindEntityType(typeof(QuestionnaireResponse))!;
    if (!response.GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "TaskId"))
        throw new InvalidOperationException("Each task must have at most one response.");
    if (context.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).Any(f => f.DeleteBehavior != DeleteBehavior.Restrict))
        throw new InvalidOperationException("Core records must not cascade-delete evidence.");
    if (context.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
        .Any(p => p.ClrType == typeof(bool) && p.GetColumnType() != "tinyint(1)"))
        throw new InvalidOperationException("MySQL boolean columns must use valid tinyint(1) mapping.");
    var audit = context.Model.FindEntityType(typeof(AuditRecord))!;
    if (audit.GetForeignKeys().Any() || !audit.GetIndexes().Any(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ResourceType", "ResourceId", "OccurredAtUtc" })))
        throw new InvalidOperationException("Audit evidence must be independent and indexed by resource/time.");
    Console.WriteLine("PASS: nine core tables plus audit_logs, role/version scope, draft concurrency, unique response and restricted deletes.");
    if (!database) return 0;

    await context.Database.OpenConnectionAsync();
    await using var command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()";
    // Windows MySQL commonly returns lowercase names, including __efmigrationshistory.
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) existing.Add(reader.GetString(0));
    if (apply)
    {
        var allowed = expectedTables.Append("__EFMigrationsHistory").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (existing.Except(allowed, StringComparer.OrdinalIgnoreCase).Any() || (existing.Count > 0 && !existing.Contains("__EFMigrationsHistory")))
            throw new InvalidOperationException("Database is not empty or migration-managed; no changes applied.");
        var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        if (appliedBefore.Length == 0 && existing.Any(expectedTables.Contains))
        {
            await InitialMigrationResume.RunAsync(context, existing);
            await context.Database.MigrateAsync();
        }
        else
            await context.Database.MigrateAsync();
        Console.WriteLine("PASS: migration applied to local earlycare_dev.");
    }
    var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
    Console.WriteLine($"PASS: connection; applied migrations={applied.Length}, pending migrations={pending.Length}.");
    if (seed)
    {
        if (applied.Length == 0 || pending.Length != 0)
            throw new InvalidOperationException("Apply the migration before seeding.");
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Development", "SnapQuestionnaire.json");
        await CoreDevelopmentSeed.EnsureAsync(context, fixturePath);
        await CoreDevelopmentSeed.EnsureAsync(context, fixturePath);
        await using var reloaded = new ApplicationDbContextFactory().CreateDbContext([]);
        if (await reloaded.Cases.CountAsync(e => e.CaseId == CoreDevelopmentSeed.CaseId) != 1 ||
            await reloaded.QuestionnaireTasks.CountAsync(e => e.TaskId == CoreDevelopmentSeed.TaskId) != 1)
            throw new InvalidOperationException("Seed persistence/idempotency verification failed.");
        Console.WriteLine("PASS: development seed repeated without duplicates; read back through a new context.");
    }
    return 0;
}
catch (MySqlException exception)
{
    // Do not print exception messages, connection strings, usernames or passwords.
    Console.Error.WriteLine($"Database check failed: MySQL error number {exception.Number}.");
    return 1;
}
catch (Exception exception)
{
    if (exception is InitialMigrationResumeException) Console.Error.WriteLine(exception.Message);
    if (!args.Contains("--database") && !args.Contains("--apply") && !args.Contains("--seed") && !args.Contains("--configure"))
        Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine($"Check failed: {exception.GetType().Name}. Check local configuration/model; no secret values printed.");
    if (exception.Data["Code"] is string code && code is "LOCAL_CONNECTION_MISSING" or "LOCAL_DATABASE_SCOPE_MISMATCH")
        Console.Error.WriteLine(code);
    return 1;
}
