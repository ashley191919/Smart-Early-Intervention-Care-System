using System.Security.Cryptography;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Authentication;

// Offline guards/input/fixture contract checks only: no host, secrets, DB connection or writes.
// These do not claim real MySQL transaction, permission or concurrent-write acceptance.
var checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
const string local = "Server=127.0.0.1;Port=3306;Database=earlycare_dev;User ID=offline";
bool Allowed(string connection, string environment = "Development", string? asp = "Development",
    string? dotnet = "Development") => DevelopmentUserCreator.IsAllowedTarget(environment, asp, dotnet, connection);

Check(Allowed(local), "exact local Development target is accepted without opening a connection");
Check(Allowed(local, dotnet: null), "unset DOTNET environment is allowed when explicit ASP.NET Development matches host");
foreach (var invalid in new[]
{
    local.Replace("127.0.0.1", "localhost"), local.Replace("127.0.0.1", "::1"),
    local.Replace("127.0.0.1", "example.invalid"), local.Replace("127.0.0.1", "127.0.0.1,example.invalid"),
    local.Replace("3306", "13306"), local.Replace("earlycare_dev", "other_database"),
    local.Replace("earlycare_dev", "EARLYCARE_DEV"), local + ";Connection Protocol=NamedPipe",
    local + ";Server Redirection Mode=Preferred", local + ";Server Redirection Mode=Required",
    "", "unsupported-key=value"
}) Check(!Allowed(invalid), "nonpermitted or malformed target is refused");
Check(!Allowed(local, environment: "Production"), "Production host is refused");
Check(!Allowed(local, asp: null), "missing explicit Development environment is refused");
Check(!Allowed(local, asp: "Production"), "conflicting ASP.NET environment is refused");
Check(!Allowed(local, dotnet: "Production"), "conflicting DOTNET environment is refused");
Check(!DevelopmentUserCreator.IsAllowedTarget("Development", "Development", "Development", null),
    "null connection is refused");

string Read(string text)
{
    var keys = new Queue<ConsoleKeyInfo>(text.Select(c => new ConsoleKeyInfo(c, ConsoleKey.A, false, false, false)));
    keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    return DevelopmentUserCreator.ReadPassword(() => keys.Dequeue());
}
var synthetic = "  " + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)) + "符號!  ";
var previousOutput = Console.Out;
using var capturedOutput = new StringWriter();
try
{
    Console.SetOut(capturedOutput);
    Check(Read(synthetic) == synthetic, "password reader preserves exact spaces and Unicode");
    Check(Read("") == "", "empty input is not silently replaced");
    Check(Read(new string('a', 72)).Length == 72, "72 ASCII characters are not truncated");
    var keys = new Queue<ConsoleKeyInfo>(new[]
    {
        new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
        new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
        new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
        new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false),
        new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
    });
    Check(DevelopmentUserCreator.ReadPassword(() => keys.Dequeue()) == "ac", "backspace edits only entered characters");
    try { Read(new string('a', 73)); Check(false, "oversized input must not be silently truncated"); }
    catch (ArgumentException error) { Check(error.Message == "PASSWORD_TOO_LONG", "oversized input has a fixed safe error"); }
    try
    {
        DevelopmentUserCreator.ReadPassword(() => new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        Check(false, "Escape must cancel");
    }
    catch (OperationCanceledException) { Check(true, "Escape cancels"); }
}
finally { Console.SetOut(previousOutput); }
Check(capturedOutput.ToString() == "", "password reader produces no echo or output");

var hash = DevelopmentUserCreator.HashValidatedPassword(synthetic);
Check(BCrypt.Net.BCrypt.Verify(synthetic, hash), "hash matches existing BCrypt Verify login flow");
Check(hash.Split('$')[2] == "12", "BCrypt work factor remains 12");
Check(!BCrypt.Net.BCrypt.Verify(synthetic + "x", hash), "different input does not match");
foreach (var invalid in new[] { "", new string('a', 11), new string('a', 73), new string('界', 25), "password123456" })
{
    try { DevelopmentUserCreator.HashValidatedPassword(invalid); Check(false, "policy must run before hashing"); }
    catch (ArgumentException error)
    {
        Check(error.Message == new StaffPasswordPolicy().Validate(invalid).Code, "hashing returns only fixed policy error codes");
        if (invalid.Length > 0) Check(!error.Message.Contains(invalid, StringComparison.Ordinal), "error does not contain input");
    }
}

var accounts = DevelopmentUserCreator.Accounts;
Check(accounts.Count == 3, "exactly three fixed fictional account specifications");
Check(accounts.Select(a => a.Username).Distinct().Count() == 3 && accounts.All(a => a.Username.Length <= 100),
    "usernames are distinct and meet mapped length");
Check(accounts.Select(a => (a.RoleName, a.Status)).SequenceEqual(new[]
{
    ("CASE_MANAGER", "ACTIVE"), ("MEDICAL_STAFF", "ACTIVE"), ("MEDICAL_STAFF", "INACTIVE")
}), "roles and statuses match the acceptance plan; no ADMIN or nonstaff accounts");
Check(DevelopmentUserCreator.OrganizationCode.Length <= 50 && DevelopmentUserCreator.OrganizationName.Length <= 200,
    "fictional organization meets mapped lengths");
Check(typeof(DevelopmentUserCreator.AccountSpec).GetProperties().All(p =>
    !p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)), "fixture specifications contain no passwords or hashes");

Console.WriteLine($"PASS: {checks} offline development user creator checks. No database connection or writes.");
