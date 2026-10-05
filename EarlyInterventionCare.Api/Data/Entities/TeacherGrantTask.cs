namespace EarlyInterventionCare.Api.Data.Entities;

/// <summary>Persistence entity; APIs must return scoped DTOs.</summary>
public sealed class TeacherGrantTask
{
    public Guid GrantId { get; set; }
    public Guid TaskId { get; set; }
    public Guid CaseId { get; set; }
    public string RespondentRole { get; set; } = null!;
}
