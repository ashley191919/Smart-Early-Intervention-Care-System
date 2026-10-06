using System.ComponentModel.DataAnnotations;
using System.Text;

namespace EarlyInterventionCare.Api.DTOs.Authentication;

public class LoginRequest : IValidatableObject
{
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Password is not null && Encoding.UTF8.GetByteCount(Password) > 72)
        {
            yield return new ValidationResult(
                "Password 不得超過 72 個 UTF-8 bytes。", new[] { nameof(Password) });
        }
    }
}
