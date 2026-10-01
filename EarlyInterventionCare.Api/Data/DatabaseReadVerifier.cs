using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EarlyInterventionCare.Api.Data;

public static class DatabaseReadVerifier
{
    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Read verification requires Development environment.");
            return 1;
        }

        // Never attach logging providers or enable sensitive data logging in this mode.
        builder.Logging.ClearProviders();
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ReportUnavailable("ConfigurationMissing", "DefaultConnection is absent or empty.");
            return 1;
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                    mysql => mysql.CommandTimeout(15))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;
            await using var db = new ApplicationDbContext(options);

            // Materialize all mapped columns, but never print any entity values.
            var roles = await VerifyTableAsync("roles", () => db.Roles.AsNoTracking().Take(1).ToListAsync());
            var permissions = await VerifyTableAsync("permissions", () => db.Permissions.AsNoTracking().Take(1).ToListAsync());
            var users = await VerifyTableAsync("users", () => db.Users.AsNoTracking().Take(1).ToListAsync());
            return roles && permissions && users ? 0 : 1;
        }
        catch (Exception exception)
        {
            var (type, cause) = Classify(exception);
            ReportUnavailable(type, cause);
            return 1;
        }
    }

    private static async Task<bool> VerifyTableAsync<T>(string table, Func<Task<List<T>>> query)
    {
        try
        {
            var rows = await query();
            Console.WriteLine($"{table}: query succeeded; has data: {(rows.Count > 0 ? "yes" : "no")}");
            return true;
        }
        catch (Exception exception)
        {
            var (type, cause) = Classify(exception);
            Console.WriteLine($"{table}: query failed; has data: unknown; error: {type}; possible cause: {cause}");
            return false;
        }
    }

    private static void ReportUnavailable(string type, string cause)
    {
        foreach (var table in new[] { "roles", "permissions", "users" })
            Console.WriteLine($"{table}: query not completed; has data: unknown; error: {type}; possible cause: {cause}");
    }

    private static (string Type, string Cause) Classify(Exception exception) => exception switch
    {
        MySqlException { Number: 1045 } => ("AuthenticationFailed", "Credentials or account host permissions may be incorrect."),
        MySqlException { Number: 1044 or 1142 } => ("AccessDenied", "The account may lack database or SELECT permissions."),
        MySqlException { Number: 1049 } => ("DatabaseNotFound", "The configured database may not exist."),
        MySqlException { Number: 1146 } => ("TableNotFound", "The selected database or mapped table name may be incorrect."),
        MySqlException { Number: 1054 } => ("ColumnMappingMismatch", "Mapped columns may differ from the actual schema."),
        MySqlException { Number: 0 or 1042 or 2002 or 2003 or 2005 } => ("ConnectionFailed", "Check server availability, host, port, network access, and TLS settings."),
        MySqlException => ("MySqlError", "Connection, permissions, schema, or query execution may need checking."),
        TimeoutException or OperationCanceledException => ("Timeout", "The connection or query may have timed out."),
        ArgumentException => ("ConfigurationInvalid", "Connection settings may have an invalid format or unsupported option."),
        InvalidCastException or FormatException => ("DataMappingMismatch", "Database values may not match the mapped CLR types."),
        InvalidOperationException => ("ModelOrQueryError", "Check model configuration and mapped nullable columns."),
        _ => ("VerificationFailed", "Check configuration, provider, and database availability locally.")
    };
}
