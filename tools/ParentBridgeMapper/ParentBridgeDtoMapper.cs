using System.Security.Claims;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;

namespace EarlyInterventionCare.Integration.ParentBridge;

/// <summary>
/// Unwired integration source: maps 11's DTOs into 16's parent commands.
/// No dependency on HttpContext, Gateway, DI, transactions, or qualification snapshots.
/// Compile against the real 11 module only after integration; tests use pinned declarations.
/// </summary>
public static class ParentBridgeDtoMapper
{
#if PARENT_DTO_26E8B7F
    // Latest pinned preparation parses Revision exactly once, using 11's source.
    public static ParentSaveDraftCommand MapDraft(ParentDraftRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prepared = ParentCorePayloadMapping.PrepareDraft(input);
        return new(prepared.Payload, prepared.RespondentRelationship);
    }

    public static ParentSubmitCommand MapSubmit(ParentSubmitRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prepared = ParentCorePayloadMapping.PrepareSubmit(input);
        return new(prepared.Payload, prepared.ExpectedRevision, prepared.RespondentRelationship);
    }

    // Relationship must already be read in the future author-scoped transaction.
    // No separate entity query or inference from Guardian/current request occurs here.
    public static ParentDraftResponse MapDraftResult(ParentDraftResult input, Guid taskId, Guid versionId)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Draft);
        return ParentCorePayloadMapping.RestoreDraft(input.Draft, taskId, versionId, input.RespondentRelationship);
    }

    public static ParentSubmissionReceipt MapReceipt(ParentQuestionnaireReceipt input, Guid taskId, Guid versionId)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (taskId == Guid.Empty || versionId == Guid.Empty || input.ResponseId == Guid.Empty ||
            input.TaskId != taskId || input.QuestionnaireVersionId != versionId || input.TaskStatus != "SUBMITTED" ||
            input.SubmittedAtUtc.Kind != DateTimeKind.Utc)
            throw new EarlyInterventionCare.Api.Services.Cases.CaseAccessException(
                "PARENT_CORE_RESPONSE_INVALID", 503, "問卷回執暫時無法確認。");
        return new(input.ResponseId, input.TaskId, input.QuestionnaireVersionId,
            input.SubmittedAtUtc, input.TaskStatus, input.Replayed);
    }
#else
    // Historical e2affb5 harness compatibility only. It is outside this batch's
    // allowed directory, so its old pinned declarations/exception contract stay intact.
    public static ParentSaveDraftCommand MapDraft(ParentDraftRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var revision = ParseRevision(input.ExpectedRevision);
        ValidateRelationship(input.RespondentRelationship, required: false);
        return new(new SaveQuestionnaireDraftRequest(input.QuestionnaireVersionId, revision,
            input.RespondentName, input.FilledOn, CopyAnswers(input.Answers), input.Observation),
            input.RespondentRelationship);
    }

    public static ParentSubmitCommand MapSubmit(ParentSubmitRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var revision = ParseRevision(input.ExpectedRevision);
        ValidateRelationship(input.RespondentRelationship, required: true);
        return new(new QuestionnaireSubmission(input.QuestionnaireVersionId, input.RespondentName,
            input.FilledOn, CopyAnswers(input.Answers), input.Observation), revision,
            input.RespondentRelationship);
    }
#endif

    /// <summary>Preserves all server context fields by reference, including GuardianId in the latest pin.
    /// This supplies neither a Stamp nor authorization and must never be serialized to a client.</summary>
    public static ParentRequestContext PreserveContext(ParentRequestContext context)
    {
        _ = GetOriginalPrincipal(context);
        return context;
    }

    /// <summary>
    /// Returns only the original server-supplied principal; does not authenticate or authorize it.
    /// Never bind ParentRequestContext from HTTP input. IDs/stamp are not mapped to claims.
    /// </summary>
    public static ClaimsPrincipal GetOriginalPrincipal(ParentRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Principal);
        return context.Principal;
    }

#if !PARENT_DTO_26E8B7F
    private static ulong ParseRevision(string? text) =>
        RevisionStringConverter.TryParse(text, out var revision)
            ? revision
            : throw new FormatException("Revision must be canonical UInt64 decimal text.");

    private static void ValidateRelationship(string? relationship, bool required)
    {
        if (relationship is null && !required) return;
        // Existing seven codes in 11's pinned Controller. No trimming, aliases or inference.
        if (relationship is not ("FATHER" or "MOTHER" or "GRANDPARENT" or "RELATIVE" or "GUARDIAN" or "CAREGIVER" or "OTHER"))
            throw new ArgumentException("A valid respondent relationship is required.", nameof(relationship));
    }

    // Preserve values/order/nulls for the existing validator; avoid sharing mutable arrays.
    private static QuestionnaireAnswer?[]? CopyAnswers(QuestionnaireAnswer[]? answers) => answers?.ToArray();
#endif
}
