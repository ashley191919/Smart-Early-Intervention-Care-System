using System.ComponentModel.DataAnnotations;

namespace EarlyInterventionCare.Api.DTOs.Authentication;

/// <summary>Medical login contract; reuses the existing username/password validation.</summary>
public sealed class StaffLoginRequest : LoginRequest
{
    [Required]
    [StringLength(50)]
    public override string? OrganizationCode { get; set; }
}
