using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>Parent-only core. No TeacherGrant, development Case bypass or HTTP controller.</summary>
public sealed class ParentQuestionnaireGateway(IParentQuestionnaireStore store,
    IParentTaskAccessAdapter access, QuestionnaireAnswerValidator validator,
    TimeProvider clock, ParentQuestionnaireOptions options)
{
    // No DI registration or public constructor can enable the command path.
    private readonly IParentTransactionAccessAdapter commandAccess = new DenyParentTransactionAccessAdapter();

    internal ParentQuestionnaireGateway(IParentQuestionnaireStore store, IParentTaskAccessAdapter access,
        QuestionnaireAnswerValidator validator, TimeProvider clock, ParentQuestionnaireOptions options,
        IParentTransactionAccessAdapter commandAccess) : this(store, access, validator, clock, options)
    {
        this.commandAccess = commandAccess ?? throw new ArgumentNullException(nameof(commandAccess));
    }

    private static QuestionnaireWorkflowException Error(string code, int status) => new(code, code, status);
    private static Guid UserId(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.Where(i => i.IsAuthenticated).ToArray();
        var ids = identities.Length == 1 ? identities[0].FindAll("userId").ToArray() : [];
        if (ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var id) || id == Guid.Empty)
            throw Error("AUTHENTICATION_REQUIRED", 401);
        return id;
    }

    private async Task<(ParentTaskAccess Access, QuestionnaireTask Task, QuestionnaireVersion Version)> Scope(
        IParentQuestionnaireTransaction tx, ClaimsPrincipal principal, Guid taskId, CancellationToken ct,
        IParentTaskAccessAdapter? activeAccess = null)
    {
        options.Validate();
        var userId = UserId(principal);
        if (taskId == Guid.Empty) throw Error("TASK_NOT_FOUND", 404);
        var scope = await (activeAccess ?? access).LockAndAuthorizeAsync(tx, principal, taskId, ct);
        if (scope.UserId != userId || scope.OrganizationId == Guid.Empty || scope.CaseId == Guid.Empty || scope.TaskId != taskId ||
            scope.GuardianId == Guid.Empty || scope.ConsentRecordId == Guid.Empty)
            throw Error("TASK_NOT_FOUND", 404);
        var task = await tx.LockTaskAsync(taskId, ct);
        if (task is null || task.CaseId != scope.CaseId || task.RespondentRole != "PARENT" || task.TaskStatus == "CANCELLED")
            throw Error("TASK_NOT_FOUND", 404);
        if (activeAccess is not null)
        {
            // Command-only masking, AFTER fresh access and Task membership checks.
            // Do not reveal state/version errors before checking persisted authors.
            // Keep the legacy read/Submit ordering unchanged.
            Owner(await tx.FindDraftAsync(taskId, ct), await tx.FindResponseAsync(taskId, ct), scope.UserId);
        }
        if (task.TaskStatus is not ("PENDING" or "IN_PROGRESS" or "SUBMITTED"))
            throw Error("TASK_UNAVAILABLE", 409);
        var version = await tx.FindVersionAsync(task.QuestionnaireVersionId, ct);
        if (task.QuestionnaireVersionId != options.QuestionnaireVersionId || version is null ||
            version.QuestionnaireId != task.QuestionnaireId || version.RespondentRole != "PARENT" ||
            version.VersionStatus is not ("PUBLISHED" or "RETIRED"))
            throw Error("VERSION_UNAVAILABLE", 409);
        return (scope, task, version);
    }

    private static void Owner(QuestionnaireDraft? draft, QuestionnaireResponse? response, Guid userId)
    {
        if ((draft is not null && (draft.ParentAuthorUserId != userId || draft.LastSavedByGrantId is not null)) ||
            (response is not null && (response.ParentAuthorUserId != userId || response.SubmittedByGrantId is not null)))
            throw Error("TASK_NOT_FOUND", 404);
    }

    private static void Consistent(QuestionnaireTask task, QuestionnaireDraft? draft, QuestionnaireResponse? response)
    {
        // Do not manufacture a success status/receipt or repair inconsistent persisted records.
        if ((task.TaskStatus == "SUBMITTED") != (response is not null) ||
            (response is null && task.SubmittedAtUtc is not null) ||
            (draft is not null && (draft.TaskId != task.TaskId || draft.QuestionnaireVersionId != task.QuestionnaireVersionId)) ||
            (response is not null && (response.TaskId != task.TaskId || response.QuestionnaireVersionId != task.QuestionnaireVersionId ||
                task.SubmittedAtUtc is null || task.SubmittedAtUtc != response.SubmittedAtUtc)))
            throw Error("STORAGE_INCONSISTENT", 503);
    }

    private async Task Finish(IParentQuestionnaireTransaction tx, ParentTaskAccess scope, CancellationToken ct,
        IParentTaskAccessAdapter? activeAccess = null)
    {
        await (activeAccess ?? access).RevalidateAsync(tx, scope, ct);
        await tx.CommitAsync(ct);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei")).DateTime);

    private ValidatedQuestionnaireContent Validate(QuestionnaireVersion version, QuestionnaireSubmission input, bool complete)
    {
        try { return validator.Validate(version.DefinitionSnapshot, input, Today, complete); }
        catch (InvalidOperationException) { throw Error("DEFINITION_UNAVAILABLE", 503); }
    }

    public async Task<QuestionnaireDraftResponse> GetDraftAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct = default)
    {
        UserId(principal);
        await using var tx = await store.OpenAsync(ct);
        var (scope, task, _) = await Scope(tx, principal, taskId, ct);
        var draft = await tx.FindDraftAsync(taskId, ct);
        var response = await tx.FindResponseAsync(taskId, ct);
        Owner(draft, response, scope.UserId);
        Consistent(task, draft, response);
        Editable(task, response);
        var result = Draft(task, draft);
        await Finish(tx, scope, ct);
        return result;
    }

    public async Task<QuestionnaireDraftResponse> SaveDraftAsync(ClaimsPrincipal principal, Guid taskId,
        SaveQuestionnaireDraftRequest input, CancellationToken ct = default)
    {
        UserId(principal);
        await using var tx = await store.OpenAsync(ct);
        var (scope, task, version) = await Scope(tx, principal, taskId, ct);
        var draft = await tx.FindDraftAsync(taskId, ct);
        var response = await tx.FindResponseAsync(taskId, ct);
        Owner(draft, response, scope.UserId);
        Consistent(task, draft, response);
        Editable(task, response);
        if (input.QuestionnaireVersionId != task.QuestionnaireVersionId) throw Error("VERSION_MISMATCH", 409);
        if (input.ExpectedRevision is null) throw Error("INVALID_INPUT", 400);
        if (input.ExpectedRevision != (draft?.Revision ?? 0)) throw Error("DRAFT_CONFLICT", 409);
        if (draft?.Revision == ulong.MaxValue) throw Error("DRAFT_CONFLICT", 409);
        var content = Validate(version, new(input.QuestionnaireVersionId,
            input.RespondentName, input.FilledOn, input.Answers, input.Observation), false);
        if (draft is null)
        {
            draft = new() { TaskId = taskId, QuestionnaireVersionId = task.QuestionnaireVersionId,
                ParentAuthorUserId = scope.UserId };
            tx.AddDraft(draft);
        }
        draft.RespondentName = content.RespondentName; draft.FilledOn = content.FilledOn;
        draft.AnswersJson = content.AnswersJson; draft.Observation = content.Observation;
        draft.Revision++; draft.UpdatedAtUtc = Now;
        task.TaskStatus = "IN_PROGRESS"; task.UpdatedAtUtc = Now;
        var result = Draft(task, draft);
        await Finish(tx, scope, ct);
        return result;
    }

    public Task<ParentQuestionnaireReceipt> SubmitAsync(ClaimsPrincipal principal, Guid taskId, Guid key,
        QuestionnaireSubmission input, CancellationToken ct = default) =>
        SubmitCoreAsync(principal, taskId, key, input, null, null, ct);

    /// <summary>
    /// Unwired command entry. The public/default construction always denies before opening storage.
    /// Only a reviewed same-transaction adapter may eventually be supplied by an internal caller.
    /// No qualification algorithm, locks or HTTP endpoint are implemented here.
    /// </summary>
    internal Task<ParentQuestionnaireReceipt> SubmitCommandAsync(ParentGatewayCallContext context,
        Guid taskId, Guid key, ParentSubmitCommand command, CancellationToken ct = default)
    {
        if (context?.Principal is null || context.ExpectedAccess is null || command?.Payload is null)
            throw Error("INVALID_INPUT", 400);
        UserId(context.Principal);
        if (commandAccess is DenyParentTransactionAccessAdapter)
            throw Error("PARENT_ACCESS_NOT_INTEGRATED", 503);
        return SubmitCoreAsync(context.Principal, taskId, key, command.Payload, command,
            new CommandAccess(commandAccess, context), ct);
    }

    private async Task<ParentQuestionnaireReceipt> SubmitCoreAsync(ClaimsPrincipal principal, Guid taskId, Guid key,
        QuestionnaireSubmission input, ParentSubmitCommand? command, IParentTaskAccessAdapter? activeAccess,
        CancellationToken ct)
    {
        UserId(principal);
        if (key == Guid.Empty) throw Error("INVALID_INPUT", 400);
        await using var tx = await store.OpenAsync(ct);
        var (scope, task, version) = await Scope(tx, principal, taskId, ct, activeAccess);
        if (command is null && input.QuestionnaireVersionId != task.QuestionnaireVersionId) throw Error("VERSION_MISMATCH", 409);
        var draft = await tx.FindDraftAsync(taskId, ct);
        var response = await tx.FindResponseAsync(taskId, ct);
        Owner(draft, response, scope.UserId);
        Consistent(task, draft, response);
        if (command is not null && input.QuestionnaireVersionId != task.QuestionnaireVersionId)
            throw Error("VERSION_MISMATCH", 409);
        var content = Validate(version, input, true);
        var existing = await tx.FindByKeyAsync(scope.UserId, key, ct);
        if (command is not null && existing is not null)
        {
            if (existing.ParentAuthorUserId != scope.UserId || existing.SubmittedByGrantId is not null)
                throw Error("TASK_NOT_FOUND", 404);
            if (existing.TaskId != taskId || existing.IdempotencyKey != key ||
                existing.QuestionnaireVersionId != task.QuestionnaireVersionId || task.TaskStatus != "SUBMITTED" ||
                response?.ResponseId != existing.ResponseId)
                throw Error("IDEMPOTENCY_CONFLICT", 409);
            if (!string.Equals(existing.HashVersion, ParentCanonicalSubmissionEncoder.FormatVersion, StringComparison.Ordinal))
                throw Error("IDEMPOTENCY_VERSION_UNSUPPORTED", 409);
        }
        byte[] hash;
        if (command is null) hash = content.SubmissionHash(taskId, task.QuestionnaireVersionId);
        else
        {
            try { hash = new ParentCanonicalSubmissionEncoder().ComputeHash(taskId, task.QuestionnaireVersionId,
                content, command.RespondentRelationship); }
            catch (ArgumentException) { throw Error("INVALID_INPUT", 400); }
        }
        if (existing is not null)
        {
            if (existing.TaskId != taskId || existing.ParentAuthorUserId != scope.UserId ||
                existing.SubmittedByGrantId is not null || task.TaskStatus != "SUBMITTED" ||
                response?.ResponseId != existing.ResponseId || existing.PayloadHash is null ||
                !CryptographicOperations.FixedTimeEquals(existing.PayloadHash, hash) ||
                (command is not null && !string.Equals(existing.RespondentRelationship, command.RespondentRelationship, StringComparison.Ordinal)))
                throw Error("IDEMPOTENCY_CONFLICT", 409);
            await Finish(tx, scope, ct, activeAccess);
            return Receipt(existing, true);
        }
        Editable(task, response);
        if (command is not null && command.ExpectedRevision != (draft?.Revision ?? 0))
            throw Error("DRAFT_CONFLICT", 409);
        var now = Now;
        response = new() { ResponseId = Guid.NewGuid(), TaskId = taskId,
            QuestionnaireVersionId = task.QuestionnaireVersionId, ParentAuthorUserId = scope.UserId,
            RespondentName = content.RespondentName!, FilledOn = content.FilledOn!.Value,
            AnswersJson = content.AnswersJson, Observation = content.Observation,
            IdempotencyKey = key, PayloadHash = hash, SubmittedAtUtc = now };
        if (command is not null)
        {
            response.RespondentRelationship = command.RespondentRelationship;
            response.HashVersion = ParentCanonicalSubmissionEncoder.FormatVersion;
        }
        tx.AddResponse(response);
        task.TaskStatus = "SUBMITTED"; task.SubmittedAtUtc = now; task.UpdatedAtUtc = now;
        await Finish(tx, scope, ct, activeAccess);
        return Receipt(response, false);
    }

    // Compatibility shim over the already fixed transaction contract, not an authorization
    // implementation. Each operation has its own shim/evidence; never cache across requests.
    private sealed class CommandAccess(IParentTransactionAccessAdapter adapter, ParentGatewayCallContext context)
        : IParentTaskAccessAdapter
    {
        private IParentQuestionnaireTransaction? authorizedTransaction;
        private ParentTransactionAccess? evidence;
        public async Task<ParentTaskAccess> LockAndAuthorizeAsync(IParentQuestionnaireTransaction tx,
            ClaimsPrincipal principal, Guid taskId, CancellationToken ct)
        {
            var expected = context.ExpectedAccess;
            if (!ReferenceEquals(principal, context.Principal) || UserId(principal) != expected.ActorUserId ||
                expected.OrganizationId == Guid.Empty || expected.CaseId == Guid.Empty || expected.GuardianId == Guid.Empty ||
                expected.ConsentId == Guid.Empty || expected.DocumentVersionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(expected.EligibilityStamp))
                throw Error("TASK_NOT_FOUND", 404);
            var result = await adapter.LockAndAuthorizeAsync(tx, principal, taskId, expected, writing: true, ct);
            if (result is null || result.TaskId != taskId || result.Snapshot != expected)
                throw Error("TASK_NOT_FOUND", 404);
            authorizedTransaction = tx; evidence = result;
            return new(expected.ActorUserId, expected.OrganizationId, expected.CaseId, taskId, expected.GuardianId, expected.ConsentId);
        }
        public Task RevalidateAsync(IParentQuestionnaireTransaction tx, ParentTaskAccess scope, CancellationToken ct)
        {
            if (!ReferenceEquals(tx, authorizedTransaction) || evidence is null ||
                UserId(context.Principal) != scope.UserId || evidence.Snapshot != context.ExpectedAccess)
                throw Error("TASK_NOT_FOUND", 404);
            return adapter.RevalidateAsync(tx, context.Principal, evidence, ct);
        }
    }

    public async Task<ParentQuestionnaireReceipt> GetReceiptAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct = default)
    {
        UserId(principal);
        await using var tx = await store.OpenAsync(ct);
        var (scope, task, _) = await Scope(tx, principal, taskId, ct);
        var response = await tx.FindResponseAsync(taskId, ct);
        var draft = await tx.FindDraftAsync(taskId, ct);
        Owner(draft, response, scope.UserId);
        Consistent(task, draft, response);
        if (response is null || task.TaskStatus != "SUBMITTED") throw Error("RESPONSE_NOT_FOUND", 404);
        await Finish(tx, scope, ct);
        return Receipt(response, false);
    }

    public async Task<ParentTaskStatus> GetStatusAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct = default)
    {
        UserId(principal);
        await using var tx = await store.OpenAsync(ct);
        var (scope, task, _) = await Scope(tx, principal, taskId, ct);
        var draft = await tx.FindDraftAsync(taskId, ct);
        var response = await tx.FindResponseAsync(taskId, ct);
        Owner(draft, response, scope.UserId);
        Consistent(task, draft, response);
        var result = new ParentTaskStatus(taskId, task.QuestionnaireVersionId, task.TaskStatus, task.SubmittedAtUtc);
        await Finish(tx, scope, ct);
        return result;
    }

    private static void Editable(QuestionnaireTask task, QuestionnaireResponse? response)
    {
        if (task.TaskStatus == "SUBMITTED" || response is not null) throw Error("TASK_SUBMITTED", 409);
    }
    private static ParentQuestionnaireReceipt Receipt(QuestionnaireResponse response, bool replayed) =>
        new(response.ResponseId, response.TaskId, response.QuestionnaireVersionId,
            DateTime.SpecifyKind(response.SubmittedAtUtc, DateTimeKind.Utc), "SUBMITTED", replayed);
    private static QuestionnaireDraftResponse Draft(QuestionnaireTask task, QuestionnaireDraft? draft) =>
        new(task.TaskId, task.QuestionnaireVersionId, draft?.Revision ?? 0, draft?.RespondentName,
            draft?.FilledOn?.ToString("yyyy-MM-dd"), draft is null ? [] :
                JsonSerializer.Deserialize<QuestionnaireAnswer[]>(draft.AnswersJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!,
            draft?.Observation, draft is null ? null : DateTime.SpecifyKind(draft.UpdatedAtUtc, DateTimeKind.Utc), task.TaskStatus);
}
