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
}
