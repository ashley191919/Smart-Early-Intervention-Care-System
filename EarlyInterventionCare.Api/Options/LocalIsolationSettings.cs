using MySqlConnector;

namespace EarlyInterventionCare.Api.Options;

public static class LocalIsolationSettings
{
    public const string EnvironmentName = "IsolatedTest";
    public const string Url = "http://127.0.0.1:5186";
    public static void ValidateConnection(string? connection)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException();
            var value = new MySqlConnectionStringBuilder(connection);
            if (value.Server != "127.0.0.1" || value.Port != 33326 ||
                value.Database != "earlycare_v53_synthetic" || value.UserID != "earlycare_v53_app" ||
                string.IsNullOrWhiteSpace(value.Password) || value.Password.StartsWith("REPLACE", StringComparison.Ordinal) ||
                value.AllowLoadLocalInfile || value.AllowUserVariables)
                throw new InvalidOperationException();
        }
        catch
        {
            // Never include provider exception/connection strings in startup errors.
            throw new InvalidOperationException("LOCAL_ISOLATION_CONFIGURATION_REJECTED");
        }
    }
}
