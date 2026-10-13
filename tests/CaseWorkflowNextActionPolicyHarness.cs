using EarlyInterventionCare.Api.Services.Cases;
using EarlyInterventionCare.Api.Services.Workflow;

// Offline tests: no web host, DbContext, User Secrets, or network connection.
var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}

var expectedHints = new (string Status, string Hint)[]
{
    (CaseStatusPolicy.New, "待開始處理個案"),
    (CaseStatusPolicy.ToContact, "待聯絡家長"),
    (CaseStatusPolicy.Appointed, "已安排評估"),
    (CaseStatusPolicy.WaitingForm, "等待必要問卷"),
    (CaseStatusPolicy.FormCompleted, "問卷已完成，待醫療人員確認"),
    (CaseStatusPolicy.Completed, "個案流程已完成")
};

foreach (var (status, hint) in expectedHints)
{
    var result = CaseWorkflowNextActionPolicy.Evaluate(status);
    Check(result.IsValid && result.ErrorCode is null, $"{status} is accepted");
    Check(result.CaseStatus == status && result.NextActionHint == hint,
        $"{status} preserves its code and returns the agreed hint");
}

string?[] invalidStatuses = [null, "", " \t", "UNKNOWN", "new", " NEW", "NEW ", "COMPLETED\n"];
foreach (var status in invalidStatuses)
{
    var result = CaseWorkflowNextActionPolicy.Evaluate(status);
    Check(!result.IsValid && result.ErrorCode == "INVALID_CASE_STATUS" &&
        result.CaseStatus is null && result.NextActionHint is null,
        "invalid input is rejected explicitly without a misleading hint");
}

Console.WriteLine($"PASS: {checks} offline CaseWorkflowNextActionPolicy checks; six hints and invalid input handling.");
