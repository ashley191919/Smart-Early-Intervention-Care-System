# 共用操作紀錄（開發版本）

教師授權與問卷填答不限時，僅在提交成功或主動撤銷後失效。建立授權的 JSON 為 `{}`，回傳 GrantId 與 TeacherFormUrl，不再提供期限欄位。授權仍存於記憶體，服務重啟後舊連結失效。

紀錄僅存於記憶體，服務重啟後清除，尚非正式持久化稽核。未驗證家長權限或教師本人身分。

A、B 可透過 DI 注入 `IAuditLogService`，呼叫 `TryWrite(AuditLogRequest)`；服務產生 EventId 與 UTC OccurredAtUtc。回傳 false 代表未保存。紀錄失敗不能回滾已完成的業務狀態；若替換實作，呼叫端也應隔離例外。不得因失敗重做已成功的提交。

```csharp
var recorded = audit.TryWrite(new AuditLogRequest(
    ActorType: "DevelopmentTestOperator",
    ActorId: "development-test",
    Action: "ExampleModule.Operation",
    ResourceType: "ExampleResource",
    ResourceId: "synthetic-resource-id",
    Result: "Success",
    RequestCorrelationId: HttpContext.TraceIdentifier));
```

介面只提供固定中繼資料欄位，沒有自由內容或 payload。呼叫端必須只傳安全的分類、內部資源 ID 與伺服器請求關聯 ID，不傳完整連結、Token／雜湊、密碼、OTP、答案或個案內容。此通用介面不會辨識任意文字中的敏感內容。ActorId 應來自模組可信的身分脈絡，不使用使用者任意提供的識別值。

`Query(resourceType, resourceId, limit)` 回傳最新紀錄優先的獨立快照；limit 為 1–200（預設 50）。記憶體實作的寫入與讀取共用鎖，執行緒安全。

C 模組事件：

| Action | ActorType / ActorId | ResourceType / ResourceId | Result |
|---|---|---|---|
| TeacherGrant.Create | DevelopmentTestOperator / development-test | TeacherGrant / GrantId | Success |
| TeacherGrant.Revoke | DevelopmentTestOperator / development-test | TeacherGrant / GrantId | Success（僅首次） |
| TeacherResponse.Submit | TeacherGrantBearer / GrantId | TeacherGrant / GrantId | Success 或 Rejected:USED、Rejected:REVOKED |

教師提交以授權 ID 識別持有人，不代表已驗證教師本人。未知或格式錯誤 Token 不建立事件，也不保存 Token 或其雜湊。欄位驗證失敗不建立成功事件。

事件範例（虛構 ID）：

```json
{
  "eventId": "11111111-1111-1111-1111-111111111111",
  "occurredAtUtc": "2026-10-03T12:00:00+00:00",
  "actorType": "TeacherGrantBearer",
  "actorId": "22222222-2222-2222-2222-222222222222",
  "action": "TeacherResponse.Submit",
  "resourceType": "TeacherGrant",
  "resourceId": "22222222-2222-2222-2222-222222222222",
  "result": "Success",
  "requestCorrelationId": "synthetic-request-001"
}
```

Development Swagger：建立授權並保留 grantId，提交或撤銷後呼叫 `GET /api/dev/audit-logs`，填入 grantId 及 limit。回應包含暫存提示與 events。不填 grantId 時列出最新共用紀錄。所有紀錄查詢禁止快取；非 Development（包括格式錯誤查詢）回傳 404。

驗證：`tests/TestTeacherAccess.ps1` 是獨立 HTTP／記憶體驗證，不替代完整 API 建置。完整 API 的 Development 與 Production 啟動後，執行 `tests/TestTeacherSubmissionFull.ps1`，可用 DevelopmentUrl／ProductionUrl 指定位址。
