using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EarlyInterventionCare.Api.DTOs.Cases;

// Reject fields such as ChangedByUserId instead of silently accepting untrusted audit data.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeCaseStatusRequest
{
    [Required]
    public string? NewStatus { get; init; }

    public string? Reason { get; init; }
}

public sealed record ChangeCaseStatusResponse(Guid CaseId, string OldStatus, string NewStatus,
    DateTime ChangedAtUtc, Guid HistoryId, string? Reason);

public sealed record CaseStatusHistoryItemResponse(Guid HistoryId, Guid CaseId, string OldStatus,
    string NewStatus, DateTime ChangedAtUtc, string TriggerSource, string? Reason);

public sealed record CaseStatusHistoryPageResponse(Guid CaseId, int Page, int PageSize, int TotalCount,
    IReadOnlyList<CaseStatusHistoryItemResponse> Items);
