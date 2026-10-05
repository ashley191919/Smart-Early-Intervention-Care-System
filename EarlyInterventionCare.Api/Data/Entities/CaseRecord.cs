namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class CaseRecord
{
    public Guid CaseId { get; set; }
    public string CaseCode { get; set; } = null!;
    public string ChildName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public string Sex { get; set; } = null!;
    public string CaseStatus { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
