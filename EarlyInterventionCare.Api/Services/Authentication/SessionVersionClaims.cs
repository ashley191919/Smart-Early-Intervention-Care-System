using System.Globalization;
using System.Security.Claims;

namespace EarlyInterventionCare.Api.Services.Authentication;

/// <summary>
/// Future token contract only. Never parses raw JWTs or verifies signatures. Call TryRead only
/// with the principal produced by successful signature/issuer/audience/lifetime validation.
/// No current issuer or authentication registration uses this contract yet.
/// </summary>
public static class SessionVersionClaims
{
    public const string UserIdClaimType = "userId";
    public const string VersionClaimType = "sv";
    // Migration initialization only; never a fallback for absent claims or unavailable storage.
    public const long InitialVersion = 1;

    public static Claim Create(long persistedVersion)
    {
        if (persistedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(persistedVersion), "Session version must be positive.");

        return new Claim(VersionClaimType, persistedVersion.ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64);
    }

    public static bool TryRead(ClaimsPrincipal? principal, out Guid userId, out long version)
    {
        userId = Guid.Empty;
        version = 0;
        var identities = principal?.Identities.ToArray();
        if (identities is not { Length: 1 } || !identities[0].IsAuthenticated)
            return false;

        // JSON claim names are case-sensitive; ClaimsIdentity.FindAll itself is not.
        var claims = identities[0].Claims.ToArray();
        var ids = claims.Where(c => string.Equals(c.Type, UserIdClaimType, StringComparison.Ordinal)).ToArray();
        var versions = claims.Where(c => string.Equals(c.Type, VersionClaimType, StringComparison.Ordinal)).ToArray();
        if (ids.Length != 1 || versions.Length != 1 ||
            !TryReadId(ids[0].Value, out var parsedId))
            return false;

        // Some JWT handlers expose a small JSON integer as Integer32 even when issued as Int64.
        // Reject JSON strings, booleans, floating point values and noncanonical numeric text.
        var claim = versions[0];
        if (claim.ValueType != ClaimValueTypes.Integer64 && claim.ValueType != ClaimValueTypes.Integer32 &&
            claim.ValueType != ClaimValueTypes.Integer)
            return false;
        if (!long.TryParse(claim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion) ||
            parsedVersion <= 0 || claim.Value != parsedVersion.ToString(CultureInfo.InvariantCulture))
            return false;

        var subjects = claims.Where(c => string.Equals(c.Type, "sub", StringComparison.Ordinal)).ToArray();
        if (subjects.Length > 1 || (subjects.Length == 1 &&
            (!TryReadId(subjects[0].Value, out var subjectId) || subjectId != parsedId)))
            return false;

        userId = parsedId;
        version = parsedVersion;
        return true;
    }

    private static bool TryReadId(string value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id) && id != Guid.Empty &&
        string.Equals(value, id.ToString("D"), StringComparison.OrdinalIgnoreCase);
}
