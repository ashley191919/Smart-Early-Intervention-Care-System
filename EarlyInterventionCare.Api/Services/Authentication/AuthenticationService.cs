using System.Text;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Authentication;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Authentication;

public class AuthenticationService
{
    private readonly ApplicationDbContext _db;
    private readonly JwtTokenService _tokens;

    public AuthenticationService(ApplicationDbContext db, JwtTokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 ||
            string.IsNullOrWhiteSpace(request.Password) || Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return null;
        }

        var user = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user is null || !string.Equals(user.Status, "ACTIVE", StringComparison.Ordinal))
            return null;

        try
        {
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return null;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return null;
        }
        catch (BCrypt.Net.HashInformationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }

        var role = await _db.Roles.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        if (role is null)
            return null;

        var permissions = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToArrayAsync(cancellationToken);

        var token = _tokens.CreateToken(user.Id, user.Username, role.Name);
        return new LoginResponse
        {
            UserId = user.Id,
            Username = user.Username,
            Role = role.Name,
            Permissions = permissions,
            Message = "登入成功",
            AccessToken = token.AccessToken,
            ExpiresAt = token.ExpiresAt
        };
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Username, u.Status, u.RoleId })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !string.Equals(user.Status, "ACTIVE", StringComparison.Ordinal))
            return null;

        var role = await _db.Roles.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        if (role is null)
            return null;

        var permissions = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == role.Id).Select(rp => rp.Permission.Name)
            .Distinct().OrderBy(name => name).ToArrayAsync(cancellationToken);
        return new CurrentUserResponse
        {
            UserId = user.Id,
            Username = user.Username,
            Role = role.Name,
            Permissions = permissions
        };
    }
}
