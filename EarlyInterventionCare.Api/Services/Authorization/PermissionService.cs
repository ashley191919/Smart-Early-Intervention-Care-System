using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace EarlyInterventionCare.Api.Services.Authorization;

/// <summary>
/// Reads current database assignments without caching or granting implicit ADMIN permissions.
/// Missing/inactive users and missing assignments receive no permissions.
/// </summary>
public sealed class PermissionService(ApplicationDbContext db) : IPermissionService
{
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
            return Array.Empty<string>();

        try
        {
            // Read status, current role and assignments in one statement, avoiding a
            // change between separate account/permission reads. This is not a lock
            // spanning authorization and a later business operation.
            var rows = await (
                from user in db.Users.AsNoTracking()
                join role in db.Roles.AsNoTracking() on user.RoleId equals role.Id
                join assignment in db.RolePermissions.AsNoTracking() on role.Id equals assignment.RoleId
                join permission in db.Permissions.AsNoTracking() on assignment.PermissionId equals permission.Id
                where user.Id == userId
                select new { user.Status, permission.Name }).ToArrayAsync(cancellationToken);

            // Compare in .NET: database case-insensitive collations must not
            // interpret unexpected status values or permission names as equivalent.
            return rows.Where(row => string.Equals(row.Status, "ACTIVE", StringComparison.Ordinal))
                .Select(row => row.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is DbException or TimeoutException ||
            exception is InvalidOperationException { InnerException: DbException or TimeoutException })
        {
            // Do not carry SQL, connection details or provider messages into the
            // authorization exception exposed to the future HTTP error boundary.
            throw new PermissionLookupException();
        }
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(permission))
            return false;

        var permissions = await GetPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(permission, StringComparer.Ordinal);
    }
}
