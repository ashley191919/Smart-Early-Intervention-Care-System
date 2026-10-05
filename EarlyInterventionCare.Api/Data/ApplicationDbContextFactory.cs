using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MySqlConnector;

namespace EarlyInterventionCare.Api.Data;

/// <summary>Scaffolding uses an explicit server version and does not contact a database.</summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var offline = args.Contains("--offline", StringComparer.Ordinal);
        var connection = offline
            ? "Server=localhost;Database=earlycare_dev;User=offline;Password=offline;"
            : new ConfigurationBuilder()
                .AddUserSecrets<ApplicationDbContextFactory>(optional: true)
                .AddEnvironmentVariables()
                .Build().GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
        {
            var exception = new InvalidOperationException("DefaultConnection is missing. Configure local user secrets; do not send passwords in chat.");
            exception.Data["Code"] = "LOCAL_CONNECTION_MISSING";
            throw exception;
        }

        // This initial factory is intentionally limited to the agreed local development database.
        var settings = new MySqlConnectionStringBuilder(connection);
        if (settings.Server is not ("localhost" or "127.0.0.1" or "::1") || settings.Database != "earlycare_dev")
        {
            var exception = new InvalidOperationException("This migration workflow requires localhost / earlycare_dev.");
            exception.Data["Code"] = "LOCAL_DATABASE_SCOPE_MISMATCH";
            throw exception;
        }
        settings.DateTimeKind = MySqlDateTimeKind.Utc;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 0, 46)))
            .Options;
        return new ApplicationDbContext(options);
    }
}
