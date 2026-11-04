using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.ParentModule;

// Test metadata only, not a production ownership implementation.
public sealed class FakeAuthorVisibility(FakeParentGatewayState state) : IParentAuthorVisibility
{
    public async Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct)
    {
        if (state.Drafts.TryGetValue(taskId, out var d) && d.Author != actorUserId) return false;
        if (state.Responses.TryGetValue(taskId, out var r) && r.Author != actorUserId) return false;
        return await new LegacyParentAuthorVisibility().CanRevealAsync(db, actorUserId, taskId, ct);
    }
}
