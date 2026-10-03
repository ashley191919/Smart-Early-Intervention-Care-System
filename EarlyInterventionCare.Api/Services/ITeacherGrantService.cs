using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Development;

namespace EarlyInterventionCare.Api.Services;

public interface ITeacherGrantService
{
    CreateTeacherGrantResponse Create(int expiresInSeconds);
    TestTeacherGrant? Validate(string? token);
}
