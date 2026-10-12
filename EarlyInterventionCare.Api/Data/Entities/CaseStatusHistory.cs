namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class CaseStatusHistory
{
    public Guid HistoryId { get; set; }
    public Guid CaseId { get; set; }
    public string OldStatus { get; set; } = null!;
    public string NewStatus { get; set; } = null!;
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public EarlyInterventionCare.Api.Models.Authentication.User? ChangedByUser { get; set; }
    public string TriggerSource { get; set; } = null!;
    public string? Reason { get; set; }
}
