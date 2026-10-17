using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EarlyInterventionCare.Api.DTOs.StaffAccounts;

// Transport contracts only. IDs and requested roles/organizations confer no authority.
// Future services must reject empty IDs and validate all references and allowed values.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateStaffAccountRequest
{
    [Required, StringLength(100)]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    public Guid RoleId { get; init; }
    public Guid OrganizationId { get; init; }

    [StringLength(20)]
    public string? Phone { get; init; }
}

public sealed class StaffAccountResponse
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? LastLoginAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StaffAccountQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    [StringLength(20)]
    public string? Status { get; init; }
}

public sealed class StaffAccountPagedResponse
{
    public IReadOnlyList<StaffAccountResponse> Items { get; init; } = Array.Empty<StaffAccountResponse>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeStaffAccountStatusRequest
{
    // ACTIVE exists today; other allowed values/transitions require shared contract confirmation.
    [Required, StringLength(20)]
    public string Status { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeOwnStaffPasswordRequest
{
    // Verify the existing password with BCrypt; do not apply the new-password policy to it.
    [Required]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    public string NewPassword { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResetStaffPasswordRequest
{
    [Required]
    public string NewPassword { get; init; } = string.Empty;
}
