using Microsoft.AspNetCore.Authorization;

namespace EarlyInterventionCare.Api.Services.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission) || permission != permission.Trim())
            throw new ArgumentException("An exact, non-empty permission name is required.", nameof(permission));
        Permission = permission;
    }

    public string Permission { get; }
}
