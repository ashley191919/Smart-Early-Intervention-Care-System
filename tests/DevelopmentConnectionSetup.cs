using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EarlyInterventionCare.Api.Data;
using Microsoft.Extensions.Configuration.UserSecrets;
using MySqlConnector;

internal static class DevelopmentConnectionSetup
{
    public static int Run()
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Run --configure in your own interactive terminal.");
        Console.WriteLine("Configure localhost:3306 / earlycare_dev / earlycare_dev_user.");
        Console.Write("Enter the project account password (hidden; not the root password): ");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
        Console.WriteLine();
        if (password.Length == 0) throw new InvalidOperationException("Empty password; no changes saved.");
        var connection = new MySqlConnectionStringBuilder
        {
            Server = "localhost", Port = 3306, Database = "earlycare_dev", UserID = "earlycare_dev_user",
            Password = password.ToString(), DateTimeKind = MySqlDateTimeKind.Utc
        };
        password.Clear();
        var secretsId = typeof(ApplicationDbContext).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId
            ?? throw new InvalidOperationException("Project UserSecretsId is missing.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", secretsId);
        var path = Path.Combine(directory, "secrets.json");
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            : new JsonObject();
        if (root is null) throw new InvalidOperationException("Existing secrets file must be a JSON object; no changes saved.");
        if (root["ConnectionStrings"] is not null and not JsonObject)
            throw new InvalidOperationException("ConnectionStrings must be a JSON object; no changes saved.");
        var section = root["ConnectionStrings"] as JsonObject ?? new JsonObject();
        if (root["ConnectionStrings"] is null) root["ConnectionStrings"] = section;
        section["DefaultConnection"] = connection.ConnectionString;
        root.Remove("ConnectionStrings:DefaultConnection");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
        Console.WriteLine("Saved local DefaultConnection; password and connection string were not printed.");
        return 0;
    }
}
