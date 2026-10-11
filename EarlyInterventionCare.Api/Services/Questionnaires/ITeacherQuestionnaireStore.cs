using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public sealed record TeacherTaskEntry(TeacherGrantTask Link, QuestionnaireTask? Task);

// Only the existing teacher-session scope is supported. TODO(11): a separate parent
// adapter must validate Guardian/User -> Case before any parent workflow is exposed.
public interface ITeacherQuestionnaireStore
{
    Task<ITeacherQuestionnaireTransaction?> OpenAsync(byte[] sessionHash, CancellationToken ct);
}

// Implementations lock grant, then tasks in UUID order. Mutations commit atomically.
public interface ITeacherQuestionnaireTransaction : IAsyncDisposable
{
    TeacherGrant? Grant { get; }
    TeacherSession? Session { get; }
    IReadOnlyList<TeacherTaskEntry> Tasks { get; }
    Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct);
    Task<QuestionnaireDraft?> FindDraftAsync(Guid taskId, CancellationToken ct);
    Task<bool> HasResponseAsync(Guid taskId, CancellationToken ct);
    Task<QuestionnaireResponse?> FindByKeyAsync(Guid grantId, Guid key, CancellationToken ct);
    Task<QuestionnaireReceipt?> FindReceiptAsync(Guid grantId, Guid taskId, CancellationToken ct);
    void AddDraft(QuestionnaireDraft draft);
    void AddResponse(QuestionnaireResponse response);
    Task RevokeSessionsAsync(DateTime now, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
