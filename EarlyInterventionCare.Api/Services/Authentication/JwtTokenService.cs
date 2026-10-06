using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EarlyInterventionCare.Api.Options;
using Microsoft.IdentityModel.Tokens;

namespace EarlyInterventionCare.Api.Services.Authentication;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(JwtOptions options, SymmetricSecurityKey signingKey)
    {
        _options = options;
        _credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    }

    public (string AccessToken, DateTimeOffset ExpiresAt) CreateToken(Guid userId, string username, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.ExpirationMinutes);
        var id = userId.ToString("D");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, id),
            new Claim("userId", id),
            new Claim("username", username),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims,
            notBefore: now.UtcDateTime, expires: expiresAt.UtcDateTime, signingCredentials: _credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
