using System.Security.Claims;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

// Mandatory server-side adapter. Implement with 11's CaseAccessService; never bind from HTTP input.
// No implementation or DI registration is supplied here: this is not a parent API.
public interface IParentQuestionnaireDefinitionAccess
{
    Task<bool> CanReadParentTaskAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct);
}

public sealed record QuestionnaireDefinitionSource(Guid TaskId, Guid QuestionnaireId, Guid VersionId,
    string RespondentRole, string TaskStatus, Guid? VersionQuestionnaireId, string? VersionRole,
    string? VersionNumber, string? VersionStatus, string? DefinitionSnapshot);

public interface IQuestionnaireDefinitionStore
{
    Task<QuestionnaireDefinitionSource?> FindAsync(Guid taskId, CancellationToken ct);
}

// Read-only projection: never load answers, scoring, drafts, grants or author identity.
public sealed class EfQuestionnaireDefinitionStore(ApplicationDbContext db) : IQuestionnaireDefinitionStore
{
    public Task<QuestionnaireDefinitionSource?> FindAsync(Guid taskId, CancellationToken ct) =>
        (from task in db.QuestionnaireTasks.AsNoTracking()
         join version in db.QuestionnaireVersions.AsNoTracking()
             on task.QuestionnaireVersionId equals version.QuestionnaireVersionId into versions
         from version in versions.DefaultIfEmpty()
         where task.TaskId == taskId
         select new QuestionnaireDefinitionSource(task.TaskId, task.QuestionnaireId,
             task.QuestionnaireVersionId, task.RespondentRole, task.TaskStatus,
             version == null ? null : version.QuestionnaireId,
             version == null ? null : version.RespondentRole,
             version == null ? null : version.VersionNumber,
             version == null ? null : version.VersionStatus,
             version == null ? null : version.DefinitionSnapshot)).SingleOrDefaultAsync(ct);
}

public sealed class QuestionnaireDefinitionService(IQuestionnaireDefinitionStore store,
    IParentQuestionnaireDefinitionAccess access, QuestionnaireAnswerValidator validator)
{
    public async Task<QuestionnaireDefinitionResponse> GetForParentAsync(ClaimsPrincipal principal,
        Guid taskId, CancellationToken ct = default)
    {
        if (!principal.Identities.Any(i => i.IsAuthenticated))
            throw new QuestionnaireWorkflowException("AUTHENTICATION_REQUIRED", "Authentication required.", 401);
        if (taskId == Guid.Empty || !await access.CanReadParentTaskAsync(principal, taskId, ct))
            throw NotFound();
        var row = await store.FindAsync(taskId, ct);
        if (row is null || row.TaskId != taskId || row.RespondentRole != "PARENT" || row.TaskStatus == "CANCELLED")
            throw NotFound();
        if (row.TaskStatus is not ("PENDING" or "IN_PROGRESS" or "SUBMITTED") ||
            row.VersionId == Guid.Empty || row.VersionQuestionnaireId != row.QuestionnaireId ||
            row.VersionRole != "PARENT" || row.VersionStatus is not ("PUBLISHED" or "RETIRED") ||
            string.IsNullOrWhiteSpace(row.VersionNumber) || row.DefinitionSnapshot is null)
            throw Unavailable();
        try
        {
            // Reuse actual write validator's structure rules without changing submit/draft semantics.
            validator.Validate(row.DefinitionSnapshot,
                new QuestionnaireSubmission(row.VersionId, null, null, [], null), DateOnly.MinValue, false);
            using var document = JsonDocument.Parse(row.DefinitionSnapshot);
            var root = document.RootElement;
            if (root.TryGetProperty("respondentRole", out var role) &&
                (role.ValueKind != JsonValueKind.String || role.GetString() != "PARENT")) throw Unavailable();
            var title = Text(root, "title");
            var instructions = root.TryGetProperty("instructions", out var instruction)
                ? instruction.ValueKind == JsonValueKind.String ? instruction.GetString() : throw Unavailable()
                : null;
            var questions = root.GetProperty("questions").EnumerateArray().Select((q, index) =>
            {
                // Legacy snapshots without order use their original array position, never question ID sorting.
                var order = q.TryGetProperty("order", out var value)
                    ? value.TryGetInt32(out var number) && number > 0 ? number : throw Unavailable()
                    : index + 1;
                return new QuestionnaireDefinitionQuestion(Text(q, "questionId"), order, Text(q, "text"),
                    "singleChoice", q.TryGetProperty("required", out var required) ? required.GetBoolean() : null,
                    true, q.GetProperty("options").EnumerateArray()
                        .Select(o => new QuestionnaireDefinitionOption(Text(o, "value"), Text(o, "label"))).ToArray());
            }).OrderBy(q => q.Order).ToArray();
            if (questions.Select(q => q.Order).Distinct().Count() != questions.Length) throw Unavailable();
            return new(row.TaskId, row.QuestionnaireId, row.VersionId, title, row.VersionNumber, instructions, questions);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Do not expose snapshot contents or mistake server configuration failures for invalid answers.
            throw Unavailable();
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw Unavailable();
    private static QuestionnaireWorkflowException NotFound() => new("TASK_NOT_FOUND", "Task unavailable.", 404);
    private static QuestionnaireWorkflowException Unavailable() => new("DEFINITION_UNAVAILABLE", "Questionnaire definition unavailable.", 503);
}
