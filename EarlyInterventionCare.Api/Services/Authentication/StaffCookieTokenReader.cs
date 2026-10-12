using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace EarlyInterventionCare.Api.Services.Authentication;

public static class StaffCookieTokenReader
{
    /// <summary>
    /// Keeps an explicit Authorization header authoritative. Cookie fallback is intentionally
    /// limited to the fixed staff cookie and is unavailable until trusted origins are configured.
    /// </summary>
    public static string? Read(HttpRequest request, bool cookieAuthenticationEnabled)
    {
        if (!cookieAuthenticationEnabled || request.Headers.ContainsKey(HeaderNames.Authorization))
            return null;

        return request.Cookies.TryGetValue(StaffSessionCookieDefaults.Name, out var token) &&
               !string.IsNullOrWhiteSpace(token)
            ? token
            : null;
    }
}
