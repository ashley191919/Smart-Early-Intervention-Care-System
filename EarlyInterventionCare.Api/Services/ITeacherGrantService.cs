using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Development;

namespace EarlyInterventionCare.Api.Services;

public interface ITeacherGrantService
{
    CreateTeacherGrantResponse Create(int expiresInSeconds);
    TestTeacherGrant? Validate(string? token);
    SubmissionResult Submit(string? token, string? question1, string? question2);
}
