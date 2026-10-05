# 教師操作紀錄 MySQL 本機驗收

更新日期：2026-10-06。已完成程式、第二份 AddAuditLogs migration、獨立資料庫驗證；使用者已回報本機驗收成功。原本九張核心表保留，新增第十張 `audit_logs`；不修改已套用的 InitialSharedCore、不重新建庫，也不需要重新 seed。

## 1. 停止舊服務、套用 migration

在 VS2022 按 Shift+F5 停止偵錯；若另有 `dotnet run` 的終端機，按 Ctrl+C 停止該服務。正在執行的舊程式會鎖住建置檔案。

在 VS2022「開發人員 PowerShell」或 Windows PowerShell 執行（兩者均可）：

```powershell
dotnet run --project "C:\Users\ashle\Desktop\輔仁大學\早療專題\vs 2022\Smart-Early-Intervention-Care-System\tests\CoreDatabaseHarness.csproj" -- --apply
```

沿用你已設定成功的本機 User Secrets，不必重設密碼。預期看到模型通過、migration applied，以及 `applied migrations=2, pending migrations=0`。若錯誤，不要刪資料庫或修改舊 migration；保留錯誤訊息供確認，不貼密碼。

完成後重新啟動 Development 專案。Workbench 刷新 earlycare_dev 的 Tables，應看到 `audit_logs`。

## 2. 建立、驗證及查詢

到 `/?role=teacher` 產生新的測試授權碼，開啟邀請並驗證，取得教師工作台。舊有操作不會被補寫成新紀錄；請以新版啟動後的操作驗收。

在 Workbench 執行以下唯讀查詢：

```sql
USE earlycare_dev;
SELECT event_id, occurred_at_utc,
       DATE_ADD(occurred_at_utc, INTERVAL 8 HOUR) AS occurred_at_taiwan,
       action, resource_type, resource_id, result
FROM audit_logs
WHERE resource_type = 'TeacherWorkspaceGrant'
ORDER BY occurred_at_utc DESC, event_id DESC
LIMIT 30;
```

找到本次 `TeacherWorkspace.Create` 與 `TeacherWorkspace.Verify`，兩者 `resource_id` 相同，即本次 grantId。`occurred_at_utc` 存 UTC；查詢的 Taiwan 欄位只是加 8 小時顯示，不改資料。

需要 API 核對時，在 PowerShell 執行，網址改成啟動時實際顯示的網址：

```powershell
$teacherAuditBase = 'http://127.0.0.1:5193'
$teacherAuditGrantId = '貼上本次 resource_id'
Invoke-RestMethod -Uri "$teacherAuditBase/api/dev/audit-logs?grantId=$teacherAuditGrantId&limit=50" |
    Select-Object -ExpandProperty events |
    Format-Table occurredAtUtc, action, resourceId, result
```

`grantId` 篩選已修正，可查 MySQL 工作台的事件；未指定時列最新紀錄。limit 為 1–200。查詢僅允許本機 loopback 連線與本機 Host，且僅 Development 啟用；這不是正式醫護授權接口。

## 3. 登出、撤銷與錯誤碼

- 工作台登出後，應新增 `TeacherWorkspace.Logout`。再次驗證同一有效碼，可重新登入。
- 按既有撤銷操作撤銷本次 grantId，應新增 `TeacherWorkspace.Revoke`；再輸入同一原碼驗證失敗，應新增 `TeacherWorkspace.VerifyDenied`，result 為 Denied。
- 完全不符合格式或查不到授權的錯誤碼也記為 VerifyDenied，但 resource_id 為 `unknown`，不保存輸入的碼；這種紀錄不會出現在特定 grantId 的查詢中。
- 再次產生新測試碼會替換仍 ACTIVE 的舊授權，舊 grantId 應新增 `TeacherWorkspace.Replace`。

撤銷請沿用同一網址，在 PowerShell 執行：

```powershell
Invoke-RestMethod -Method Post -Uri "$teacherAuditBase/api/dev/teacher-workspace/grants/$teacherAuditGrantId/revoke"
```

操作後重跑第 2 節 SQL 核對。重複撤銷可以產生多筆操作紀錄，不代表重複新增授權；這是每次成功請求的紀錄。

## 4. 重啟驗證

記下本次紀錄的 event_id，停止並重新啟動網站，再執行同一 SQL 或 API。相同 event_id 仍存在，代表資料庫保存成功。已撤銷的原碼也仍不能取得任務。

## 保存範圍與限制

每筆紀錄只有 eventId、occurredAtUtc、actorType、actorId、action、resourceType、resourceId、result、requestCorrelationId。不保存授權明碼、碼雜湊、cookie、邀請網址、手機、姓名、問卷答案或自由輸入內容。Actor 為開發操作員、授權持有人或未驗證來源；授權持有人不代表已確認某位教師真實身分。

目前沿用共用介面的 best-effort 契約：業務交易提交後，以獨立 DbContext 寫紀錄；紀錄寫入失敗會記固定 `AUDIT_WRITE_UNAVAILABLE` 警告，不撤回已成功的授權／撤銷。紀錄查詢失敗回安全 503 `AUDIT_UNAVAILABLE`。這不是保證零遺失的正式稽核；正式交付若要求每筆不可遺失，需再加入同交易事件／outbox 與重試機制。

沒有新增修改／刪除操作紀錄的 HTTP API。資料庫管理員仍能修改資料，此版本沒有不可竄改儲存。紀錄也不設外鍵連帶刪除，以保留事件證據。

## 已完成的獨立驗證

- 建置 0 警告、0 錯誤；九張核心表加 audit_logs 的模型檢查。
- 在專用臨時 MySQL port 33318，從一份 migration 升級為兩份，seed 重跑可查回；未操作使用者 port 3306。
- 真實 HTTP 建立、驗證、任務查詢、登出、撤銷、拒絕及替換事件；重啟後 eventId 保留、篩選及 UTC 正確。
- 模擬 audit 表暫時不可用：撤銷仍保存，紀錄查詢回安全 503，測試後還原表。
- 非本機 Host 查詢拒絕；Production 的開發紀錄與授權入口回 404。

下一步：使用者本機驗收及保存後，做既有單一虛構 SNAP-IV 任務的最小提交、原始答案保存與 grant USED；草稿及完整多問卷流程仍延後。
