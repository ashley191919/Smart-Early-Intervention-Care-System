namespace EarlyInterventionCare.Api.Services.Authorization;

/// <summary>
/// Reads operation permissions for an authenticated user ID supplied by the caller.
/// Resource scope (case, organization, task) must be checked separately by the owning module.
/// This service does not validate JWTs or teacher tokens.
/// </summary>
public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default);
}
