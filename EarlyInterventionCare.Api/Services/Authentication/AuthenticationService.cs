using System.Text;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Authentication;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly ApplicationDbContext _db;
    private readonly JwtTokenService _tokens;

    public AuthenticationService(ApplicationDbContext db, JwtTokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        LoginCoreAsync(request, staffOnly: false, cancellationToken);

    public Task<LoginResponse?> LoginStaffAsync(StaffLoginRequest request, CancellationToken cancellationToken = default) =>
        LoginCoreAsync(request, staffOnly: true, cancellationToken);

    private async Task<LoginResponse?> LoginCoreAsync(LoginRequest request, bool staffOnly,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 ||
            string.IsNullOrWhiteSpace(request.Password) || Encoding.UTF8.GetByteCount(request.Password) > 72 ||
            request.OrganizationCode?.Length > 50)
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

        // Decide from the current persisted role, never from a caller-supplied role or flag.
        var medicalRole = StaffSessionAuthorization.CanCreateStaffSession(role.Name);
        if (staffOnly && !medicalRole)
            return null;

        // Medical users cannot bypass the organization gate via the generic JWT endpoint.
        // Other roles retain code-less login, but a supplied code must never be ignored.
        if (staffOnly || medicalRole || request.OrganizationCode is not null)
        {
            var code = request.OrganizationCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var organization = await _db.Organizations.AsNoTracking()
                .Where(o => o.Id == user.OrganizationId)
                .Select(o => new { o.Code, o.Status })
                .SingleOrDefaultAsync(cancellationToken);
            // Explicit comparisons avoid relying on the database's case/accent-insensitive collation.
            if (organization is null || !string.Equals(organization.Status, "ACTIVE", StringComparison.Ordinal) ||
                !string.Equals(organization.Code, code, StringComparison.OrdinalIgnoreCase))
                return null;
        }

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
