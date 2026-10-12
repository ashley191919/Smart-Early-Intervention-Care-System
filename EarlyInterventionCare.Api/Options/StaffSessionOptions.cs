namespace EarlyInterventionCare.Api.Options;

/// <summary>
/// Explicit browser origins permitted to create and use the medical staff cookie session.
/// Cookie authentication stays disabled unless at least one HTTPS origin is configured.
/// </summary>
public sealed class StaffSessionOptions
{
    public const string SectionName = "StaffSession";

    private readonly HashSet<string> _trustedOrigins;

    public StaffSessionOptions(IEnumerable<string>? trustedOrigins)
    {
        _trustedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in trustedOrigins ?? Array.Empty<string>())
        {
            if (TryNormalizeOrigin(origin, out var normalized))
                _trustedOrigins.Add(normalized);
        }
    }

    public bool IsEnabled => _trustedOrigins.Count > 0;

    public bool IsTrustedOrigin(string? origin) =>
        TryNormalizeOrigin(origin, out var normalized) && _trustedOrigins.Contains(normalized);

    public static StaffSessionOptions Load(IConfiguration configuration) =>
        new(configuration.GetSection(SectionName).GetSection("TrustedOrigins").Get<string[]>());

    private static bool TryNormalizeOrigin(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath is not "" and not "/"))
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
