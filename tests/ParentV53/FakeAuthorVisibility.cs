using EarlyInterventionCare.Api.Data;
using EarlyInterventionCare.Api.ParentModule;
using EarlyInterventionCare.Api.Services.Questionnaires;

// Test metadata only, not a production ownership implementation.
public sealed class FakeAuthorVisibility(FakeParentGatewayState state) : IParentAuthorVisibility
{
    public Task<bool> CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct) =>
        new MetadataParentAuthorVisibility(new FixtureReader(state)).CanRevealAsync(db,actorUserId,taskId,ct);
    private sealed class FixtureReader(FakeParentGatewayState state) : IParentAuthorMetadataReader
    {
        public async Task<ParentTaskAuthorMetadata> ReadAsync(ApplicationDbContext db, Guid taskId, CancellationToken ct)
        {
            var legacy = await new EfParentAuthorMetadataReader().ReadAsync(db,taskId,ct);
            return new(state.Drafts.TryGetValue(taskId,out var d) ? new(true,d.Author,false) : legacy.Draft,
                state.Responses.TryGetValue(taskId,out var r) ? new(true,r.Author,false) : legacy.Response);
        }
    }
}
