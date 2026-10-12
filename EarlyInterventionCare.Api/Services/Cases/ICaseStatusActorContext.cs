using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Cases;

/// <summary>Supplies the authenticated actor for manual Case Status changes.</summary>
public interface ICaseStatusActorContext
{
    Guid? GetAuthenticatedUserId();
}

/// <summary>
/// Reads only the already-authenticated JWT principal. API clients never provide a
/// ChangedByUserId in a status-change request.
/// </summary>
public sealed class HttpContextCaseStatusActorContext(IHttpContextAccessor httpContextAccessor) : ICaseStatusActorContext
{
    public Guid? GetAuthenticatedUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        var claims = user.FindAll("userId").ToArray();
        return claims.Length == 1 && Guid.TryParse(claims[0].Value, out var userId) && userId != Guid.Empty
            ? userId
            : null;
    }
}
