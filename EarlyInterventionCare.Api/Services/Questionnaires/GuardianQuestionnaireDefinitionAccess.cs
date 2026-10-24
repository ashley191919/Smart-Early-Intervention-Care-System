using System.Security.Claims;
using EarlyInterventionCare.Api.Services.Cases;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

// The same current account, verified Email and Guardian scope used by the parent API.
// This grants definition access only, never access to answers or another author's draft.
public sealed class GuardianQuestionnaireDefinitionAccess(CaseAccessService access)
    : IParentQuestionnaireDefinitionAccess
{
    public Task<bool> CanReadParentTaskAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct)
        => access.CanReadParentTaskAsync(principal, taskId, ct);
}

public static class ParentQuestionnaireDefinitionRegistration
{
    public static IServiceCollection AddParentQuestionnaireDefinitions(this IServiceCollection services)
    {
        EarlyInterventionCare.Api.ParentModule.ParentModuleRegistration.AddParentV53(services);
        services.AddSingleton<QuestionnaireAnswerValidator>();
        services.AddScoped<IParentQuestionnaireDefinitionAccess, GuardianQuestionnaireDefinitionAccess>();
        services.AddScoped<IQuestionnaireDefinitionStore, EfQuestionnaireDefinitionStore>();
        services.AddScoped<QuestionnaireDefinitionService>();
        return services;
    }
}
