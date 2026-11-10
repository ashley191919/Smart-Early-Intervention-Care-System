namespace EarlyInterventionCare.Api.DTOs.Cases;

public sealed record CaseDto(Guid CaseId, string CaseCode, string ChildName, DateOnly BirthDate,
    string Sex, string NationalId, string Address, string CaseStatus, uint Revision,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
