using System.Text;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Authentication;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Authentication;

public class AuthenticationService
{
    private readonly ApplicationDbContext _db;

    public AuthenticationService(ApplicationDbContext db)
    {
        _db = db;
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

        return new LoginResponse
        {
            UserId = user.Id,
            Username = user.Username,
            Role = role.Name,
            Permissions = permissions,
            Message = "帳密驗證成功"
        };
    }
}
