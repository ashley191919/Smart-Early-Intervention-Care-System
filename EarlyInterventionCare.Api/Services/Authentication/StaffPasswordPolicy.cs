using System.Text;

namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Validates new staff passwords; never modifies or returns the supplied password.</summary>
public sealed class StaffPasswordPolicy
{
    public const int MinimumCharacters = 12;
    public const int MaximumUtf8Bytes = 72;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // A small baseline only, not a comprehensive compromised-password blocklist.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123456", "123456789012", "1234567890123456", "qwerty123456", "admin12345678"
    };

    public PasswordPolicyResult Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(password))
            return PasswordPolicyResult.Failure("PASSWORD_REQUIRED", "請輸入密碼。" );

        int byteCount;
        try
        {
            // Reject invalid UTF-16 rather than silently replacing unpaired surrogates.
            byteCount = StrictUtf8.GetByteCount(password);
        }
        catch (EncoderFallbackException)
        {
            return PasswordPolicyResult.Failure("PASSWORD_INVALID_UNICODE", "密碼包含無效的 Unicode 字元。" );
        }

        if (byteCount > MaximumUtf8Bytes)
            return PasswordPolicyResult.Failure("PASSWORD_TOO_LONG", "密碼不得超過 72 個 UTF-8 位元組。" );

        var characters = 0;
        foreach (var unused in password.EnumerateRunes()) characters++;
        if (characters < MinimumCharacters)
            return PasswordPolicyResult.Failure("PASSWORD_TOO_SHORT", "密碼至少需要 12 個字元。" );

        if (CommonPasswords.Contains(password))
            return PasswordPolicyResult.Failure("PASSWORD_COMMON", "此密碼過於常見，請選擇其他密碼。" );

        return PasswordPolicyResult.Success;
    }
}

/// <summary>Contains only fixed, non-sensitive validation metadata.</summary>
public sealed record PasswordPolicyResult(bool IsValid, string? Code, string? Message)
{
    internal static PasswordPolicyResult Success { get; } = new(true, null, null);
    internal static PasswordPolicyResult Failure(string code, string message) => new(false, code, message);
}
