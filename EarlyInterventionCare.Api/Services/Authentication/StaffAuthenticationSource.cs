namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>Records the successfully validated JWT transport for CSRF decisions.</summary>
public static class StaffAuthenticationSource
{
    private const string ItemKey = "EarlyCare.StaffAuthenticationSource";
    public const string BearerHeader = "BearerHeader";
    public const string StaffCookie = "StaffCookie";

    public static void Set(HttpContext context, string source) => context.Items[ItemKey] = source;

    public static bool IsValidatedBearerHeader(HttpContext context) =>
        string.Equals(context.Items[ItemKey] as string, BearerHeader, StringComparison.Ordinal);
}
