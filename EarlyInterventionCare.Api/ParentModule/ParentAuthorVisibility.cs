using EarlyInterventionCare.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EarlyInterventionCare.Api.ParentModule;

// Metadata-only precheck; never returns answers. 16's author-aware store adapter
// must replace this during reviewed integration and recheck inside its transaction.
public interface IParentAuthorVisibility
{
    Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct);
}

public sealed class LegacyParentAuthorVisibility : IParentAuthorVisibility
{
    public async Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct)
    {
        // This branch has no ParentAuthorUserId columns. Existing rows have unknown
        // parent ownership: never infer it from Guardian, name or teacher Grant.
        if (await db.QuestionnaireDrafts.AsNoTracking().AnyAsync(x => x.TaskId == taskId, ct)) return false;
        return !await db.QuestionnaireResponses.AsNoTracking().AnyAsync(x => x.TaskId == taskId, ct);
    }
}
