using EarlyInterventionCare.Api.Models.Authentication;
using EarlyInterventionCare.Api.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EarlyInterventionCare.Api.Data;

/// <summary>Explicit, interactive local fixtures only; never called by normal API startup.</summary>
public static class DevelopmentUserCreator
{
    public const string OrganizationCode = "LOCAL_AUTH_ACCEPTANCE";
    public const string OrganizationName = "本機醫療登入驗收虛構院所";
    public const string Confirmation = "CREATE LOCAL AUTH FIXTURE";

    public sealed record AccountSpec(string Username, string RoleName, string Status);

    public static IReadOnlyList<AccountSpec> Accounts { get; } = Array.AsReadOnly(new[]
    {
        new AccountSpec("local_case_manager", "CASE_MANAGER", "ACTIVE"),
        new AccountSpec("local_medical_staff", "MEDICAL_STAFF", "ACTIVE"),
        new AccountSpec("local_inactive_staff", "MEDICAL_STAFF", "INACTIVE")
    });

    // Public pure helpers support offline checks; these are not API or DI services.
    public static bool IsAllowedTarget(string environmentName, string? aspNetCoreEnvironment,
        string? dotnetEnvironment, string? connectionString)
    {
        if (environmentName != "Development" || aspNetCoreEnvironment != "Development" ||
            (dotnetEnvironment is not null && dotnetEnvironment != "Development") ||
            string.IsNullOrWhiteSpace(connectionString))
            return false;
        try
        {
            var settings = new MySqlConnectionStringBuilder(connectionString);
            return settings.Server == "127.0.0.1" && settings.Port == 3306 &&
                settings.Database == "earlycare_dev" &&
                settings.ConnectionProtocol == MySqlConnectionProtocol.Sockets &&
                settings.ServerRedirectionMode == MySqlServerRedirectionMode.Disabled;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Reads exact input without echo, trimming, normalization or silent truncation.</summary>
    public static string ReadPassword(Func<ConsoleKeyInfo> readKey)
    {
        // A valid UTF-8 password of at most 72 bytes cannot require more than 72 UTF-16 chars.
        var buffer = new char[StaffPasswordPolicy.MaximumUtf8Bytes];
        var length = 0;
        try
        {
            while (true)
            {
                var key = readKey();
                if (key.Key == ConsoleKey.Escape)
                    throw new OperationCanceledException();
                if (key.Key == ConsoleKey.Enter)
                    return new string(buffer, 0, length);
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (length > 0)
                    {
                        var removed = buffer[--length];
                        buffer[length] = '\0';
                        if (char.IsLowSurrogate(removed) && length > 0 && char.IsHighSurrogate(buffer[length - 1]))
                            buffer[--length] = '\0';
                    }
                    continue;
                }
                if (key.KeyChar == '\0' || (char.IsControl(key.KeyChar) && key.KeyChar != '\t'))
                    continue;
                if (length == buffer.Length)
                    throw new ArgumentException("PASSWORD_TOO_LONG");
                buffer[length++] = key.KeyChar;
            }
        }
        finally { Array.Clear(buffer); }
    }

    public static string HashValidatedPassword(string password)
    {
        var result = new StaffPasswordPolicy().Validate(password);
        if (!result.IsValid)
            throw new ArgumentException(result.Code);
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        try
        {
            // Snapshot the effective configuration once; never reload the target after checking it.
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (!IsAllowedTarget(builder.Environment.EnvironmentName,
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), connectionString))
            {
                Console.WriteLine("LocalDatabaseScopeMismatch: explicit Development and TCP 127.0.0.1:3306/earlycare_dev without server redirection are required.");
                return 1;
            }
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                Console.WriteLine("InteractiveTerminalRequired: enter passwords directly in a local terminal; stdin/stdout redirection is not allowed.");
                return 1;
            }

            Console.WriteLine("Target: 127.0.0.1:3306/earlycare_dev. Create one fictional organization and these NEW users:");
            foreach (var account in Accounts)
                Console.WriteLine($"  {account.Username}: {account.RoleName}, {account.Status}");
            Console.WriteLine("Existing matching organization or username stops the entire operation. No existing data will be changed.");
            Console.Write($"Type {Confirmation} to permit this local database operation (anything else cancels): ");
            if (!string.Equals(Console.ReadLine(), Confirmation, StringComparison.Ordinal))
            {
                Console.WriteLine("Cancelled: no database connection or changes.");
                return 1;
            }

            var settings = new MySqlConnectionStringBuilder(connectionString!)
            {
                DateTimeKind = MySqlDateTimeKind.Utc, ConnectionTimeout = 5, Pooling = false
            };
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 0, 41)),
                    mysql => mysql.CommandTimeout(15))
                .EnableSensitiveDataLogging(false)
                .Options;
            await using var db = new ApplicationDbContext(options);

            // Fail before asking for passwords if the existing schema, roles or fixture names are unsuitable.
            var engines = await db.Database.SqlQueryRaw<string>(
                "SELECT ENGINE AS Value FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('organizations', 'users', 'roles')")
                .ToListAsync();
            if (engines.Count != 3 || engines.Any(engine => !string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("TransactionalSchemaRequired: organizations, users and roles must exist and use InnoDB.");
                return 1;
            }
            var usernames = Accounts.Select(account => account.Username).ToArray();
            if (await db.Organizations.AsNoTracking().AnyAsync(o => o.Code == OrganizationCode) ||
                await db.Users.AsNoTracking().AnyAsync(u => usernames.Contains(u.Username)))
            {
                Console.WriteLine("FixtureAlreadyExists: stopped without changing data; existing credentials and statuses are preserved.");
                return 1;
            }
            var roleNames = Accounts.Select(account => account.RoleName).Distinct().ToArray();
            var roles = await db.Roles.AsNoTracking().Where(r => roleNames.Contains(r.Name))
                .Select(r => new { r.Id, r.Name }).ToListAsync();
            if (roles.Count != roleNames.Length || roleNames.Any(name =>
                    roles.Count(role => role.Name == name && role.Id != Guid.Empty) != 1))
            {
                Console.WriteLine("RoleSelectionFailed: exactly one existing nonempty Guid per required role is necessary; no roles are created.");
                return 1;
            }

            var organizationId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var newUsers = new List<User>();
            foreach (var account in Accounts)
            {
                string? password = null;
                string? confirmation = null;
                try
                {
                    Console.Write($"New password for {account.Username} (hidden; Esc cancels): ");
                    password = ReadPassword(() => Console.ReadKey(intercept: true));
                    Console.WriteLine();
                    var validation = new StaffPasswordPolicy().Validate(password);
                    if (!validation.IsValid)
                    {
                        Console.WriteLine($"{validation.Code}: {validation.Message} No data was written.");
                        return 1;
                    }
                    Console.Write("Confirm password (hidden): ");
                    confirmation = ReadPassword(() => Console.ReadKey(intercept: true));
                    Console.WriteLine();
                    if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                    {
                        Console.WriteLine("PasswordMismatch: stopped without changing data.");
                        return 1;
                    }
                    newUsers.Add(new User
                    {
                        Id = Guid.NewGuid(), Username = account.Username,
                        PasswordHash = HashValidatedPassword(password),
                        OrganizationId = organizationId,
                        RoleId = roles.Single(role => role.Name == account.RoleName).Id,
                        Status = account.Status, CreatedAtUtc = now, UpdatedAtUtc = now
                    });
                }
                finally
                {
                    // Managed strings cannot be reliably zeroed; retain no password fields or files.
                    password = null;
                    confirmation = null;
                }
            }

            // One explicit transaction for all four new rows; unique indexes also guard concurrent runs.
            // No migrations, seed, role attachment, updates or deletes are performed.
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Organizations.Add(new Organization
            {
                Id = organizationId, Code = OrganizationCode, Name = OrganizationName,
                Status = "ACTIVE", CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.Users.AddRange(newUsers);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine("Created one fictional local organization and three users. Keep the passwords privately; they cannot be displayed by this tool.");
            foreach (var account in Accounts)
                Console.WriteLine($"  {account.Username}: {account.RoleName}, {account.Status}");
            return 0;
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: 1062 })
        {
            Console.WriteLine("FixtureAlreadyExists: concurrent duplicate rejected; this transaction was not committed. No existing data was overwritten.");
            return 1;
        }
        catch (Exception exception)
        {
            var error = exception is DbUpdateException { InnerException: MySqlException inner } ? inner : exception;
            var message = error switch
            {
                MySqlException { Number: 1045 } => "AuthenticationFailed: check local database credentials and account host permissions.",
                MySqlException { Number: 1044 or 1142 } => "AccessDenied: check local database SELECT and INSERT permissions.",
                MySqlException { Number: 1049 } => "DatabaseNotFound: the permitted local database does not exist.",
                MySqlException { Number: 1146 or 1054 } => "SchemaMismatch: check existing tables and mapped columns.",
                MySqlException { Number: 1452 } => "ForeignKeyConflict: a required reference may no longer exist.",
                MySqlException => "MySqlError: check local connection, permissions and schema.",
                OperationCanceledException => "Cancelled: no success is assumed; verify local fixture existence before retrying.",
                TimeoutException => "Timeout: verify local fixture existence before retrying; commit outcome may be uncertain.",
                ArgumentException or FormatException => "ConfigurationOrPasswordInvalid: check local target and password policy; no success is assumed.",
                _ => "CreationFailed: verify local fixture existence before retrying; no success is assumed."
            };
            // Never print exception messages, password values, hashes, or connection strings.
            Console.WriteLine(message);
            return 1;
        }
    }
}
