using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;
using EarlyInterventionCare.Api.Services.Questionnaires;

namespace EarlyInterventionCare.Api.ParentModule;

// Metadata-only precheck; never returns answers. 16's author-aware store adapter
// must replace this during reviewed integration and recheck inside its transaction.
public interface IParentAuthorVisibility
{
    Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct);
}

public sealed class LegacyParentAuthorVisibility : IParentAuthorVisibility
{
    public Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct) =>
        new MetadataParentAuthorVisibility(new LegacyParentAuthorMetadataReader()).CanRevealAsync(db, actorUserId, taskId, ct);
}

public sealed class MetadataParentAuthorVisibility(IParentAuthorMetadataReader reader) : IParentAuthorVisibility
{
    public async Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (actorUserId == Guid.Empty || taskId == Guid.Empty) return false;
        // Reader errors/cancellation propagate; never translate them into missing rows.
        var metadata = await reader.ReadAsync(db, taskId, ct);
        return metadata is not null && Allows(metadata.Draft) && Allows(metadata.Response);
        bool Allows(ParentAuthorRowMetadata? row) => row is not null && !row.HasTeacherGrant &&
            (row.Exists ? row.ParentAuthorUserId == actorUserId : row.ParentAuthorUserId is null);
    }
}

// Transitional projection for the CURRENT model. Not 16's author-aware EF reader.
// Replace only when ParentAuthorUserId columns/mapping are integrated and reviewed.
public sealed class LegacyParentAuthorMetadataReader : IParentAuthorMetadataReader
{
    public async Task<ParentTaskAuthorMetadata> ReadAsync(ApplicationDbContext db, Guid taskId, CancellationToken ct)
    {
        var draft = await db.QuestionnaireDrafts.AsNoTracking().Where(x => x.TaskId == taskId)
            .Select(x => new { HasTeacherGrant = x.LastSavedByGrantId != null }).SingleOrDefaultAsync(ct);
        var response = await db.QuestionnaireResponses.AsNoTracking().Where(x => x.TaskId == taskId)
            .Select(x => new { HasTeacherGrant = x.SubmittedByGrantId != null }).SingleOrDefaultAsync(ct);
        return new(new(draft is not null, null, draft?.HasTeacherGrant ?? false),
            new(response is not null, null, response?.HasTeacherGrant ?? false));
    }
}
