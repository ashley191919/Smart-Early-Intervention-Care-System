using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Earlycare.CloudMigration;

public class DeploymentDependencies
{
    public virtual string? EnvironmentValue(string name) => Environment.GetEnvironmentVariable(name);
    public virtual ApplicationDbContext OfflineContext() => new ApplicationDbContextFactory().CreateDbContext([]);
    public virtual ApplicationDbContext DatabaseContext(string password)
    {
        var settings = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1", Port = 13306, Database = "earlycare_dev", UserID = "earlycare_migration",
            Password = password, DateTimeKind = MySqlDateTimeKind.Utc, Pooling = false,
            ConnectionTimeout = 10, AllowUserVariables = false
        };
        return new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 4, 11))).Options);
    }
    public virtual Task OpenConnection(ApplicationDbContext db) => db.Database.OpenConnectionAsync();
    public virtual int? MySqlErrorNumber(Exception exception) => exception is MySqlException mysql ? mysql.Number : null;
    public virtual async Task<List<string[]>> Read(ApplicationDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql; cmd.CommandTimeout = 30;
        var rows = new List<string[]>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!).ToArray());
        return rows;
    }
    public virtual string PasswordInput()
    {
        if (Console.IsInputRedirected) throw new DeploymentException("INVALID_ARGUMENT");
        Console.Write("Migration password (hidden): ");
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
            else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        Console.WriteLine(); var value = buffer.ToString(); buffer.Clear();
        if (value.Length == 0) throw new DeploymentException("INVALID_ARGUMENT");
        return value;
    }
    public virtual void WriteScript(string path, string sql) => SafeScript.Write(path, sql);
}

public sealed class DeploymentException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public static class SafeScript
{
    public static void Write(string path, string sql, Action<Stream, string>? write = null)
    {
        string? full = null; bool owned = false;
        try
        {
            full = Path.GetFullPath(path);
            using (var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                if (write is not null) write(file, sql);
                else { using var writer = new StreamWriter(file, new System.Text.UTF8Encoding(false), leaveOpen: true); writer.Write(sql); writer.Flush(); }
                file.Flush(true);
            }
        }
        catch
        {
            if (owned && full is not null)
            {
                try { File.Delete(full); }
                catch { throw new DeploymentException("SQL_SCRIPT_CLEANUP_FAILED"); }
            }
            throw new DeploymentException("SQL_SCRIPT_WRITE_FAILED");
        }
    }
}
