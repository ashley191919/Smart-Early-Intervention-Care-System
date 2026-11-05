using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

// Isolated assembly: exact pinned 11 sources + blob-identical linked production sources.
// No API ProjectReference/EntryPoint/configuration/connection string/scaffolder.
public static class UnionCandidateProbe
{
    public static (IReadOnlyList<MigrationOperation> History, IReadOnlyList<MigrationOperation> Stale) Read()
    {
        var events = new List<string>();
        using var connection = new OfflineConnection(events) { FailBegin = false, FailCommit = false };
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 46))).Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var ids = assembly.Migrations.Keys.Order(StringComparer.Ordinal).ToArray();
        if (ids.Length != 5 || ids[^1] != "20261019131342_AddCaseStatusHistories")
            throw new InvalidOperationException("Unexpected historical baseline");
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var target = initializer.Initialize(assembly.CreateMigration(assembly.Migrations[ids[^1]], db.Database.ProviderName!).TargetModel, designTime: true);
        var stale = initializer.Initialize(assembly.ModelSnapshot!.Model, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        var differ = db.GetService<IMigrationsModelDiffer>();
        var historyDiff = differ.GetDifferences(target.GetRelationalModel(), current.GetRelationalModel());
        var staleDiff = differ.GetDifferences(stale.GetRelationalModel(), current.GetRelationalModel());
        if (events.Count != 0 || connection.State != System.Data.ConnectionState.Closed || db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Offline boundary violated");
        return (historyDiff, staleDiff);
    }
}
