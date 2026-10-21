using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        // Check final options, including registrations made AFTER this extension.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthorizationOptions>, ScopedReadPolicyValidation>());
        return services;
    }
}

internal sealed class ScopedReadPolicyValidation : IValidateOptions<AuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthorizationOptions options)
    {
        foreach (var permission in new[] { "case.read", "questionnaire.task.read" })
        {
            var policy = options.GetPolicy("Permission:" + permission);
            if (policy is null || policy.AuthenticationSchemes.Count != 0 || policy.Requirements.Count != 2 ||
                policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Count() != 1 ||
                policy.Requirements.OfType<PermissionRequirement>().Count(r => r.Permission == permission) != 1)
                return ValidateOptionsResult.Fail("Scoped read policy configuration conflicts with its required permission.");
        }
        return ValidateOptionsResult.Success;
    }
}
