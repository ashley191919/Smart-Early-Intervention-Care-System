using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EarlyInterventionCare.Api.Services.Authorization;

public interface ICaseTaskReadScope
{
    Task<bool> CanReadCaseAsync(ClaimsPrincipal principal, Guid caseId, CancellationToken ct);
    Task<bool> CanReadTaskAsync(ClaimsPrincipal principal, Guid caseId, Guid taskId, CancellationToken ct);
}

public sealed class DenyCaseTaskReadScope : ICaseTaskReadScope
{
    public Task<bool> CanReadCaseAsync(ClaimsPrincipal principal, Guid caseId, CancellationToken ct) => Task.FromResult(false);
    public Task<bool> CanReadTaskAsync(ClaimsPrincipal principal, Guid caseId, Guid taskId, CancellationToken ct) => Task.FromResult(false);
}

/// <summary>07 supplies scope; permission success alone never grants a Case or Task.</summary>
public sealed class ScopedReadAuthorization(IAuthorizationService authorization, ICaseTaskReadScope scope)
{
    public async Task<bool> CanReadCaseAsync(ClaimsPrincipal principal, Guid caseId, CancellationToken ct = default) =>
        caseId != Guid.Empty && (await authorization.AuthorizeAsync(principal, null, "Permission:case.read")).Succeeded &&
        await scope.CanReadCaseAsync(principal, caseId, ct);
    public async Task<bool> CanReadTaskAsync(ClaimsPrincipal principal, Guid caseId, Guid taskId, CancellationToken ct = default) =>
        caseId != Guid.Empty && taskId != Guid.Empty &&
        (await authorization.AuthorizeAsync(principal, null, "Permission:questionnaire.task.read")).Succeeded &&
        await scope.CanReadTaskAsync(principal, caseId, taskId, ct);
}

public static class ScopedReadRegistration
{
    public static IServiceCollection AddScopedCaseTaskReads(this IServiceCollection services)
    {
        services.AddPermissionAuthorization();
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("Permission:case.read") is null)
                options.AddPermissionPolicy("Permission:case.read", "case.read");
            if (options.GetPolicy("Permission:questionnaire.task.read") is null)
                options.AddPermissionPolicy("Permission:questionnaire.task.read", "questionnaire.task.read");
        });
        services.TryAddScoped<ICaseTaskReadScope, DenyCaseTaskReadScope>();
        services.TryAddScoped<ScopedReadAuthorization>();
        return services;
    }
}
