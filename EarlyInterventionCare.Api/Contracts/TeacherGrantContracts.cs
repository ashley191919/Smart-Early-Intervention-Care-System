namespace EarlyInterventionCare.Api.Contracts;

public sealed class CreateTeacherGrantRequest
{
    // No expiration settings: grants remain active until submitted or revoked.
}

public sealed record CreateTeacherGrantResponse(
    Guid GrantId, string TeacherFormUrl);

// Development fixture only; this does not verify parental authority.
public sealed record RevokeTeacherGrantResponse(
    Guid GrantId, string Status, DateTimeOffset? RevokedAtUtc);
