using EarlyInterventionCare.Api.Services.Questionnaires;
using Microsoft.AspNetCore.Mvc;

namespace EarlyInterventionCare.Api.ParentModule;

public static class ParentCoreHttpErrors
{
    // Shared HTTP decisions confirmed 2026-10-31. Never expose Exception.Message.
    private static readonly IReadOnlyDictionary<string, int> Statuses = new Dictionary<string, int>
    {
        ["AUTHENTICATION_REQUIRED"] = 401,
        ["TASK_NOT_FOUND"] = 404, ["RESPONSE_NOT_FOUND"] = 404,
        ["INVALID_INPUT"] = 400, ["INVALID_ANSWERS"] = 400,
        ["VERSION_MISMATCH"] = 409, ["VERSION_UNAVAILABLE"] = 409,
        ["DRAFT_CONFLICT"] = 409, ["TASK_SUBMITTED"] = 409, ["TASK_UNAVAILABLE"] = 409,
        ["IDEMPOTENCY_CONFLICT"] = 409, ["WRITE_CONFLICT"] = 409,
        ["ACCESS_PRECHECK_CHANGED"] = 409, ["IDEMPOTENCY_VERSION_UNSUPPORTED"] = 409,
        ["PARENT_ACCESS_NOT_INTEGRATED"] = 503, ["DEFINITION_UNAVAILABLE"] = 503,
        ["STORAGE_INCONSISTENT"] = 503
    };

    public static ObjectResult Result(QuestionnaireWorkflowException exception, string traceId)
    {
        if (!Statuses.TryGetValue(exception.Code, out var status) || status != exception.Status)
            return ParentModuleErrors.Result("PARENT_CORE_UNAVAILABLE", "問卷服務暫時無法使用。", 503, traceId);
        var message = status switch
        {
            401 => "請重新登入。",
            404 => "找不到問卷資料，或目前已無法存取。",
            400 => "請確認填答內容與必填欄位。",
            409 when exception.Code == "ACCESS_PRECHECK_CHANGED" => "存取資格已變更，請重新確認後再填寫。",
            409 when exception.Code == "IDEMPOTENCY_VERSION_UNSUPPORTED" => "目前無法確認先前提交結果，請聯絡服務人員。",
            409 when exception.Code == "TASK_SUBMITTED" => "這份問卷已提交，不能再修改。",
            409 when exception.Code == "IDEMPOTENCY_CONFLICT" => "這次提交與先前內容不同，請先確認提交結果。",
            409 => "問卷資料已更新，請重新載入並確認目前狀態。",
            _ => "問卷服務暫時無法使用，請稍後再試。"
        };
        return ParentModuleErrors.Result(exception.Code, message, status, traceId);
    }
}
