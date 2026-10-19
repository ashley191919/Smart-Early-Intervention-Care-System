using EarlyInterventionCare.Api.DTOs.Authentication;

namespace EarlyInterventionCare.Api.Services.Authentication;

public interface IAuthenticationService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse?> LoginStaffAsync(StaffLoginRequest request, CancellationToken cancellationToken = default);
    Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
