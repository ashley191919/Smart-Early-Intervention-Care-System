using System.Text.Json;
using EarlyInterventionCare.Api.Services.Authentication;

var policy = new StaffPasswordPolicy();
var checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception(description);
}
void Rejected(string? input, string code)
{
    var result = policy.Validate(input);
    Check(!result.IsValid && result.Code == code && !string.IsNullOrEmpty(result.Message), "expected rejection code " + code);
    if (!string.IsNullOrEmpty(input))
        Check(!JsonSerializer.Serialize(result).Contains(input, StringComparison.Ordinal), "result must not contain password");
}
void Accepted(string input)
{
    var before = input;
    var result = policy.Validate(input);
    Check(result.IsValid && result.Code is null && result.Message is null, "expected acceptance");
    Check(input == before, "input must not change");
}

Rejected(null, "PASSWORD_REQUIRED");
Rejected("", "PASSWORD_REQUIRED");
Rejected(new string(' ', 12), "PASSWORD_REQUIRED");
Rejected("abcdefghijk", "PASSWORD_TOO_SHORT");
Accepted("abcdefghijkl");
Accepted(new string('a', 72));
Rejected(new string('a', 73), "PASSWORD_TOO_LONG");
Accepted("春夏秋冬天地山川日月星海");
Rejected(new string('界', 25), "PASSWORD_TOO_LONG");
Accepted("letter!@#-+=");
Accepted("  a long pass phrase  ");
Rejected("password123456", "PASSWORD_COMMON");
Rejected("PASSWORD123456", "PASSWORD_COMMON");
Accepted(" password123456 "); // No trimming; the exact supplied value is validated.
Accepted("😀😀😀😀😀😀😀😀😀😀😀😀");
Rejected("😀😀😀😀😀😀😀😀😀😀😀", "PASSWORD_TOO_SHORT");
Rejected("abcdefghijk\ud800", "PASSWORD_INVALID_UNICODE");

Console.WriteLine($"PASS: {checks} offline staff password policy checks.");
