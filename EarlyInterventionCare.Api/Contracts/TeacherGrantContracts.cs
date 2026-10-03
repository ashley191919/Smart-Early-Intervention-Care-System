using System.ComponentModel.DataAnnotations;

namespace EarlyInterventionCare.Api.Contracts;

public sealed class CreateTeacherGrantRequest
{
    [Range(1, 3600)]
    public int ExpiresInSeconds { get; init; } = 300;
}

public sealed record CreateTeacherGrantResponse(
    Guid GrantId, string TeacherFormUrl, DateTimeOffset ExpiresAtUtc);
