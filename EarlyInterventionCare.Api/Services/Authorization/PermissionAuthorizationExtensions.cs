using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EarlyInterventionCare.Api.Services.Authorization;

public static class PermissionAuthorizationExtensions
{
    /// <summary>Registers the first batch's operation policies, without granting role permissions.</summary>
    public static AuthorizationOptions AddFirstBatchPermissionPolicies(this AuthorizationOptions options) => options
        .AddPermissionPolicy("Permission:case.status.update", "case.status.update")
        .AddPermissionPolicy("Permission:case.status_history.read", "case.status_history.read")
        .AddPermissionPolicy("Permission:case.read", "case.read")
        .AddPermissionPolicy("Permission:questionnaire.task.read", "questionnaire.task.read");

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
