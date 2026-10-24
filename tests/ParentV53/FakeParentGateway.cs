using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.EntityFrameworkCore;

// TEST EXECUTABLE ONLY. Answers/receipts are memory-only. This is not a second
// production storage engine and is never registered in the application.
public sealed class FakeParentGatewayState
{
    public Dictionary<Guid, (Guid Author, ParentDraftResponse Draft)> Drafts { get; } = new();
    public Dictionary<Guid, (Guid Author, ParentSubmissionReceipt Receipt)> Responses { get; } = new();
    public Dictionary<(Guid, Guid), (string Hash, ParentSubmissionReceipt Receipt)> Keys { get; } = new();
}
public sealed class FakeParentGateway(ApplicationDbContext db, IParentQuestionnaireAccess access,
    FakeParentGatewayState state, TimeProvider clock, QuestionnaireAnswerValidator validator) : IParentQuestionnaireGateway
{
    static CaseAccessException Error(string code, int status) => new(code, status, "隔離 Gateway 契約測試情境。");
    async Task<ParentRequestContext> Scope(ParentRequestContext request, Guid taskId, bool writing, CancellationToken ct)
    {
        var fresh = await access.RequireAsync(request.Principal, taskId, writing, db, ct);
        if (fresh.ActorUserId != request.ActorUserId || fresh.OrganizationId != request.OrganizationId) throw Error("UNAUTHENTICATED", 401);
        return fresh;
    }
    public async Task<ParentDraftResponse> GetDraftAsync(ParentRequestContext context, Guid taskId, CancellationToken ct = default)
    {
        var actor = await Scope(context, taskId, false, ct); var task = await db.QuestionnaireTasks.AsNoTracking().SingleAsync(t => t.TaskId == taskId, ct);
        if (task.TaskStatus == "SUBMITTED") throw Error("TASK_NOT_EDITABLE", 409);
        if (state.Drafts.TryGetValue(taskId, out var saved))
        { if (saved.Author != actor.ActorUserId) throw Error("DRAFT_OWNERSHIP_CONFLICT", 409); return saved.Draft; }
        return new(taskId, task.QuestionnaireVersionId, "0", null, null, "", [], null, null, task.TaskStatus);
    }
    public async Task<ParentDraftResponse> SaveDraftAsync(ParentRequestContext context, Guid taskId, ParentDraftRequest input, CancellationToken ct = default)
    {
        await Scope(context, taskId, true, ct); var draft = await GetDraftAsync(context, taskId, ct);
        if (input.QuestionnaireVersionId != draft.QuestionnaireVersionId) throw Error("VERSION_MISMATCH", 409);
        if (input.ExpectedRevision != draft.Revision) throw Error("DRAFT_CONFLICT", 409);
        await Validate(draft.QuestionnaireVersionId, input.RespondentName, input.FilledOn, input.Answers, input.Observation, false, ct);
        var result = new ParentDraftResponse(taskId, draft.QuestionnaireVersionId,
            checked(ulong.Parse(draft.Revision, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture),
            input.RespondentName, input.FilledOn, input.RespondentRelationship, input.Answers, input.Observation, clock.GetUtcNow().UtcDateTime, "IN_PROGRESS");
        state.Drafts[taskId] = (context.ActorUserId, result);
        await db.QuestionnaireTasks.Where(t => t.TaskId == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskStatus, "IN_PROGRESS"), ct);
        return result;
    }
    async Task Validate(Guid versionId, string? name, string? date, EarlyInterventionCare.Api.DTOs.Questionnaires.QuestionnaireAnswer[] answers, string? observation, bool complete, CancellationToken ct)
    {
        var snapshot = await db.QuestionnaireVersions.Where(v => v.QuestionnaireVersionId == versionId).Select(v => v.DefinitionSnapshot).SingleAsync(ct);
        try { validator.Validate(snapshot, new(versionId, name, date, answers, observation), DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), complete); }
        catch (QuestionnaireWorkflowException e) { throw new CaseAccessException(e.Code, e.Status, e.Message); }
    }
    public async Task<ParentSubmissionReceipt> SubmitAsync(ParentRequestContext context, Guid taskId, Guid key, ParentSubmitRequest input, CancellationToken ct = default)
    {
        await Scope(context, taskId, true, ct);
        if (key == Guid.Empty) throw Error("INVALID_IDEMPOTENCY_KEY", 400);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {taskId, input.QuestionnaireVersionId,
            input.ExpectedRevision, input.RespondentName, input.FilledOn, input.RespondentRelationship, input.Observation,
            Answers = input.Answers.OrderBy(x => x.QuestionId).ToArray()})));
        if (state.Keys.TryGetValue((context.ActorUserId, key), out var previous))
        { if (previous.Hash != hash) throw Error("IDEMPOTENCY_CONFLICT", 409); return previous.Receipt with { Replayed = true }; }
        if (state.Responses.ContainsKey(taskId)) throw Error("TASK_NOT_EDITABLE", 409);
        var draft = await GetDraftAsync(context, taskId, ct);
        if (draft.Revision != input.ExpectedRevision) throw Error("DRAFT_CONFLICT", 409);
        if (draft.QuestionnaireVersionId != input.QuestionnaireVersionId) throw Error("VERSION_MISMATCH", 409);
        await Validate(input.QuestionnaireVersionId, input.RespondentName, input.FilledOn, input.Answers, input.Observation, true, ct);
        var receipt = new ParentSubmissionReceipt(Guid.NewGuid(), taskId, input.QuestionnaireVersionId, clock.GetUtcNow().UtcDateTime, "SUBMITTED", false);
        // SQLite task update is fixture UI data, not production Response storage.
        await db.QuestionnaireTasks.Where(t => t.TaskId == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskStatus, "SUBMITTED")
            .SetProperty(t => t.SubmittedAtUtc, receipt.SubmittedAtUtc), ct);
        state.Keys[(context.ActorUserId, key)] = (hash, receipt); state.Responses[taskId] = (context.ActorUserId, receipt); return receipt;
    }
    public async Task<ParentSubmissionReceipt> GetReceiptAsync(ParentRequestContext context, Guid taskId, CancellationToken ct = default)
    {
        await Scope(context, taskId, false, ct);
        if (!state.Responses.TryGetValue(taskId, out var result) || result.Author != context.ActorUserId) throw Error("RESPONSE_NOT_FOUND", 404);
        return result.Receipt;
    }
}
