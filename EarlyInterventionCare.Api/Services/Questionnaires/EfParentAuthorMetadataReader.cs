using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.Services.Questionnaires;

/// <summary>Unregistered server-only projection; no store, authorization or transaction creation.</summary>
public sealed class EfParentAuthorMetadataReader : IParentAuthorMetadataReader
{
    public async Task<ParentTaskAuthorMetadata> ReadAsync(ApplicationDbContext db, Guid taskId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (taskId == Guid.Empty) throw new ArgumentException("A scoped Task ID is required.", nameof(taskId));
        ct.ThrowIfCancellationRequested();

        var draft = await db.QuestionnaireDrafts.AsNoTracking().Where(d => d.TaskId == taskId)
            .Select(d => new { d.ParentAuthorUserId, HasTeacherGrant = d.LastSavedByGrantId != null })
            .SingleOrDefaultAsync(ct);
        var response = await db.QuestionnaireResponses.AsNoTracking().Where(r => r.TaskId == taskId)
            .Select(r => new { r.ParentAuthorUserId, HasTeacherGrant = r.SubmittedByGrantId != null })
            .SingleOrDefaultAsync(ct);

        return new(draft is null ? new(false, null, false) : new(true, draft.ParentAuthorUserId, draft.HasTeacherGrant),
            response is null ? new(false, null, false) : new(true, response.ParentAuthorUserId, response.HasTeacherGrant));
    }
}
