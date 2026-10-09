using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Data.Entities;
using EarlyInterventionCare.Api.DTOs.Questionnaires;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

public sealed class EfQuestionnaireStore(ApplicationDbContext db) : IQuestionnaireStore
{
    public async Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken ct)
    {
        var forms = await db.Questionnaires.AsNoTracking().OrderBy(q => q.QuestionnaireCode).ToListAsync(ct);
        var versions = await db.QuestionnaireVersions.AsNoTracking().Where(v => v.VersionStatus == "PUBLISHED")
            .OrderBy(v => v.RespondentRole).ThenBy(v => v.VersionNumber).ToListAsync(ct);
        return forms.Select(q => new QuestionnaireSummary(q.QuestionnaireId, q.QuestionnaireCode, q.Title,
            versions.Where(v => v.QuestionnaireId == q.QuestionnaireId).Select(v =>
                new QuestionnaireVersionSummary(v.QuestionnaireVersionId, v.RespondentRole, v.VersionNumber, v.VersionStatus)).ToArray())).ToArray();
    }

    public Task<bool> CaseExistsAsync(Guid caseId, CancellationToken ct) => db.Cases.AsNoTracking().AnyAsync(c => c.CaseId == caseId, ct);
    public Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct) =>
        db.QuestionnaireVersions.AsNoTracking().SingleOrDefaultAsync(v => v.QuestionnaireVersionId == versionId, ct);
    public Task<bool> AssignmentExistsAsync(Guid caseId, Guid questionnaireId, string role, uint round, CancellationToken ct) =>
        db.QuestionnaireTasks.AsNoTracking().AnyAsync(t => t.CaseId == caseId && t.QuestionnaireId == questionnaireId &&
            t.RespondentRole == role && t.AssignmentRound == round, ct);

    public async Task<bool> InsertAsync(QuestionnaireTask task, CancellationToken ct)
    {
        db.QuestionnaireTasks.Add(task);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 } mysql &&
            mysql.Message.Contains("uq_tasks_assignment", StringComparison.Ordinal))
        {
            db.Entry(task).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<CaseTaskResponse>> ListTasksAsync(Guid caseId, CancellationToken ct)
    {
        var rows = await (from task in db.QuestionnaireTasks.AsNoTracking()
            join version in db.QuestionnaireVersions.AsNoTracking() on task.QuestionnaireVersionId equals version.QuestionnaireVersionId
            join form in db.Questionnaires.AsNoTracking() on task.QuestionnaireId equals form.QuestionnaireId
            where task.CaseId == caseId
            orderby task.AssignmentRound, form.QuestionnaireCode, task.RespondentRole, task.TaskId
            select new { task, form.Title, version.VersionNumber }).ToListAsync(ct);
        return rows.Select(r => new CaseTaskResponse(r.task.TaskId, r.task.CaseId, r.task.QuestionnaireId,
            r.task.QuestionnaireVersionId, r.Title, r.VersionNumber, r.task.RespondentRole, r.task.AssignmentRound,
            r.task.IsRequired, r.task.TaskStatus, Utc(r.task.CreatedAtUtc), Utc(r.task.UpdatedAtUtc),
            r.task.SubmittedAtUtc is { } submitted ? Utc(submitted) : null)).ToArray();
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
