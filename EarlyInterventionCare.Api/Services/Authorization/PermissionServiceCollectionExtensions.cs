using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EarlyInterventionCare.Api.Services.Authorization;

public static class PermissionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the permission reader using the application's scoped ApplicationDbContext.
    /// API owners opt in during integration; this does not install guards or change endpoints.
    /// </summary>
    public static IServiceCollection AddPermissionServices(this IServiceCollection services)
    {
        services.TryAddScoped<IPermissionService, PermissionService>();
        return services;
    }
}
