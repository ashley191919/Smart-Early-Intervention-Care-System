using EarlyInterventionCare.Api.Contracts;
using EarlyInterventionCare.Api.Development;

namespace EarlyInterventionCare.Api.Services;

public interface ITeacherGrantService
{
    CreateTeacherGrantResponse Create(string? requestCorrelationId = null);
    TestTeacherGrant? Validate(string? token);
    SubmissionResult Submit(string? token, string? question1, string? question2, string? requestCorrelationId = null);
    RevokeTeacherGrantResponse? Revoke(Guid grantId, string? requestCorrelationId = null);
}
