using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EarlyInterventionCare.Api.Services.Authorization;

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddPermissionServices();
        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());
        return services;
    }

    /// <summary>Registers an explicit policy; this does not grant or seed the permission.</summary>
    public static AuthorizationOptions AddPermissionPolicy(
        this AuthorizationOptions options, string policyName, string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        if (policyName != policyName.Trim())
            throw new ArgumentException("Policy names cannot have leading or trailing whitespace.", nameof(policyName));
        if (options.GetPolicy(policyName) is not null)
            throw new InvalidOperationException("An authorization policy with this name already exists.");
        var requirement = new PermissionRequirement(permission);
        options.AddPolicy(policyName, policy => policy.RequireAuthenticatedUser().AddRequirements(requirement));
        return options;
    }
}
