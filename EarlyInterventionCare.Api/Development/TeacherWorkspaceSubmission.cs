using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Development;

// Preserve the existing development API payload/response types.
public sealed record WorkspaceAnswer(string? QuestionId, string? OptionValue);
public sealed record WorkspaceSubmission(Guid QuestionnaireVersionId, string? RespondentName,
    string? FilledOn, WorkspaceAnswer?[]? Answers, string? Observation);
public sealed record WorkspaceReceipt(Guid ResponseId, Guid TaskId, Guid QuestionnaireVersionId,
    DateTime SubmittedAtUtc, string GrantStatus, bool Replayed);

public sealed partial class TeacherWorkspaceService
{
    public async Task<WorkspaceReceipt> SubmitAsync(string? token, Guid taskId, Guid key,
        WorkspaceSubmission input, string correlationId, CancellationToken ct = default)
    {
        var receipt = await workflow.SubmitAsync(token, taskId, key,
            new(input.QuestionnaireVersionId, input.RespondentName, input.FilledOn,
                input.Answers?.Select(a => a is null ? null : new QuestionnaireAnswer(a.QuestionId, a.OptionValue)).ToArray(), input.Observation), correlationId, ct);
        return new(receipt.ResponseId, receipt.TaskId, receipt.QuestionnaireVersionId,
            receipt.SubmittedAtUtc, receipt.GrantStatus, receipt.Replayed);
    }

    public Task<QuestionnaireDraftResponse> GetDraftAsync(string? token, Guid taskId, CancellationToken ct = default)
        => workflow.GetDraftAsync(token, taskId, ct);

    public Task<QuestionnaireDraftResponse> SaveDraftAsync(string? token, Guid taskId,
        SaveQuestionnaireDraftRequest input, string correlationId, CancellationToken ct = default)
        => workflow.SaveDraftAsync(token, taskId, input, correlationId, ct);

    public Task<QuestionnaireReceipt> GetReceiptAsync(string? token, Guid taskId, CancellationToken ct = default)
        => workflow.GetReceiptAsync(token, taskId, ct);
}
