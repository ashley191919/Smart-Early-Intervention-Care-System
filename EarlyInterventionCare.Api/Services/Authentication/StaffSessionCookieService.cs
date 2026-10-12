using EarlyInterventionCare.Api.Options;
using Microsoft.AspNetCore.Http;

namespace EarlyInterventionCare.Api.Services.Authentication;

public static class StaffSessionCookieDefaults
{
    public const string Name = "__Host-earlycare-staff";
}

/// <summary>Writes only the fixed, host-only staff session cookie.</summary>
public sealed class StaffSessionCookieService
{
    public void Set(HttpResponse response, string accessToken, DateTimeOffset expiresAt)
    {
        response.Cookies.Append(StaffSessionCookieDefaults.Name, accessToken, CreateOptions(expiresAt));
    }

    public void Delete(HttpResponse response)
    {
        response.Cookies.Delete(StaffSessionCookieDefaults.Name, CreateOptions(null));
    }

    private static CookieOptions CreateOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
        IsEssential = true
    };
}
