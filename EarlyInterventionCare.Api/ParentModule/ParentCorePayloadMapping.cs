using System.Globalization;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using EarlyInterventionCare.Api.Services.Cases;

namespace EarlyInterventionCare.Api.ParentModule;

// Preparation for the reviewed core bridge, NOT a storage service or DI Gateway.
// Metadata missing from 16's current DTO stays explicit; a future bridge must not
// pass Payload alone and silently lose relationship or submit revision.
public sealed record ParentCoreDraftInput(SaveQuestionnaireDraftRequest Payload, string? RespondentRelationship);
public sealed record ParentCoreSubmitInput(QuestionnaireSubmission Payload, ulong ExpectedRevision, string RespondentRelationship);

public static class ParentCorePayloadMapping
{
    public static ulong ParseRevision(string? value)
    {
        if (string.IsNullOrEmpty(value) || (value.Length > 1 && value[0] == '0') || value.Any(c => c is < '0' or > '9') ||
            !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            throw Invalid();
        return revision;
    }

    public static string FormatRevision(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    public static bool ValidRelationship(string? value) => value is
        "FATHER" or "MOTHER" or "GRANDPARENT" or "RELATIVE" or "GUARDIAN" or "CAREGIVER" or "OTHER";

    public static ParentCoreDraftInput PrepareDraft(ParentDraftRequest input)
    {
        var revision = ParseRevision(input.ExpectedRevision);
        if ((input.RespondentRelationship is not null && !ValidRelationship(input.RespondentRelationship)) || input.Answers is null) throw Invalid();
        return new(new(input.QuestionnaireVersionId, revision, input.RespondentName, input.FilledOn,
            input.Answers.ToArray(), input.Observation), input.RespondentRelationship);
    }

    public static ParentCoreSubmitInput PrepareSubmit(ParentSubmitRequest input)
    {
        var revision = ParseRevision(input.ExpectedRevision);
        if (!ValidRelationship(input.RespondentRelationship) || input.Answers is null) throw Invalid();
        return new(new(input.QuestionnaireVersionId, input.RespondentName, input.FilledOn,
            input.Answers.ToArray(), input.Observation), revision, input.RespondentRelationship);
    }

    // Relationship must come from reviewed, author-scoped persistence, never the
    // current browser request, current Guardian relationship or a guessed default.
    public static ParentDraftResponse RestoreDraft(QuestionnaireDraftResponse input, Guid taskId,
        Guid versionId, string? storedRelationship)
    {
        if (input.TaskId != taskId || input.QuestionnaireVersionId != versionId ||
            input.TaskStatus is not ("PENDING" or "IN_PROGRESS"))
            throw new CaseAccessException("PARENT_CORE_RESPONSE_INVALID", 503, "問卷資料暫時無法確認，請稍後再試。");
        if (storedRelationship is not null && !ValidRelationship(storedRelationship))
            throw new CaseAccessException("PARENT_CORE_RESPONSE_INVALID", 503, "填表人關係資料暫時無法確認。");
        return new(input.TaskId, input.QuestionnaireVersionId, FormatRevision(input.Revision), input.RespondentName,
            input.FilledOn, storedRelationship, input.Answers.ToArray(), input.Observation, input.UpdatedAtUtc, input.TaskStatus);
    }

    private static CaseAccessException Invalid() => new("INVALID_INPUT", 400, "請確認填表人關係與草稿版本。");
}
