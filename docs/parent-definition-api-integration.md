# 家長問卷讀題 API 第一批整合

日期：2026-10-19。家長分支基準 `feature/case-guardian-management`／`121fedf`。

## 來源與修改範圍

共用來源固定為 16 的 `codex/questionnaire-definition-core`／`50785650648ef9145caddb9c8e044819bb0e2a0a`。

從該版本逐檔引入以下五份檔案，內容保持一致，沒有 Merge 整個分支：

- `EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireDefinitionContracts.cs`
- `EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireDefinitionService.cs`
- `EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireAnswerValidator.cs`
- `tests/QuestionnaireDefinition/Program.cs`
- `tests/QuestionnaireDefinition/QuestionnaireDefinitionHarness.csproj`

Validator 是必要依賴：舊版不會完整拒絕不支援題型、重複題號／選項及非法 required；直接只引入 Definition 檔案會缺少這些檢查。保留 16 的必答語意：目前每題提交時仍必填，DTO 分別提供 SnapshotRequired 與 RequiredOnSubmit，不能只依 snapshot.required=false 略過提交驗證。

本次新增 Guardian Adapter、正式讀取 Controller、共用 DI 註冊與整合測試。沒有修改 Entity、DbContext、Migration、ModelSnapshot、資料庫或前端畫面；沒有引入其他 RBAC、醫療端或寫入核心。

## API 契約

保留 `GET /api/parent/cases/{caseId}/tasks` 與原本 `CaseTaskResponse[]`。

新增：`GET /api/parent/tasks/{taskId}/questionnaire`

- 必須使用 Bearer JWT。Controller 使用已驗證的 HttpContext.User，不接收 userId、院所、版本或授權結果參數。
- `GuardianQuestionnaireDefinitionAccess` 委派現有 `CaseAccessService.CanReadParentTaskAsync`，重查 ACTIVE 帳號／院所、DB 的 PARENT 角色、可信已驗證 Email，以及同院所 ACTIVE 且已綁定的 Guardian。
- 問卷核心先查 Scope，再讀 Task 指派的固定版本；不升級最新版。允許既有任務的 PUBLISHED／RETIRED 版本，拒絕取消任務與教師任務。
- 原封使用 `QuestionnaireDefinitionResponse`：taskId、questionnaireId、questionnaireVersionId、title、versionNumber、instructions、questions。題目與選項只投影公開欄位，不包含原始答案、草稿、作者、計分、來源內部資訊。
- 成功回傳 200、Cache-Control: no-store。無需 CSRF 寫入介接：本次只有 Bearer GET，不引入 Cookie 認證或寫入操作。
- `ParentQuestionnaireDefinitionRegistration.AddParentQuestionnaireDefinitions()` 註冊 Scoped 的核心、EF Store、Guardian Adapter 及 Singleton Validator；正式網站及整合測試共用此註冊，非僅 Development 可用。

### 錯誤

| 狀況 | HTTP／code | 格式 |
| --- | --- | --- |
| 缺少／無效 JWT | 401 | 沿用現有 JWT middleware，可能空本文，前端不可假設一定是 JSON |
| JWT 有效但帳號／院所已停用、userId 不合法 | 401／UNAUTHENTICATED | code、message、traceId；no-store |
| 未綁定、綁定已撤銷、未驗證 Email、教師任務、錯誤角色、未知／取消任務 | 404／TASK_NOT_FOUND | 統一不洩漏任務存在與否；code、message、traceId；no-store |
| 版本或 Definition 結構不合法 | 503／DEFINITION_UNAVAILABLE | 不回傳 snapshot 或內部例外；code、message、traceId；no-store |
| DB／逾時失敗 | 503／SERVICE_UNAVAILABLE | 安全文字；code、message、traceId；no-store |

不變更其他 Controller 的既有錯誤格式，也不宣稱三端 JWT／Cookie／CSRF 已統一。

## 撤銷時序

Controller 在共用核心讀完 Definition 後、回傳 DTO 前，再執行一次現有 Scope 檢查。測試模擬首次授權成功後、Store 已讀取但回傳前提交撤銷，結果為 404。

這是額外的撤銷檢查，不是原子交易或共同鎖定協定。最後一次檢查之後至網路送出仍存在時間差；已回傳的內容也無法收回。正式撤銷與讀取的 MySQL 隔離／鎖定要求仍需共同確認，不能把此測試視為所有並發順序已解決。Draft／Submit 的 Case 協調鎖、交易擁有者及撤銷競態仍由後續寫入整合處理。

## 實際驗證結果

所有測試於隔離輸出目錄執行，沒有真實 SMTP 或 MySQL 連線。

| 工具 | 結果與範圍 |
| --- | --- |
| QuestionnaireDefinitionHarness | 16 原始 36 項通過；Fake Store／Access 核心規則 |
| CaseGuardianHarness | 共 209 項通過，包含新增 71 項讀題整合；保留原本 138 項家長／Guardian／Email 檢查 |
| QuestionnairePhase21Harness | 指派、列表、完成度案例通過 |
| QuestionnairePhase22Harness --case-guardian-schema-handoff | 79 項通過；顯式交接模式，不代表 pending Migration 已解決 |

71 項整合檢查沿用實際 Email OTP API 發出的 JWT（測試 sender），經真正 JWT 簽章／issuer／audience／有效期驗證、SQLite EF Store、正式 Adapter 與 HTTP Controller；涵蓋原版本讀題、欄位隱私、越權、停用、未驗證 Email、取消、錯誤定義、重查撤銷及不產生草稿／答案。並非僅 Fake Access 測試。

執行方式（在 repo 根目錄）：

```powershell
dotnet run --project tests/CaseGuardianHarness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-definition-integration/
dotnet run --project tests/QuestionnaireDefinition/QuestionnaireDefinitionHarness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-definition-core/
dotnet run --project tests/QuestionnairePhase21Harness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-definition-phase21/
dotnet run --project tests/QuestionnairePhase22Harness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-definition-regression/ -- --case-guardian-schema-handoff
```

## 下一批交接

11 已完成這批 Adapter／正式讀題 API／DI／隔離整合驗證。請 16 核對來源版本與路由，接續提供 Parent Draft／Submit／本人 Response 的共用核心、作者資料模型及交易契約，避免重做儲存流程。

仍待完成：作者隔離與共用寫入服務、正式前端填答／暫存／提交介接、MySQL 8.4.11 整合驗收、撤銷並發協定、三端 Session／錯誤格式統一。Schema／Migration 仍由 16 統整；ParentEmailLogin:Enabled 保持預設 false，未啟用正式寄碼，不代表現在能用真實帳號跑完整問卷。

## 前端讀題後續完成項目（2026-10-19）

- Account 家長首頁與「我的問卷」新增「查看題目」，直接呼叫正式 Definition API；沿用現有對話框、醫院識別、字體與配色。
- 顯示 Task 固定版本的標題、說明、全部題目、選項及後端 RequiredOnSubmit 規則。僅閱讀，沒有輸入、暫存或送出操作；不另用既有示範問卷替代正式資料。
- 載入先清除上一份內容；成功後焦點移至問卷標題；失敗提供手動重新載入與返回。401 清除登入，403／404 清除孩子與題目資料；503 不自動重試。
- 關閉、離開／歷史還原及登入變更會捨棄過期請求，避免較晚的結果重新顯示舊題目。所有 API 文字以 textContent 或 HTML escaping 顯示，不插入原始 HTML。
- Node 前端測試共 39 項通過（新增 5 項讀題 client 測試）；CaseGuardianHarness 209 項再驗通過。
- 瀏覽器隔離驗收：1440×900、390×844；12 題／48 個選項完整保留，對話框內垂直捲動，文件與對話框均無橫向溢出。驗證重新載入、返回、Escape、慢速載入後關閉、503→重試成功、404 清除資料及 401 過期處理。
- 驗收使用測試執行檔中的 SQLite、合成帳號與 JWT，未寄真實郵件、未連接本機／雲端 MySQL。測試旗標：`--serve-email-browser-fixture --definition-browser-data`；fixture 登入與情境路由只存在測試專案，不註冊到正式網站。

雲端狀態：依團隊回報，VM MySQL／SSH Tunnel 與唯讀工具已可連線，之前為 0 applied／4 pending Migration。尚無這批 Email／Guardian／Definition 模型已部署及網站已接雲端的驗收結果；不能僅依 localhost 頁面判定資料來源。需 16 統整 Migration 後，再明確確認網站使用的連線目標、執行唯讀辨識與完整跨角色流程。這次不變更 ConnectionString、不自行套用 Migration。
