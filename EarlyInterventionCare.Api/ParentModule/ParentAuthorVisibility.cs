using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.Services.Questionnaires;

namespace EarlyInterventionCare.Api.ParentModule;

// Metadata-only precheck; never returns answers. 16's author-aware store adapter
// must replace this during reviewed integration and recheck inside its transaction.
public interface IParentAuthorVisibility
{
    Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct);
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
