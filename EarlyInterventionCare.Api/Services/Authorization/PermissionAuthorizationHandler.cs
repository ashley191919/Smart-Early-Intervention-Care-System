using Microsoft.AspNetCore.Authorization;

namespace EarlyInterventionCare.Api.Services.Authorization;

public sealed class PermissionAuthorizationHandler(IPermissionService permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Read only the identity authenticated by the configured authentication scheme.
        // Multiple authenticated identities/IDs are ambiguous and must not be combined.
        var identities = context.User.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (identities.Length != 1)
        {
            context.Fail();
            return;
        }

        var ids = identities[0].FindAll("userId").ToArray();
        if (ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var userId) || userId == Guid.Empty)
        {
            context.Fail();
            return;
        }

        // Re-query current assignments on every evaluation. Never trust role/permission claims.
        // Query failures propagate to the API error boundary; they never grant access.
        try
        {
            if (await permissions.HasPermissionAsync(userId, requirement.Permission))
                context.Succeed(requirement);
            else
                context.Fail();
        }
        catch
        {
            // Preserve failure even if a custom caller catches the exception after
            // another handler already marked the same requirement as successful.
            context.Fail();
            throw;
        }
    }
}
