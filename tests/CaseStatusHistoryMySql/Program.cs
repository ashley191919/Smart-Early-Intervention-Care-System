using HistoryAcceptance;

// No configuration, User Secrets, environment passwords or API host startup.
return await Entry.RunAsync(args, MySqlChecks.RunAsync);

namespace HistoryAcceptance
{
    internal static class Entry
    {
        internal static async Task<int> RunAsync(string[] args, Func<Guid, Task> mysql)
        {
            try
            {
                Safety.Scenario = "ENTRY"; Safety.Step = "ARGUMENTS";
                if (args.Length == 0 || args.SequenceEqual(new[] { "--offline" }))
                {
                    await OfflineChecks.RunAsync();
                    return 0;
                }
                if (args.SequenceEqual(new[] { "--help" }))
                {
                    Console.WriteLine("Default/--offline: no credentials or connections. Approved isolated run only: --mysql --server-uuid <UUID>. See docs/database/case-status-history-mysql-acceptance.md.");
                    return 0;
                }
                var uuid = Safety.ParseMySqlArgs(args);
                await mysql(uuid);
                return 0;
            }
            catch (AcceptanceFailure failure)
            {
                Console.Error.WriteLine(Safety.Failure(failure.Code));
                return 1;
            }
            catch (Exception)
            {
                // Never print provider exception messages, connection strings or row values.
                Console.Error.WriteLine(Safety.Failure("UNEXPECTED"));
                return 1;
            }
        }
    }
}
