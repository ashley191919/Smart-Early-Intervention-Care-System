# G3 Task／Completion GET 專用授權接線

整合起點：07 `c76020edcd90be962ff8a4449fe1a55632a4a47a`，獨立分支 `codex/v53-g3-get-auth`；16 來源比較基準 `36c15eb3fc1fcc4879e7b472a9782311ee80ad7c`。三份正式讀取 Service／Store／DTO 在兩個固定版本沒有差異。本批不搬入 16 未提交測試、不整個合併分支。

## 正式修改與保留的 Gate

只修改 QuestionnairesController：兩個 GET 共用新增的 CheckTaskRead。順序為唯一已認證 identity／合法 userId → 既有 DB ACTIVE 帳號與 ADMIN／CASE_MANAGER Gate → 原 Development／固定 Case Gate → Permission:questionnaire.task.read → 07 IMedicalTaskCaseReadScope → 原 caseId 限定查詢。

既有 Program 的 Scoped CaseAccessService／CaseAccessMedicalTaskCaseReadScope 與 Permission 註冊不變。建構式增加可選授權相依以保持舊手動建構相容；缺任一相依即 503，不會回退舊授權。正常 Host DI 會解析既有服務。POST Assign、目錄 List、CheckUser、CaseAllowed、Service、Store、DTO、認證、CSRF、Schema 及所有 DenyAll 不變。

有效身分缺 Task Permission 回 403 FORBIDDEN；通過原 Gate 及 Permission 後 Scope=false 回 404 CASE_NOT_FOUND，不能區分不存在／不可讀。實際 CaseAccessException 的已知 401 UNAUTHENTICATED 在 GET guard 轉為固定安全訊息；未知例外不猜測狀態／不吞掉。CaseAccessService 也有家長分支，因此醫療角色 Gate 必須在呼叫前執行。

**不是一般 Case 開放版本**：MEDICAL_STAFF、IsolatedTest、Production、非既有 Development Case 均仍拒絕。即使同院且有 Permission，未通過原 Gate 仍不會進 Scope。兩個 GET 的 HttpGet 路由、跨輪次 Completion、零必要 false、取消必要仍計數與空 Task [] 沒有改變。

## 無 Socket 測試

延伸 07 既有 Harness；不新增第二套授權規則。真實 Controller、AuthorizationService／PermissionAuthorizationHandler／PermissionService、CaseAccess Adapter／Service、EfQuestionnaireStore 與 QuestionnaireService 都執行。EF 連線 Open 被攔截，只有合成 ADO 列，未知命令及寫入拒絕，不讀秘密設定、不啟動 HTTP、不連線 MySQL。

```powershell
dotnet restore tests/MedicalTaskCaseReadScopeHarness.csproj --source . -p:NuGetAudit=false
# 本機須先有固定專案所需套件；缺套件時另核准還原，不改套件版本。
dotnet build tests/MedicalTaskCaseReadScopeHarness.csproj --no-restore -p:NuGetAudit=false
# 僅在建置成功後執行：
dotnet tests/bin/Debug/net8.0/MedicalTaskCaseReadScopeHarness.dll
$LASTEXITCODE
```

本批 48 PASS、0 FAIL、1 BLOCKED，退出碼 2；0 錯誤／0 警告。包含既有 Scoped 容器對 Controller 建構式的實際解析測試。例外捕捉僅為 Harness 結果輸出，不改正式 Filter 或安全 Gate。初次測試因測試 DI 缺 EndpointDataSource 失敗，已只在測試加入 AddRouting 後重跑通過；沒有放寬授權。

既有回歸：QuestionnairePhase21 指派／完成度通過；RBAC Policy 43 PASS、PermissionService 34 PASS、真實 AuthenticationService 合成驗收 162 PASS、StaffSession 29 PASS，均退出碼 0。上述均離線執行，非完整 HTTP／資料庫驗收。

覆蓋同 Scope 空 Task／Completion、缺 Permission 時不查 Case／Task、Scope=false 的不存在／跨院合成分支、未認證／錯誤身分、停用帳號／院所、角色／環境／固定個案 Gate、缺 DI、Scope 查詢錯誤及 POST 原 Gate／輸入驗證。SQL 斷言要求 CaseScope 的 organization_id／case_id 參數與 Task 的 caseId 條件；替身不是 MySQL 引擎，跨院與不存在用 CaseVisible=false 注入相同查詢結果，不能稱資料庫隔離實測通過。

## BLOCKED：完整 HTTP／資料庫驗收

本 Harness 的單一 BLOCKED 集合涵蓋真 JWT／Cookie handler、401 challenge、403 forbid、MVC formatter、HTTPS／CSRF、安全 500／traceId、真 MySQL 篩選／Schema。Controller 的 401 JSON 只涵蓋到達 action 後的 guard；匿名請求若由 Authorize 先 challenge，實際是否含 UNAUTHENTICATED JSON 必須另驗，不能由此工具宣稱已完成。

共同 Host 基準已存在，但未啟動或驗收；Schema／Migration 未就緒，不以 synthetic rows 宣稱實際資料庫可用。11／07 需共同審查不可見 Case 與身分失效的 HTTP 格式，環境及角色 Gate 的任何未來調整須另核准。本批可作為保留原 Gate 的候選 Commit，不能當成醫療端正式開放／資料庫驗收通過。
