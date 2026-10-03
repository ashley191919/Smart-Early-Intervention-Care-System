namespace EarlyInterventionCare.Api.Services;

// Process-local only. Restart clears records; this is not durable production auditing.
public sealed class InMemoryAuditLogService(TimeProvider clock) : IAuditLogService
{
    private readonly object gate = new();
    private readonly List<AuditLogEvent> events = new();

    public bool TryWrite(AuditLogRequest request)
    {
        try
        {
            var entry = new AuditLogEvent(Guid.NewGuid(), clock.GetUtcNow(), request.ActorType,
                request.ActorId, request.Action, request.ResourceType, request.ResourceId,
                request.Result, request.RequestCorrelationId);
            lock (gate) events.Add(entry);
            return true;
        }
        catch (Exception) { return false; }
    }

    public IReadOnlyList<AuditLogEvent> Query(string? resourceType = null, string? resourceId = null, int limit = 50)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (gate)
            return events.AsEnumerable().Reverse()
                .Where(e => (resourceType == null || e.ResourceType == resourceType)
                    && (resourceId == null || e.ResourceId == resourceId))
                .Take(limit).ToArray();
    }
}
