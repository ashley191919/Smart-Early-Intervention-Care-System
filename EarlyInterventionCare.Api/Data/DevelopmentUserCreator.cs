using System.Text;
using EarlyInterventionCare.Api.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EarlyInterventionCare.Api.Data;

public static class DevelopmentUserCreator
{
    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Development", StringComparison.Ordinal) || !builder.Environment.IsDevelopment())
        {
            Console.WriteLine("Development user creation requires explicit Development environment.");
            return 1;
        }

        builder.Logging.ClearProviders();

        try
        {
            // Read account settings exclusively from this assembly's User Secrets.
            var secrets = new ConfigurationBuilder()
                .AddUserSecrets(typeof(DevelopmentUserCreator).Assembly, optional: true)
                .Build();
            using var secretsLifetime = secrets as IDisposable;
            var username = secrets["DevelopmentUser:Username"];
            var password = secrets["DevelopmentUser:Password"];
            var roleName = secrets["DevelopmentUser:RoleName"];
            var organizationSetting = secrets["DevelopmentUser:OrganizationId"];

            if (string.IsNullOrWhiteSpace(username) || username.Length > 100 ||
                string.IsNullOrWhiteSpace(password) || Encoding.UTF8.GetByteCount(password) > 72 ||
                string.IsNullOrWhiteSpace(roleName) ||
                !Guid.TryParse(organizationSetting, out var organizationId) || organizationId == Guid.Empty)
            {
                Console.WriteLine("ConfigurationInvalid: account secrets and a valid DevelopmentUser:OrganizationId are required.");
                return 1;
            }

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("ConfigurationMissing: DefaultConnection is absent or empty.");
                return 1;
            }

            var settings = new MySqlConnectionStringBuilder(connectionString) { DateTimeKind = MySqlDateTimeKind.Utc };
            if (settings.Server is not ("localhost" or "127.0.0.1" or "::1") || settings.Database != "earlycare_dev")
            {
                Console.WriteLine("LocalDatabaseScopeMismatch: this tool requires localhost / earlycare_dev.");
                return 1;
            }
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 0, 46)),
                    mysql => mysql.CommandTimeout(15))
                .Options;
            await using var db = new ApplicationDbContext(options);

            if (await db.Users.AsNoTracking().AnyAsync(u => u.Username == username))
            {
                Console.WriteLine("UserAlreadyExists: stopped without changing data.");
                return 1;
            }

            var roleIds = await db.Roles.AsNoTracking()
                .Where(r => r.Name == roleName).Select(r => r.Id).Take(2).ToListAsync();
            if (roleIds.Count != 1)
            {
                Console.WriteLine("RoleSelectionFailed: the existing role must have exactly one match.");
                return 1;
            }

            if (!await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == organizationId))
            {
                Console.WriteLine("OrganizationNotFound: select an existing organization; no data was changed.");
                return 1;
            }

            var now = DateTime.UtcNow;
            // Only this new user is tracked; existing roles and permissions are never attached.
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
                RoleId = roleIds[0],
                OrganizationId = organizationId,
                Status = "ACTIVE",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            // UTC timestamps are supplied by the application, matching the core model.
            await db.SaveChangesAsync();
            Console.WriteLine("Development user created successfully.");
            return 0;
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: 1062 })
        {
            Console.WriteLine("UserAlreadyExists: a concurrent insert was rejected; no account was overwritten.");
            return 1;
        }
        catch (Exception exception)
        {
            var error = exception is DbUpdateException { InnerException: MySqlException inner } ? inner : exception;
            var message = error switch
            {
                MySqlException { Number: 1045 } => "AuthenticationFailed: check database credentials and account host permissions.",
                MySqlException { Number: 1044 or 1142 } => "AccessDenied: check database SELECT and INSERT permissions.",
                MySqlException { Number: 1049 } => "DatabaseNotFound: check the configured database.",
                MySqlException { Number: 1146 or 1054 } => "SchemaMismatch: check existing tables and mapped columns.",
                MySqlException { Number: 1452 } => "ForeignKeyConflict: the selected role may no longer exist.",
                MySqlException { Number: 0 or 1042 or 2002 or 2003 or 2005 } => "ConnectionFailed: check server, network access, port, and TLS settings.",
                MySqlException => "MySqlError: check connection, permissions, and existing schema.",
                TimeoutException or OperationCanceledException => "Timeout: the database operation may have timed out; verify account existence before retrying.",
                ArgumentException or FormatException => "ConfigurationInvalid: check local configuration format.",
                _ => "CreationFailed: check local configuration and database availability."
            };
            // Never print exception messages, account values, hashes, or connection strings.
            Console.WriteLine(message);
            return 1;
        }
    }
}
