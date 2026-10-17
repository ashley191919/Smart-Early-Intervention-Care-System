using EarlyInterventionCare.Api.DTOs.StaffAccounts;

namespace EarlyInterventionCare.Api.Services.StaffAccounts;

/// <summary>
/// Server-owned source of the already-authenticated actor, never a request DTO or client userId.
/// A future adapter must read the validated Authentication Context and reject absent, ambiguous,
/// malformed or empty userId claims. An ID alone does not establish permissions or organization scope.
/// No adapter or DI registration is supplied here.
/// </summary>
public interface IStaffAccountActorContext
{
    Guid? GetAuthenticatedUserId();
}

/// <summary>Account-management boundary only; no implementation, endpoint or DI registration exists.</summary>
/// <remarks>
/// Future implementations must fail closed until shared Permission policies, ADMIN authentication,
/// allowed target roles, organization scope and credential revocation are integrated and confirmed.
/// Resolve actor only from the server-owned context, check its current ACTIVE state and current
/// permissions, and authorize target accounts and both source/destination organizations on every call.
/// A requested RoleId/OrganizationId is data to validate, never proof of authority. List filtering
/// and Total must be scoped before paging, with stable ordering and bounded page size.
/// Reject empty IDs, invalid field values, forbidden role assignments and unsupported status changes.
/// An omitted update field stays unchanged; explicit Phone null clears it. OrganizationId null/empty
/// is invalid when specified. No role, username or password changes belong in the profile update.
/// For Create, ChangeOwnPassword and ResetPassword, call StaffPasswordPolicy.Validate on the new
/// password unchanged, then store BCrypt.HashPassword (existing development baseline: workFactor 12).
/// Never apply this new-password policy to CurrentPassword; verify it with BCrypt.Verify instead.
/// Never log/return passwords or hashes, including request serialization and exception metadata.
/// Password/status changes require the confirmed revocation strategy for old JWTs and Cookies;
/// password reset/disable/re-enable must not revive old tokens. Return success only after authorized
/// persistence and required revocation succeed atomically. Do not substitute cookie deletion alone.
/// Authorization/persistence failures must not be represented as successful results or empty pages.
/// Transport adapters must retain existing HTTPS, Cookie, CSRF and Origin protection.
/// These are implementation obligations, not security checks enforced by this declaration.
/// </remarks>
public interface IStaffAccountService
{
    Task<StaffAccountResponse> CreateAsync(IStaffAccountActorContext actor,
        CreateStaffAccountRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null represents an unavailable target under the agreed resource-disclosure policy.</summary>
    Task<StaffAccountResponse?> GetByIdAsync(IStaffAccountActorContext actor,
        Guid targetUserId, CancellationToken cancellationToken = default);

    Task<StaffAccountPagedResponse> GetPagedAsync(IStaffAccountActorContext actor,
        StaffAccountQuery query, CancellationToken cancellationToken = default);

    Task<StaffAccountResponse> UpdateAsync(IStaffAccountActorContext actor,
        Guid targetUserId, UpdateStaffAccountRequest request, CancellationToken cancellationToken = default);

    Task<StaffAccountResponse> ChangeStatusAsync(IStaffAccountActorContext actor,
        Guid targetUserId, ChangeStaffAccountStatusRequest request, CancellationToken cancellationToken = default);

    /// <summary>Target is the authenticated actor; callers cannot supply another target ID.</summary>
    Task ChangeOwnPasswordAsync(IStaffAccountActorContext actor,
        ChangeOwnStaffPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Administrative reset requires independent permission and target scope verification.</summary>
    Task ResetPasswordAsync(IStaffAccountActorContext actor,
        Guid targetUserId, ResetStaffPasswordRequest request, CancellationToken cancellationToken = default);
}
