namespace Earlycare.CloudMigration;

public static class DeploymentPolicy
{
    // Deliberately no CLI/environment override. Enable only after reviewed Guardian integration.
    public static void RequireApplyApproval() => throw new DeploymentException("APPLY_BLOCKED");
    public static string Mode(string[] args)
    {
        if (args.Length == 0) return "help";
        if (args.Length == 1 && args[0] is "help" or "check" or "history" or "preflight" or "apply") return args[0];
        if (args.Length == 3 && args[0] == "script" && args[1] == "--output" && !string.IsNullOrWhiteSpace(args[2])) return "script";
        throw new DeploymentException("INVALID_ARGUMENT");
    }
    public static void ValidateEndpoint(string? host, string? port, string? database, string? user)
    {
        if (host != "127.0.0.1" || port != "13306" || database != "earlycare_dev" || user != "earlycare_migration")
            throw new DeploymentException("INVALID_ENDPOINT");
    }
    public static void ValidateHistory(IReadOnlyList<string> known, IReadOnlyList<string> applied)
    {
        if (applied.Count > known.Count || !applied.SequenceEqual(known.Take(applied.Count)))
            throw new DeploymentException("MIGRATION_HISTORY_MISMATCH");
    }
    public static void ValidateServer(string version, string database, string caseRule)
    {
        if (version != "8.4.11" || database != "earlycare_dev" || caseRule != "0")
            throw new DeploymentException("INVALID_DATABASE_VERSION");
    }
}
