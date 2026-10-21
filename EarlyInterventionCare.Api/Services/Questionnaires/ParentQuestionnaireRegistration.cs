using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public static class ParentQuestionnaireRegistration
{
    // Explicit opt-in only AFTER schema upgrade and 11's adapter review. No Program.cs changes.
    public static IServiceCollection AddParentQuestionnaireCore(this IServiceCollection services,
        ParentQuestionnaireOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<QuestionnaireAnswerValidator>();
        services.TryAddScoped<IParentQuestionnaireStore, EfParentQuestionnaireStore>();
        services.TryAddScoped<IParentTaskAccessAdapter, DenyParentTaskAccessAdapter>();
        services.TryAddScoped<ParentQuestionnaireGateway>();
        return services;
    }
}
