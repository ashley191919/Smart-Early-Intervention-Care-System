using System.Globalization;
using System.Text.Json;
using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Development;

/// <summary>Explicit, local development fixture. Never run automatically at startup.</summary>
public static class CoreDevelopmentSeed
{
    public static readonly Guid CaseId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    public static readonly Guid QuestionnaireId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    public static readonly Guid VersionId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    public static readonly Guid TaskId = Guid.Parse("40000000-0000-4000-8000-000000000001");

    public static async Task EnsureAsync(ApplicationDbContext context, string fixturePath)
    {
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var source = fixture.RootElement;
        var options = source.GetProperty("options").EnumerateArray().Select((option, index) => new
        {
            value = index.ToString(CultureInfo.InvariantCulture), label = option.GetString(), score = index
        }).ToArray();
        var questions = source.GetProperty("questions").EnumerateArray().Select((question, index) => new
        {
            questionId = $"SNAP_IV_Q{index + 1:00}", order = index + 1, text = question.GetString(),
            type = "singleChoice", required = true, options
        }).ToArray();
        if (questions.Length != 26 || options.Length != 4)
            throw new InvalidOperationException("Unexpected development SNAP fixture.");
        var snapshot = JsonSerializer.Serialize(new
        {
            title = source.GetProperty("title").GetString(),
            instructions = source.GetProperty("instructions").GetString(),
            sourceReference = source.GetProperty("source").GetString(),
            respondentRole = "TEACHER", isDevelopment = true, questions
        });
        var now = DateTime.UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync();
        if (!await context.Cases.AnyAsync(e => e.CaseId == CaseId))
            context.Cases.Add(new CaseRecord
            {
                CaseId = CaseId, CaseCode = "CASE-DEMO-001", ChildName = "測試幼兒（虛構）",
                BirthDate = new DateOnly(2022, 10, 1), Sex = "UNKNOWN", CaseStatus = "WAITING_FORM",
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
        if (!await context.Questionnaires.AnyAsync(e => e.QuestionnaireId == QuestionnaireId))
            context.Questionnaires.Add(new Questionnaire
            {
                QuestionnaireId = QuestionnaireId, QuestionnaireCode = "SNAP_IV",
                Title = "SNAP-IV 評量表", CreatedAtUtc = now
            });
        if (!await context.QuestionnaireVersions.AnyAsync(e => e.QuestionnaireVersionId == VersionId))
            context.QuestionnaireVersions.Add(new QuestionnaireVersion
            {
                QuestionnaireVersionId = VersionId, QuestionnaireId = QuestionnaireId,
                RespondentRole = "TEACHER", VersionNumber = "1.0.0", VersionStatus = "PUBLISHED",
                DefinitionSnapshot = snapshot, ScoringDefinition = null, CreatedAtUtc = now, PublishedAtUtc = now
            });
        if (!await context.QuestionnaireTasks.AnyAsync(e => e.TaskId == TaskId))
            context.QuestionnaireTasks.Add(new QuestionnaireTask
            {
                TaskId = TaskId, CaseId = CaseId, QuestionnaireId = QuestionnaireId,
                QuestionnaireVersionId = VersionId, RespondentRole = "TEACHER", AssignmentRound = 1,
                IsRequired = true, TaskStatus = "PENDING", CreatedAtUtc = now, UpdatedAtUtc = now
            });
        // Unique keys reject conflicting IDs/codes; never overwrite a published version or existing answers.
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
