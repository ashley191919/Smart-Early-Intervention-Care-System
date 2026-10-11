using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public sealed class QuestionnaireOperationException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

// Caller must enforce user/case access before using case operations.
public sealed class QuestionnaireService(IQuestionnaireStore store, TimeProvider clock)
{
    private static QuestionnaireOperationException Error(string code, string message, int status) => new(code, message, status);
    public Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken ct = default) => store.ListAsync(ct);

    public async Task<IReadOnlyList<CaseTaskResponse>> ListTasksAsync(Guid caseId, CancellationToken ct = default)
    {
        await RequireCase(caseId, ct);
        return await store.ListTasksAsync(caseId, ct);
    }

    public async Task<Guid> AssignAsync(Guid caseId, AssignQuestionnaireRequest input, CancellationToken ct = default)
    {
        if (input.QuestionnaireVersionId is null || input.QuestionnaireVersionId == Guid.Empty ||
            input.RespondentRole is not ("PARENT" or "TEACHER") || input.AssignmentRound is null or < 1 || input.IsRequired is null)
            throw Error("INVALID_INPUT", "請提供有效的版本、填答角色、正整數輪次與必要性。", 400);
        await RequireCase(caseId, ct);
        var version = await store.FindVersionAsync(input.QuestionnaireVersionId.Value, ct)
            ?? throw Error("VERSION_NOT_FOUND", "找不到問卷版本。", 404);
        if (version.VersionStatus != "PUBLISHED") throw Error("VERSION_NOT_PUBLISHED", "只能指派已發布版本。", 409);
        if (version.RespondentRole != input.RespondentRole) throw Error("ROLE_MISMATCH", "版本與填答角色不符。", 400);
        var round = (uint)input.AssignmentRound.Value;
        if (await store.AssignmentExistsAsync(caseId, version.QuestionnaireId, input.RespondentRole, round, ct))
            throw Duplicate();
        var now = clock.GetUtcNow().UtcDateTime;
        var task = new QuestionnaireTask { TaskId = Guid.NewGuid(), CaseId = caseId,
            QuestionnaireId = version.QuestionnaireId, QuestionnaireVersionId = version.QuestionnaireVersionId,
            RespondentRole = input.RespondentRole, AssignmentRound = round, IsRequired = input.IsRequired.Value,
            TaskStatus = "PENDING", CreatedAtUtc = now, UpdatedAtUtc = now };
        if (!await store.InsertAsync(task, ct)) throw Duplicate();
        return task.TaskId;
    }

    private static QuestionnaireOperationException Duplicate() => Error("ASSIGNMENT_EXISTS", "同個案、問卷、角色與輪次的任務已存在。", 409);
    private async Task RequireCase(Guid caseId, CancellationToken ct)
    {
        if (caseId == Guid.Empty) throw Error("INVALID_INPUT", "個案識別碼無效。", 400);
        if (!await store.CaseExistsAsync(caseId, ct)) throw Error("CASE_NOT_FOUND", "找不到個案。", 404);
    }
}
