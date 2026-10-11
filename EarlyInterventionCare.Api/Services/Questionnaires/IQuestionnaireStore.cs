using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public interface IQuestionnaireStore
{
    Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken ct);
    Task<bool> CaseExistsAsync(Guid caseId, CancellationToken ct);
    Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct);
    Task<bool> AssignmentExistsAsync(Guid caseId, Guid questionnaireId, string role, uint round, CancellationToken ct);
    // False means the database unique assignment key rejected a concurrent duplicate.
    Task<bool> InsertAsync(QuestionnaireTask task, CancellationToken ct);
    Task<IReadOnlyList<CaseTaskResponse>> ListTasksAsync(Guid caseId, CancellationToken ct);
}
