using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EarlyInterventionCare.Api.Data;

/// <summary>Offline model scaffolding only; runtime configuration remains in Program.cs.</summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Never load secrets or runtime connection settings for model scaffolding.
        const string connection = "Server=localhost;Database=earlycare_dev;User=offline;DateTimeKind=Utc;";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 46)))
            .AddInterceptors(new OfflineConnectionInterceptor())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class OfflineConnectionInterceptor : DbConnectionInterceptor
    {
        private static InvalidOperationException OfflineOnly() => new(
            "Design-time database connections are disabled. Use migrations list --no-connect. Apply database changes through a separately reviewed workflow.");

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => throw OfflineOnly();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
            => throw OfflineOnly();
    }
}
