namespace EarlyInterventionCare.Api.Services;

// Metadata only: callers must never supply tokens, URLs, answers or sensitive content.
public sealed record AuditLogRequest(string ActorType, string ActorId, string Action,
    string ResourceType, string ResourceId, string Result, string RequestCorrelationId);

public sealed record AuditLogEvent(Guid EventId, DateTimeOffset OccurredAtUtc,
    string ActorType, string ActorId, string Action, string ResourceType,
    string ResourceId, string Result, string RequestCorrelationId);

public interface IAuditLogService
{
    // Best effort: false means no record was saved. Business state must not be rolled back.
    bool TryWrite(AuditLogRequest request);
    IReadOnlyList<AuditLogEvent> Query(string? resourceType = null, string? resourceId = null, int limit = 50);
}
