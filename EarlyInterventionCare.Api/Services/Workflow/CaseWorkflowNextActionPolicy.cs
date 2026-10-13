using EarlyInterventionCare.Api.Services.Cases;

namespace EarlyInterventionCare.Api.Services.Workflow;

/// <summary>
/// An immutable presentation result. Invalid input has no CaseStatus or next-action hint.
/// This result does not authorize an operation or request a status transition.
/// </summary>
public sealed record CaseWorkflowNextActionResult(string? CaseStatus, string? NextActionHint, string? ErrorCode)
{
    public bool IsValid => ErrorCode is null;
}

/// <summary>
/// Maps the existing Case Status contract to interim medical workspace hints.
/// Reads no persistence and neither evaluates questionnaires nor changes a Case or History.
/// </summary>
public static class CaseWorkflowNextActionPolicy
{
    /// <summary>
    /// Accepts only the exact values recognized by CaseStatusPolicy; does not trim or change case.
    /// Returns INVALID_CASE_STATUS for null, blank, or unknown input without a fallback hint.
    /// </summary>
    public static CaseWorkflowNextActionResult Evaluate(string? caseStatus)
    {
        if (!CaseStatusPolicy.IsValid(caseStatus))
            return new(null, null, "INVALID_CASE_STATUS");

        var hint = caseStatus switch
        {
            CaseStatusPolicy.New => "待開始處理個案",
            CaseStatusPolicy.ToContact => "待聯絡家長",
            CaseStatusPolicy.Appointed => "已安排評估",
            CaseStatusPolicy.WaitingForm => "等待必要問卷",
            CaseStatusPolicy.FormCompleted => "問卷已完成，待醫療人員確認",
            CaseStatusPolicy.Completed => "個案流程已完成",
            // Fail closed if the shared status contract gains a value before this mapping is updated.
            _ => null
        };

        return hint is null
            ? new(null, null, "INVALID_CASE_STATUS")
            : new(caseStatus, hint, null);
    }
}
