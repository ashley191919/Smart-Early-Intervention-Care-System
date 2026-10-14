using System.Net.Sockets;
using System.Security.Authentication;
using Earlycare.CloudMigration;
using EarlyInterventionCare.Api.Data;

public static class ErrorTests
{
    private const string Sensitive = "Password=SYNTHETIC-NOT-A-CREDENTIAL;Server=synthetic.invalid;Token=SYNTHETIC";
    public static async Task Run()
    {
        var count = 0;
        async Task Scenario(string stage, Exception exception, string code, int? number = null)
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            var oldOut = Console.Out; var oldError = Console.Error;
            var fake = new FaultDependencies(stage, exception, number);
            try
            {
                Console.SetOut(output); Console.SetError(error);
                if (await DeploymentEntry.RunAsync(["check"], fake) != 1) throw new Exception("Expected failure");
                var text = output.ToString() + error;
                if (!error.ToString().StartsWith(code + Environment.NewLine) || !text.Contains("Stage: " + stage))
                    throw new Exception("Wrong classification/stage");
                if (text.Contains(Sensitive) || text.Contains("SYNTHETIC") || text.Contains("Password=") || text.Contains("Token=") || text.Contains("Server="))
                    throw new Exception("Diagnostic leaked credentials");
                var expectedOpens = stage is "OPEN_CONNECTION" or "QUERY_SERVER_INFO" or "VALIDATE_SERVER_INFO" ? 1 : 0;
                var expectedQueries = stage is "QUERY_SERVER_INFO" or "VALIDATE_SERVER_INFO" ? 1 : 0;
                if (fake.OpenCalls != expectedOpens || fake.QueryCalls != expectedQueries)
                    throw new Exception("Unexpected connection/query path");
            }
            finally { Console.SetOut(oldOut); Console.SetError(oldError); }
            count++;
        }
        foreach (var number in new[] { 1045, 1698 }) await Scenario("OPEN_CONNECTION", new Exception(Sensitive), "DATABASE_AUTH_FAILED", number);
        foreach (var number in new[] { 1044, 1142, 1143, 1227 }) await Scenario("QUERY_SERVER_INFO", new Exception(Sensitive), "DATABASE_PERMISSION_DENIED", number);
        await Scenario("OPEN_CONNECTION", new Exception(Sensitive), "DATABASE_NOT_FOUND", 1049);
        foreach (var number in new[] { 1042, 2002, 2003, 2006, 2013 }) await Scenario("OPEN_CONNECTION", new Exception(Sensitive), "DATABASE_CONNECTION_FAILED", number);
        await Scenario("OPEN_CONNECTION", new SocketException((int)SocketError.ConnectionRefused), "DATABASE_CONNECTION_FAILED");
        await Scenario("QUERY_SERVER_INFO", new TimeoutException(Sensitive), "DATABASE_CONNECTION_FAILED");
        foreach (var number in new[] { 2026, 2059, 2061 }) await Scenario("OPEN_CONNECTION", new Exception(Sensitive), "DATABASE_SECURITY_NEGOTIATION_FAILED", number);
        await Scenario("OPEN_CONNECTION", new Exception(Sensitive, new AuthenticationException(Sensitive)), "DATABASE_SECURITY_NEGOTIATION_FAILED");
        await Scenario("CREATE_DATABASE_CONTEXT", new InvalidOperationException(Sensitive), "INTERNAL_ERROR");
        await Scenario("CREATE_DATABASE_CONTEXT", new TimeoutException(Sensitive), "INTERNAL_ERROR");
        await Scenario("QUERY_SERVER_INFO", new Exception(Sensitive), "INTERNAL_ERROR", 9999);
        await Scenario("VALIDATE_SERVER_INFO", new Exception(Sensitive), "INVALID_DATABASE_VERSION");
        await Scenario("OPEN_CONNECTION", new DeploymentException("APPLY_BLOCKED"), "APPLY_BLOCKED", 1045);
        await Scenario("OPEN_CONNECTION", new DeploymentException("SCHEMA_DRIFT"), "SCHEMA_DRIFT");
        // These messages resemble errors but must NOT influence classification.
        await Scenario("OPEN_CONNECTION", new Exception("1045 Access denied " + Sensitive), "INTERNAL_ERROR");
        await Scenario("PASSWORD_INPUT", new Exception(Sensitive), "INTERNAL_ERROR");
        Console.WriteLine($"PASS: {count} offline entry error/stage/redaction scenarios; no real connections.");
    }
    private sealed class FaultDependencies(string stage, Exception error, int? number) : DeploymentDependencies
    {
        public int OpenCalls, QueryCalls;
        public override string? EnvironmentValue(string name) => name.EndsWith("HOST") ? "127.0.0.1" : name.EndsWith("PORT") ? "13306" :
            name.EndsWith("DATABASE") ? "earlycare_dev" : name.EndsWith("USER") ? "earlycare_migration" : stage == "PASSWORD_INPUT" ? null : Sensitive;
        public override string PasswordInput() => throw error;
        public override ApplicationDbContext DatabaseContext(string password)
        {
            if (stage == "CREATE_DATABASE_CONTEXT") throw error;
            // Actual offline context has a connection interceptor; even accidental provider opens are forbidden.
            return new ApplicationDbContextFactory().CreateDbContext([]);
        }
        public override Task OpenConnection(ApplicationDbContext db)
        {
            OpenCalls++;
            if (stage == "OPEN_CONNECTION") throw error;
            return Task.CompletedTask; // Never call base or provider.
        }
        public override Task<List<string[]>> Read(ApplicationDbContext db, string sql)
        {
            QueryCalls++;
            if (stage == "QUERY_SERVER_INFO") throw error;
            return Task.FromResult(new List<string[]> { new[] { "synthetic-version", "earlycare_dev", "0" } });
        }
        public override int? MySqlErrorNumber(Exception exception) => number; // Synthetic provider-number adapter.
    }
}
