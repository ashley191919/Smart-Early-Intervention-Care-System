using EarlyInterventionCare.Api.Services.Cases;
using MimeKit;

namespace EarlyInterventionCare.Api.Services.Email;

public static class EmailAddressIdentity
{
    public static string Normalize(string? email)
    {
        var text = email?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 254 || text.Any(char.IsControl) ||
            !MailboxAddress.TryParse(text, out var parsed) || parsed is null || !string.IsNullOrEmpty(parsed.Name) ||
            parsed.Address != text || text.LastIndexOf('@') <= 0 || text.EndsWith('@'))
            throw new CaseAccessException("INVALID_EMAIL", 400, "請輸入有效的電子郵件地址。");
        var at = text.LastIndexOf('@');
        return text[..at] + "@" + text[(at + 1)..].ToLowerInvariant();
    }

    // Application account policy: compare case-insensitively, without merging
    // Gmail dots/+ aliases or allowing a caller to select the organization.
    public static string LookupKey(string? email) => Normalize(email).ToLowerInvariant();
}
