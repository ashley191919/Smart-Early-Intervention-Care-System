using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Cases;

// Reuse this exact suite with 16's real Adapter and equivalent isolated fixtures.
// Passing the Fake does NOT certify MySQL transactions/locks/rollback.
public static class ParentGatewayContractSuite
{
    public static async Task<int> RunAsync(IParentQuestionnaireGateway gateway, ParentRequestContext author,
        ParentRequestContext other, Guid taskId, Guid versionId, string today, Action<bool, string> check)
    {
        var count = 0;
        void C(bool value, string label) { check(value, label); count++; }
        async Task Error(Func<Task> work, string code) { try { await work(); } catch (CaseAccessException e) { C(e.Code == code, code); return; } throw new Exception("Expected " + code); }
        var initial = await gateway.GetDraftAsync(author, taskId);
        C(initial.Revision == "0" && initial.TaskStatus == "PENDING", "initial revision is explicit string zero");
        C(initial.RespondentRelationship is null, "new draft has no guessed relationship");
        await Error(async () => { await gateway.GetDraftAsync(author with { GuardianId = Guid.NewGuid() }, taskId); }, "ACCESS_PRECHECK_CHANGED");
        await Error(async () => { await gateway.GetDraftAsync(author with { DocumentVersionId = Guid.NewGuid() }, taskId); }, "ACCESS_PRECHECK_CHANGED");
        var input = new ParentDraftRequest(versionId, "0", "虛構填表人", today, "FATHER", [new QuestionnaireAnswer("Q1", "0")], null);
        var saved = await gateway.SaveDraftAsync(author, taskId, input);
        C(saved.Revision == "1" && saved.TaskStatus == "IN_PROGRESS", "saved revision and task state");
        C(saved.RespondentRelationship == "FATHER", "relationship is draft data");
        var restored = await gateway.GetDraftAsync(author, taskId);
        C(restored.Answers.Count == 1 && restored.Revision == "1", "resume original draft");
        await Error(async () => { await gateway.SaveDraftAsync(author, taskId, input); }, "DRAFT_CONFLICT");
        await Error(async () => { await gateway.GetDraftAsync(other, taskId); }, "TASK_NOT_FOUND");
        var submission = new ParentSubmitRequest(versionId, "1", "虛構填表人", today, "FATHER", input.Answers, null);
        var key = Guid.NewGuid(); var receipt = await gateway.SubmitAsync(author, taskId, key, submission);
        C(receipt.TaskStatus == "SUBMITTED" && !receipt.Replayed && receipt.ResponseId != Guid.Empty, "server receipt");
        var again = await gateway.SubmitAsync(author, taskId, key, submission);
        C(again.Replayed && again.ResponseId == receipt.ResponseId, "identical retry returns original response");
        C((await gateway.SubmitAsync(author, taskId, key, submission with { ExpectedRevision = "99" })).ResponseId == receipt.ResponseId, "successful replay does not recheck original revision");
        await Error(async () => { await gateway.SubmitAsync(author, taskId, key, submission with { RespondentRelationship = "MOTHER" }); }, "IDEMPOTENCY_CONFLICT");
        await Error(async () => { await gateway.SubmitAsync(author, taskId, key, submission with { RespondentName = "不同內容" }); }, "IDEMPOTENCY_CONFLICT");
        await Error(async () => { await gateway.SubmitAsync(author, taskId, Guid.NewGuid(), submission); }, "TASK_SUBMITTED");
        C((await gateway.GetReceiptAsync(author, taskId)).ResponseId == receipt.ResponseId, "own receipt reload");
        await Error(async () => { await gateway.GetReceiptAsync(other, taskId); }, "TASK_NOT_FOUND");
        return count;
    }
}
