namespace EarlyInterventionCare.Api.Data.Entities;

// Metadata only; deliberately no navigation or cascading link to mutable business records.
public sealed class AuditRecord
{
    public Guid EventId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string ActorType { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string Result { get; set; } = "";
    public string RequestCorrelationId { get; set; } = "";
}
