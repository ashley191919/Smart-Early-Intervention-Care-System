namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class CaseRecord
{
    public Guid? OrganizationId { get; set; }
    public uint Revision { get; set; }
    public Guid CaseId { get; set; }
    public string CaseCode { get; set; } = null!;
    public string NationalId { get; set; } = "";
    public string Address { get; set; } = "";
    public string ChildName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public string Sex { get; set; } = null!;
    public string CaseStatus { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
