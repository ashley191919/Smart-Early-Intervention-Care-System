using System.Security.Cryptography;
using System.Text;

// Read-only source inventory, NOT an HTTP pipeline or an implemented exception handler.
internal static class PipelineSafetyReadiness
{
    internal sealed record Inventory(bool BaselineMatches, bool HasExplicitGeneralHandler,
        bool HasProblemDetailsRegistration, bool PermissionHandlerIsSpecific)
    {
        // Always blocked: names/source hashes cannot prove runtime safety, even if a
        // future source contains UseExceptionHandler. Require separately reviewed tests.
        public string BlockedReason => "PIPELINE_SAFETY: safe generic exception response is NOT implemented/accepted; " +
            "general handler, approved HTTP status (500 vs 503 undecided), fixed safe message, trace ID, " +
            "no-detail disclosure, response-started/cancellation behavior and full pipeline tests remain required";
    }

    internal static Inventory Inspect()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative)).Replace("\r\n", "\n");
        var program = Read("EarlyInterventionCare.Api/Program.cs");
        var permission = Read("EarlyInterventionCare.Api/Services/Authorization/PermissionLookupExceptionMiddleware.cs");
        string Blob(string text)
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(text);
            return Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {bytes.Length}\0").Concat(bytes).ToArray())).ToLowerInvariant();
        }
        // Anchored to 16's 4fe7b6a. No private configuration is read.
        var matches = Blob(program) == "286539db93a6f53e01cfa5253749d3a4f7469770" &&
            Blob(permission) == "80bab86b6e7f93ff4e5f1bea41e5330dc7edba89";
        return new(matches,
            program.Contains("UseExceptionHandler", StringComparison.Ordinal) || program.Contains("AddExceptionHandler", StringComparison.Ordinal),
            program.Contains("AddProblemDetails", StringComparison.Ordinal),
            permission.Contains("catch (PermissionLookupException)", StringComparison.Ordinal));
    }
}
