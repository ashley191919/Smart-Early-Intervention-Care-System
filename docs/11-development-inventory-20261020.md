# 早療智慧動態照護系統｜重大需求變更前盤點

**範圍：11 家長與教師端**
**盤點日期：2026-10-20**
**程式基準：`feature/case-guardian-management`／`0e4e16939b2fb711190aa490ca80098f0615ff47`**

## 閱讀及查核範圍

本文件整理既有唯讀盤點結果，作為重大需求調整前的開發基準，不決定功能刪減、架構變更或重新分工。

盤點結論：**家長端已完成登入、綁定、個案範圍與讀題的主要程式；正式問卷寫入尚未完成。教師端已有草稿、提交及撤銷核心，但仍限 Development。最新模型尚未完成真實 MySQL 整合驗收。**

- 原盤點只讀取本機程式、Git 紀錄與既有驗收文件，沒有修改程式、執行測試、啟動網站、連線資料庫或更新遠端。
- 本次僅將報告另存為 Markdown；沒有重新查核遠端或執行功能驗收。
- 下列測試結果均為既有紀錄，不能解讀為本次重新執行通過。
- 「已完成」限定在列明的程式或隔離驗證範圍，不等於正式部署完成。
- 「未證實」代表缺乏本次可採用的完成證據，不代表資料庫或其他分支一定沒有該功能。
- A 節的乾淨工作目錄及 Commit 狀態是**原盤點當時**的結果；本文件建立後會成為新的未提交文件。

---

## A. Git／版本狀態

### A.1 目前專案

| 項目 | 查核結果 |
| --- | --- |
| Repository | `ashley191919/Smart-Early-Intervention-Care-System` |
| Worktree | `C:\Users\ashle\Desktop\輔仁大學\早療專題\vs 2022\Smart-Early-Intervention-Frontend-Integration` |
| Branch | `feature/case-guardian-management` |
| HEAD | `0e4e16939b2fb711190aa490ca80098f0615ff47` |
| 最後提交 | `fix: align parent help instruction typography and list spacing` |
| 提交時間 | 2026-10-20 11:03:45，UTC+8 |
| 原盤點工作目錄 | 乾淨，沒有未提交或未追蹤檔案 |
| 尚未 Push | 相對本機保存的同名遠端追蹤分支為 0 |
| 與上游差距 | ahead 0／behind 0 |
| 與保存的 `origin/main` 差距 | 目前分支領先 15 個 Commit |

**沒有查詢即時 GitHub。**「與上游一致」僅表示本機 HEAD 與保存的遠端追蹤分支一致，不能保證遠端此刻沒有更新。

另一個共用 Git Repository 的主工作目錄：

- 路徑：`C:\Users\ashle\Desktop\輔仁大學\早療專題\vs 2022\Smart-Early-Intervention-Care-System`
- 分支：`feature/authorization`
- HEAD：`f40a788c2406d624d730bb9861b51f76ea631486`
- 未將另一個 Worktree 的工作目錄狀態當作目前分支狀態。

### A.2 本機已取得的其他版本

以下均為本機保存的遠端追蹤紀錄，並非即時遠端查詢。

| 範圍 | 分支 | 保存的 Commit |
| --- | --- | --- |
| main | `origin/main` | `a68ed6d` |
| 前端整合 | `origin/integration/frontend` | `a68ed6d` |
| 第一階段整合 | `origin/integration/phase1` | `b922a40` |
| 07 醫療端 | `origin/feature/medical-backend` | `f642ec8` |
| 16 RBAC／問卷 | `origin/feature/shared-rbac-core` | `a9de569` |
| 16 Definition | `origin/codex/questionnaire-definition-core` | `5078565` |
| 醫療／RBAC 整合 | `origin/codex/integration-medical-rbac` | `1af22c3` |
| 狀態歷程 Migration | `origin/codex/case-status-history-migration` | `7fe3bf1` |
| 雲端 Migration 前置工具 | `origin/feature/cloud-migration-preflight` | `6f9eb60` |
| 問卷第一批 | `origin/feature/questionnaire-phase2-1` | `197dbf2` |

醫療端等部分追蹤紀錄更新到 10/19；保存的 main 較舊。不能據此判定其他人沒有更新。

### A.3 已整合與仍在功能分支的內容

| 範圍 | 查核結果 |
| --- | --- |
| 較早的前端整合、身分模型、教師開發流程及問卷基礎 | 已存在目前分支的共同歷史 |
| 11 Guardian、Email、正式讀題、近期前端修正 | 相對保存的 main，仍在目前分支的 15 個提交內 |
| 16 Definition 查詢 | 逐檔引入 `5078565` 的五份來源，再加入 11 Adapter／API／前端 |
| 16 全部 RBAC、作者模型及後續寫入核心 | 未整批合入 |
| 07 最新醫療端、SessionVersion、狀態歷程 | 未整批合入 |
| 醫療／RBAC Integration Branch | 本機已取得，但不是目前分支的已合併祖先 |

有使用部分共用核心，不等於已完成三方整合。

## B. 已完成的功能

本節「已完成」指限定範圍內已實作，且有既有隔離驗證紀錄；正式使用仍受部署及整合條件限制。檔案代號見 F.5。

| 範圍 | 已有成果 | 程式依據 |
| --- | --- | --- |
| Email 寄送元件 | Gmail 加密 SMTP、認證、失敗處理；曾實際收到測試信 | P01 |
| Email OTP 核心 | 六位碼、五分鐘、60 秒重寄、五次失敗、一次性消耗與舊 challenge 失效 | P02 |
| Email 登入 API／前端 | request／verify、可信帳號、JWT、重複點擊與過期回應處理 | P03、P04、P32 |
| 家長資料範圍 | 目前帳號／院所／角色、Email 驗證及有效綁定檢查 | P05 |
| Guardian／綁定基礎 | 聯絡人、關係、邀請、指定 Email 兌換、撤銷與重複使用阻擋 | P06、P07 |
| 家長資料讀取 | 自己的孩子、基本資料、任務、本人聯絡資料 | P07、P08 |
| 家長完整讀題 | 固定 Task 版本、完整題目／選項、Guardian Adapter、JWT 保護 | P09、P10、P11 |
| 教師完成狀態最小揭露 | 家長只能讀完成度，不回答案／草稿／分數 | P07 |
| 正式畫面錯誤處理 | 未登入、401／403／404／503、舊資料清除、過期請求與歷史返回保護 | P08 |
| 共用家長介面 | 六項側欄、工作台名稱、填答返回、按鈕間距、協助文字樣式 | P12、P13 |

Email 登入開關預設關閉；不能直接歸類為目前已能正式使用。

## C. 部分完成的功能

| 功能 | 已有部分 | 尚未完成／限制 |
| --- | --- | --- |
| 家長正式登入 | Email OTP、JWT、前端、核准帳號驗證 | 預設關閉；首次帳號、院所核准、Migration／回填及真實驗收未完成 |
| 手機驗證 | Phone、PhoneVerifiedAtUtc、SMS 介面與舊 API | Runtime 使用 ParentSmsUnavailable，未接實際 SMS；Email 不等於手機驗證 |
| Guardian 綁定管理 | 新增、關聯、邀請、綁定、撤銷及範圍 | 完整事件歷史、跨帳號轉移、換登入 Email 未完成 |
| 家長問卷 | 正式任務清單與完整題目 | 正式草稿、接續、提交及本人答案查詢未完成 |
| 家長完成狀態 | 讀伺服器任務狀態；展示頁有提交紀錄 | 展示紀錄為瀏覽器資料，不是正式答案 |
| 教師授權 | Grant／GrantTask／Session、驗碼、連結、QR 圖片 | 限 Development；缺正式家長入口與同意介接 |
| 教師草稿與提交 | EF、Revision、Idempotency、Rollback、Receipt | 限 Development 固定個案；正式入口與跨模組驗收未完成 |
| 授權失效 | Session 到期、Grant 撤銷、全部提交後停止填答 | Grant 業務期限、Guardian 連動、同意撤回影響待決策 |
| 知情同意 | 說明頁、獨立畫面草稿 | 無正式 Entity、保存／查詢、版本、撤回紀錄 |
| 共用權限 | 家長實際資料範圍檢查 | 未接 16 RBAC Handler／Policy 與共用 Session 撤銷 |
| MySQL 整合 | EF Mapping、舊 Migration、部分教師開發流程 | 最新 Guardian／Email 及完整家長流程缺真實 MySQL 驗收證據 |

## D. 尚未開始的功能

以下指未找到正式實作，不表示新需求一定要保留。

1. **家長首次帳號建立及院所核准**：驗碼後仍須已有唯一、有效 PARENT，不會自動註冊。
2. **正式家長問卷寫入 API**：沒有家長草稿讀寫、提交及本人 Response 路由。
3. **家長作者欄位及作者隔離儲存**：Draft／Response 沒有 ParentAuthorUserId；教師 Grant 作者不能替代。
4. **家長 Idempotency 儲存**：尚無 ParentAuthorUserId＋IdempotencyKey 的正式去重。
5. **正式家長建立／撤銷教師 Grant API**：Dev 測試入口沒有正式家長、孩子及同意資格流程。
6. **正式同意保存及撤回**：無正式模型、本文版本保存、Grant 完整關聯。
7. **跨帳號轉移與完整綁定歷史**：重新啟用關係仍更新原資料，並非事件歷程。
8. **實際相機 QR 掃描**：只有掃描示意，沒有取像與解碼。
9. **本分支共用 SessionVersion／全域撤銷**：未接 07 實作；家長登出主要清除本分頁 Token。
10. **最新三方模型真實 MySQL 端到端驗收**：未找到完成證據。

## E. Entity／資料表／Migration

### E.1 11 新增或修改的模型

| Entity／資料表 | 內容 | 關聯及限制 |
| --- | --- | --- |
| GuardianContact／guardians | GuardianId、OrganizationId、ParentUserId、DisplayName、Email、Phone、Revision、CreatedAtUtc、UpdatedAtUtc | 關聯 Organization／User；同院所 Phone 唯一；Revision 並發控制 |
| CaseGuardian／case_guardians | CaseId、GuardianId、Relationship、IsPrimaryContact、LinkStatus、CreatedAtUtc、BoundAtUtc、ActiveCaseId、RevokedAtUtc | Case＋Guardian 複合主鍵；computed ActiveCaseId 唯一索引限制最多一位有效主要聯絡人 |
| ParentBindingInvitation／parent_binding_invitations | InvitationId、CaseId、GuardianId、CodeHash、TargetPhone、TargetEmailNormalized、IssuedByUserId、CreatedAtUtc、ExpiresAtUtc、ConsumedAtUtc、ConsumedByUserId、RevokedAtUtc | 關聯 CaseGuardian 與 User；只保存邀請碼雜湊 |
| CaseRecord／cases | 新增 OrganizationId、NationalId、Address、Revision | OrganizationId 允許 null；舊資料院所待回填 |
| User／users | Phone、PhoneVerifiedAtUtc、Email、NormalizedEmail、EmailVerifiedAtUtc | 共用既有 Role／Organization；本分支無 SessionVersion |

依據：P14～P18。

注意事項：

- GuardianContact.ParentUserId 連接聯絡人與登入帳號。
- CaseGuardian 決定照顧者與孩子目前是否有有效關係。
- TargetEmailNormalized 為 null 的舊手機邀請不會自動轉成可信 Email 邀請。
- NormalizedEmail 目前非唯一索引；登入遇重複帳號會拒絕。
- InviteAsync 目前建立 ExpiresAtUtc=null，沒有自動到期；模型與檢查仍支援期限。
- Phone 仍是 Guardian 必填及同院所去重欄位，沒有因 Email 登入而移除。
- 已綁定聯絡人的 Email 修改會被阻擋，要求重新核准／驗證；尚無完整變更流程。
- 關係重新啟用更新同一筆 CaseGuardian，不是完整歷史事件保存。

### E.2 既有教師及問卷模型

| Entity | 現況 |
| --- | --- |
| TeacherGrant | CodeHash、IsDevelopment、授權狀態及撤銷；ConsentReferenceId 是預留欄位，非正式 Consent 外鍵；無 Grant 到期欄位 |
| TeacherGrantTask | Grant 與指定 Task 關聯 |
| TeacherSession | Session 雜湊、期限、撤銷、ReceiptExpiresAtUtc |
| QuestionnaireDraft | Task、Version、答案、Revision、LastSavedByGrantId；無家長作者欄位 |
| QuestionnaireResponse | Task、Version、答案、SubmittedByGrantId、IdempotencyKey、PayloadHash；無可信家長作者 userId |
| FormAccessToken | Legacy／NotMapped，不是目前持久化教師授權模型 |

### E.3 DbContext／Configuration／Migration

ApplicationDbContext 已註冊 Guardian、CaseGuardian、Invitation，以及既有問卷、教師、User／Role／Organization 等 DbSet。Configuration 已描述新增欄位、關聯、索引及並發欄位。

目前仍只有四份原有 Migration：

1. `20261005152359_InitialSharedCore`
2. `20261005180101_AddAuditLogs`
3. `20261005184434_AddSubmissionReceiptWindow`
4. `20261006074424_IntegrateIdentityAndAuthorization`

相對保存的 main，Migration／ModelSnapshot 沒有新增差異。Runtime Model 已包含新 Guardian／Email，但現有 Migration／Snapshot 尚未涵蓋這批模型。

**不能只套用現有四份 Migration 就認為最新家長 Schema 已完成。**本次未查詢資料庫，無法判定各台 MySQL 的實際 Schema 或 Applied Migration。

## F. API／前端／測試狀態

### F.1 家長、Guardian 與 Case API

以下已有程式，不代表部署環境目前可直接使用。

| API | 現況 |
| --- | --- |
| POST `/api/parent/auth/email/request` | 取碼；開關預設關閉 |
| POST `/api/parent/auth/email/verify` | 驗碼、既有 PARENT 核對、JWT |
| POST `/api/parent/auth/otp/request`、`/verify` | 舊 SMS；目前服務拒絕提供實際驗證 |
| GET `/api/parent/cases` | 有效綁定孩子 |
| POST `/api/parent/bindings` | 指定 Email 邀請兌換 |
| GET `/api/parent/profile` | 本人聯絡資料 |
| PUT `/api/parent/profile/{guardianId}` | 修改姓名與 Revision |
| GET `/api/parent/cases/{caseId}/tasks` | 正式家長任務清單 |
| GET `/api/parent/tasks/{taskId}/questionnaire` | 完整題目，固定 Task 版本 |
| GET `/api/parent/cases/{caseId}/teacher-completion` | 教師完成狀態，不含答案 |
| GET／POST `/api/cases/{caseId}/guardians` | 醫療查詢／新增聯絡人 |
| PUT `/api/cases/{caseId}/guardians/{guardianId}` | 修改聯絡人及關係 |
| POST `/api/cases/{caseId}/guardians/{guardianId}/link` | 關聯既有聯絡人 |
| POST `/api/cases/{caseId}/guardians/{guardianId}/invitations` | 建立邀請 |
| POST `/api/cases/{caseId}/guardians/{guardianId}/revoke` | 撤銷關係 |
| POST `/api/parent-binding-invitations/{invitationId}/revoke` | 撤銷未消耗邀請 |
| GET `/api/guardians` | 同院所聯絡人 |
| GET／POST `/api/cases` | 既有個案列表／新增 |
| GET／PUT `/api/cases/{caseId}` | 既有個案詳細／修改 |

沒有 DELETE Case 路由，也沒有正式家長 Draft／Submit／本人 Response 路由。

Case 程式仍保留在 11 分支的 CasesController／CaseGuardianService；後續維護已規劃交接 07，但不代表已搬檔、合併或可自行刪除。

### F.2 教師 Development API

前綴：`/api/dev/teacher-workspace`。依據 P20～P23。

| 方法／子路由 | 功能 |
| --- | --- |
| POST `grants` | 建立測試授權及邀請 |
| POST `verify` | 驗碼、Cookie Session |
| GET `task` | 指定任務 |
| GET `tasks/{taskId}/draft` | 讀草稿 |
| PUT `tasks/{taskId}/draft` | 保存草稿 |
| POST `tasks/{taskId}/submit` | 提交，要求 Idempotency-Key |
| GET `tasks/{taskId}/receipt` | 回執，不含完整答案 |
| POST `grants/{grantId}/revoke` | 撤銷測試授權 |
| POST `logout` | 撤銷 Session、清除 Cookie |

- 非 Development 阻擋；使用 EarlyCare.DevTeacher Cookie。
- 草稿／提交有同源與自訂 Header 檢查。
- 建立／撤銷入口不是正式家長授權 API。
- 固定 CoreDevelopmentSeed 個案與指定版本。
- 另留有較早的 `/api/dev/teacher-grants` 與 `/teacher/test-form` 開發流程。
- Session 為八小時；全部指定任務完成後 Grant=USED，停止新填答，回執最多再讀十五分鐘且不超過 Session 原期限。

### F.3 前端及資料來源

| 頁面／模式 | 資料來源 | 可操作範圍 |
| --- | --- | --- |
| `/?role=parent` | 正式 Email API | Schema／帳號／開關就緒才可真實登入 |
| `parent-workspace.html?mode=account` | JWT＋正式家長 API | 孩子、任務、姓名、教師完成度、唯讀題目 |
| 家長展示工作台與 `parent-questionnaire.html?mode=demo` | SharedDemo／localStorage | 展示草稿、提交及紀錄，非正式 DB 答案 |
| `teacher-workspace.html?mode=authorized` | Development Cookie＋EF | 固定開發資料 |
| 教師 Demo | 瀏覽器展示資料 | 不代表正式授權 |
| 同意說明／Prototype | 靜態說明／草稿 | 不保存正式同意 |

家長展示問卷已修正同孩子／同輪次完成數量、提交後返回及六項側欄；這不代表正式家長寫入已完成。

QR 產生與連結已實作於 Development；index.html 的掃描框仍是示意，沒有相機取像與解碼。

### F.4 DTO、錯誤與測試

已有 CaseContracts、QuestionnaireDefinitionContracts、QuestionnaireWorkflowContracts，見 P25～P27。

| 錯誤來源 | 格式 |
| --- | --- |
| Case／Guardian | 通常 code、message |
| Definition／教師 Workflow | 通常 code、message、traceId |
| 模型驗證 | 可能為 ValidationProblem |
| JWT／限流 | 可能只有狀態，沒有 JSON |

前端支援非 JSON 錯誤，但三端錯誤格式尚未統一。

**以下是既有紀錄，本次沒有重跑：**

| 測試／證據 | 結果 | 範圍與限制 |
| --- | --- | --- |
| ParentEmailHarness | 28 寄信元件＋32 OTP | 離線；非真實登入端到端 |
| 使用者 SMTP 測試 | TLS、認證通過，已收到測試信 | 只證明寄信，不證明帳號／綁定／DB |
| CaseGuardianHarness | 共 209，內含 71 Definition 整合 | SQLite、可信 JWT、Guardian／Email／讀題；非 MySQL |
| Definition Harness | 36 | Fake Store／Access |
| Phase 2-1 | 指派、列表、完成度案例通過 | 既有離線紀錄 |
| Phase 2-2 | handoff 模式 79 | 不代表 Migration 缺口解決 |
| 前端 Node | 最近紀錄 41 | 請求競態、草稿 client、展示任務範圍等 |
| 隔離瀏覽器 | 桌面／手機、錯誤、登出、讀題、返回 | SQLite fixture 或獨立展示 origin，非共用 DB |

未找到最新 Guardian／Email／Definition 已完成真實 MySQL 8.4.11 全流程的證據。先前雲端連線／唯讀檢查回報不能替代。

### F.5 程式依據索引

以下路徑相對於 A.1 所列的完整 Worktree 根目錄；代號供功能總表追溯。後端均位於 `EarlyInterventionCare.Api`。

| 代號 | 實際程式檔案／目錄 |
| --- | --- |
| P01 | `EarlyInterventionCare.Api/Services/Email/ParentEmailSender.cs` |
| P02 | `EarlyInterventionCare.Api/Services/Email/ParentEmailVerification.cs` |
| P03 | `EarlyInterventionCare.Api/Controllers/ParentEmailAuthenticationController.cs` |
| P04 | `EarlyInterventionCare.Api/Services/Authentication/ParentEmailAuthenticationService.cs` |
| P05 | `EarlyInterventionCare.Api/Services/Cases/CaseAccessService.cs` |
| P06 | `EarlyInterventionCare.Api/Services/Cases/CaseGuardianService.cs` |
| P07 | `EarlyInterventionCare.Api/Controllers/CasesController.cs` |
| P08 | `EarlyInterventionCare.Api/wwwroot/js/parent-account-workspace.js` |
| P09 | `EarlyInterventionCare.Api/Controllers/ParentQuestionnaireDefinitionsController.cs` |
| P10 | `EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireDefinitionService.cs` |
| P11 | `EarlyInterventionCare.Api/Services/Questionnaires/GuardianQuestionnaireDefinitionAccess.cs` |
| P12 | `EarlyInterventionCare.Api/wwwroot/css/parent-shell.css` |
| P13 | `EarlyInterventionCare.Api/wwwroot/js/parent-workspace.js` |
| P14 | `EarlyInterventionCare.Api/Data/Entities/GuardianContact.cs`，包含三個 Guardian／Binding Entity |
| P15 | `EarlyInterventionCare.Api/Data/Entities/CaseRecord.cs` |
| P16 | `EarlyInterventionCare.Api/Models/Authentication/User.cs` |
| P17 | `EarlyInterventionCare.Api/Data/GuardianModelConfiguration.cs` |
| P18 | `EarlyInterventionCare.Api/Data/IdentityModelConfiguration.cs` |
| P19 | `EarlyInterventionCare.Api/Data/ApplicationDbContext.cs`、`Data/Migrations/` |
| P20 | `EarlyInterventionCare.Api/Controllers/DevelopmentTeacherWorkspaceController.cs` |
| P21 | `EarlyInterventionCare.Api/Development/TeacherWorkspaceService.cs` |
| P22 | `EarlyInterventionCare.Api/Services/Questionnaires/TeacherQuestionnaireWorkflowService.cs` |
| P23 | `EarlyInterventionCare.Api/Services/Questionnaires/EfTeacherQuestionnaireStore.cs` |
| P24 | `EarlyInterventionCare.Api/Development/TeacherInvitation.cs` |
| P25 | `EarlyInterventionCare.Api/DTOs/Cases/CaseContracts.cs` |
| P26 | `EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireDefinitionContracts.cs` |
| P27 | `EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireWorkflowContracts.cs` |
| P28 | `EarlyInterventionCare.Api/Data/Entities/QuestionnaireDraft.cs` |
| P29 | `EarlyInterventionCare.Api/Data/Entities/QuestionnaireResponse.cs` |
| P30 | `EarlyInterventionCare.Api/Program.cs` |
| P31 | `EarlyInterventionCare.Api/wwwroot/js/teacher-workspace.js`、`teacher-access.js`、`teacher-draft.js` |
| P32 | `EarlyInterventionCare.Api/wwwroot/js/parent-otp-client.js`、`parent-session.js`、`parent-account-client.js` |
| P33 | `docs/prototypes/parent-consent-review.html` |
| P34 | `EarlyInterventionCare.Api/wwwroot/index.html` |
| P35 | `EarlyInterventionCare.Api/Controllers/ParentAuthenticationController.cs`、`Services/Authentication/IParentSmsVerification.cs`、`ParentOtpAuthenticationService.cs` |

既有驗收及交接依據：

- `docs/parent-definition-api-integration.md`
- `docs/parent-email-api-acceptance.md`
- `docs/parent-email-model-handoff.md`
- `docs/parent-frontend-submit-flow-review.md`
- `docs/11-parent-independent-review-20261019.md`
- `docs/11-parent-browser-qa-20261018.md`
- `docs/11-case-transfer-and-teacher-consent.md`
- `docs/11-integration-readiness-20261018.md`
- `tests/CaseGuardianHarness.cs`、`tests/ParentEmailHarness.cs`
- `tests/QuestionnaireDefinition/Program.cs`
- `tests/QuestionnairePhase21Harness.cs`、`tests/QuestionnairePhase22Harness.cs`
- `tests/TeacherAccessHarness.cs` 及相關前端 `.test.cjs`

## G. 與 07、16 的依賴及重疊

| 範圍 | 現況 | 依賴／重疊 |
| --- | --- | --- |
| Case 基本資料 | 11 分支仍有既有實作 | 與 07 接手範圍重疊；與 Guardian 同檔，不能整檔覆蓋 |
| Case Entity | 11 新增院所、地址、身分證、Revision | 保存的 07 模型較早，需保留雙方變更 |
| CaseStatus／History | 已取得 07 相關分支 | 未進目前分支，不列 11 已完成 |
| Definition | 已使用 16 固定版本核心 | 11 Adapter／Controller／DI／前端完成 |
| Task List | 沿用既有共用 Store | 沒另建重複 Controller |
| 教師 Draft／Submit | 已用共用 Workflow／EF Store | Development，非最新所有 16 成果已合入 |
| 家長 Draft／Submit／Response | 未完成 | 依賴作者模型、Service、交易契約 |
| RBAC | 已有 Role／Permission 模型 | 未接 PermissionService／Handler／Policy |
| JWT／Cookie／Session | 家長 Bearer、教師 Dev Cookie | 與 07／16 共用撤銷及安全規格待統一 |
| Schema／Migration | Runtime 擴充、Migration 未跟上 | 三方模型確認後由 16 統整 |

存在 Permissions 表不等於套用 RBAC；使用部分共用服務不等於所有問卷功能已串接；約定交接不等於程式已拆分或合併。

## H. 可重用的現有程式

以下只盤點資產，不決定未來保留功能。

| 資產 | 可重用內容 |
| --- | --- |
| Email／OTP | 發送介面、期限、重寄、一次性消耗 |
| 可信帳號判斷 | DB 帳號／院所／角色，拒絕前端偽造身分 |
| CaseAccessService | 家長個案及任務範圍 |
| Guardian 基礎 | 聯絡人、關係、邀請、撤銷 |
| Definition／Adapter | 固定版本、公開投影、授權委派 |
| Validator | 題型、答案與 Definition 結構檢查 |
| 教師 Workflow／Store | Revision、去重、Receipt、Rollback |
| 邀請呈現 | 授權碼、fragment 連結、QR 圖片 |
| 前端請求元件 | JWT、錯誤解析、過期回應阻擋 |
| 共用樣式 | 側欄、醫院識別、對話框、手機布局 |
| 測試資產 | SQLite fixture、Fake Store／Sender、越權／撤銷及前端測試 |
| 文件 | API 契約、責任邊界、驗收表、已知限制 |

## I. 重大調整的技術風險

### I.1 Runtime Model 與 Migration 不一致

新 User、Guardian、Case 查詢可能依賴未建立欄位，其他共用 User 的登入也會受影響。不能只開啟家長登入開關而忽略 Schema。

### I.2 Case 與 Guardian 共用檔案

Controller／Service 同時包含 Case CRUD、Guardian、Binding、Profile。直接覆蓋醫療端版本可能遺失家長功能。

### I.3 資料依賴連鎖

家長：`User／Role／Organization → 已驗證 Email → GuardianContact → CaseGuardian → Case → Task`。

教師：`Case／Task → TeacherGrant／GrantTask → TeacherSession → Draft／Response`。

Consent 尚未正式接上。

| 刪改對象 | 可能影響 |
| --- | --- |
| User／Email | 登入、可信帳號、邀請核對、家長範圍 |
| GuardianContact | 聯絡資料、ParentUserId、邀請及跨孩子關係 |
| CaseGuardian | 孩子清單、任務、讀題與撤銷 |
| Case | Guardian、Task、Grant、未來 Workflow |
| Grant／Session | 驗碼、教師讀寫、作者、去重、Receipt |
| Consent | 未來授權建立條件與撤回；目前無正式保存資料可直接沿用 |

多數外鍵為 Restrict，不能假設刪除上層就能自動安全清理下層。

### I.4 撤銷與提交的共同交易未完成

- Guardian 撤銷會阻止後續家長 Scope 檢查通過。
- Definition 回傳前有第二次檢查，仍非原子鎖定；最後檢查至送出有時間差，已回傳內容無法收回。
- 教師 EF Store 已鎖 Grant，再依 TaskId 順序鎖任務。
- Guardian 撤銷尚未與未來家長 Submit 共用 Case 協調鎖。
- Guardian 撤銷不會自動撤銷教師 Grant。

不能宣稱所有撤銷並發已解決。

### I.5 家長作者與去重未落地

Draft 以 Task 為核心，Response 作者為 Grant；直接開放家長使用會缺可信作者隔離。

### I.6 同院所不等於最終醫療授權

家長及綁定實際檢查 User／Guardian／Case 院所一致性；但 CaseAccessService 目前對 ADMIN／CASE_MANAGER／MEDICAL_STAFF 允許同院所個案，未對齊 07 個案指派及 16 RBAC。教師固定 Dev 個案也不能證明一般化院所範圍完成。

### I.7 展示成果與正式成果混淆

localStorage 提交不是 MySQL 答案，固定 Development 個案不是正式授權。去掉畫面示範文字不會完成後端整合。

### I.8 OTP 與 Session 限制

OTP 為單程序記憶體、重啟失效，未支援多程序；家長登出是本地 Token 清除，未完成全域撤銷。

### I.9 綁定歷史不完整

重啟關係更新原紀錄並重設部分時間，不保留每次綁定及撤銷事件。

### I.10 文字與實作仍有差異

教師 Service Notice 仍說草稿刷新清除，但已有 EF 保存；一般問卷 Controller 拒絕文字仍提固定 Dev 個案，實際已使用同院所檢查。本次只記錄，未修改。

## J. 待確認事項

收到老師新要求前，以下維持待確認：

1. 家長／教師／照顧者的最終角色與範圍。
2. Email 登入、帳號核准、首次註冊是否維持或調整。
3. Guardian／Case 關係及多照顧者規則。
4. 新照顧者碰到舊草稿的處理。
5. Guardian 撤銷是否連動 Grant。
6. 教師是否維持授權碼／連結及正式期限。
7. 同意本文、版本、保存、撤回及授權關係。
8. 問卷提交後的 Case 狀態規則。
9. 家長作者、Idempotency、共同交易與鎖定順序。
10. 07／11／16 新責任界線及固定整合版本。
11. 模型、回填、Migration 與部署安排。
12. 真實 MySQL、Email、跨角色及重啟驗收標準。
13. 新需求下保留或調整哪些舊畫面、開發入口與模型。

本文件不代為定案，也不將既有交接安排當作需求變更後的新分工。

## 11 功能總表

程式代號見 F.5；路由見 F.1／F.2。「操作」是已有能力及歷史驗證，本次未操作；MySQL 指最新正式整合驗收。

| 功能 | 狀態 | 程式依據／入口 | 操作與既有驗證 | 真實 MySQL | 依賴／未完成項目 |
| --- | --- | --- | --- | --- | --- |
| Email 寄信 | 已完成：元件 | P01 | 曾實際收信、離線測試 | 不適用 | 帳號／額度／環境 |
| Email OTP | 已完成：核心 | P02 | 期限、重寄、錯碼、重播隔離測試 | 不需 DB；整體未驗收 | 共享儲存、多程序 |
| 家長正式登入 | 部分完成 | P03、P04、P32；登入頁 | API／前端隔離通過，預設關閉 | 未證實 | 核准、Migration、回填、啟用 |
| 首次家長帳號 | 未開始：正式流程 | P04 只查既有帳號 | 無正式建立入口 | 未驗收 | 核准來源、院所 |
| 手機驗證 | 部分完成 | P35、P30；舊 OTP API | 介面存在，實際服務不可用 | 未驗收 | SMS 未接；需求待決策 |
| GuardianContact | 部分完成 | P14、P06 | 新增、修改、共用、隔離測試 | 未證實 | Schema、歷史 |
| CaseGuardian | 部分完成 | P14、P17、P06 | 有效關係、主要聯絡人、撤銷 | 未證實 | 重綁、轉移、歷史 |
| 綁定邀請 | 已完成：基礎程式 | P06、P07；invitations | 指定 Email、雜湊、一次性兌換 | 未證實 | 真實 DB、期限、舊邀請 |
| 家長個案綁定 | 部分完成 | P06、P07；bindings | 錯信箱、重播、越權測試 | 未證實 | 轉移、並發、部署驗收 |
| 家長個案範圍 | 已完成：目前讀取 | P05 | 帳號、院所、Email、綁定測試 | 未證實 | RBAC、寫入交易 |
| 家長首頁 | 部分完成 | P08；account 模式 | SQLite 瀏覽器驗收 | 未證實 | 正式帳號、DB |
| 孩子清單／基本資料 | 部分完成 | P07、P08；parent/cases | 多孩子、切換、空資料、越權 | 未證實 | Schema、回填 |
| 本人聯絡資料 | 部分完成 | P06、P07、P25；profile | 姓名、Revision、錯誤焦點 | 未證實 | 換 Email、轉移 |
| 家長任務清單 | 已完成：讀取 | P07、P08；tasks | 範圍、狀態統計隔離測試 | 未證實 | 共用任務、真實驗收 |
| 完整題目 | 已完成：讀取整合 | P09～P11、P26 | 真 JWT＋SQLite、前端 | 未證實 | 16 核心、MySQL、撤銷時序 |
| 家長草稿保存 | 未開始：正式；展示已有 | P13；demo | localStorage 展示可用 | 否 | 16 寫入、作者模型 |
| 家長繼續填寫 | 未開始：正式；展示已有 | P13、P31；parent-questionnaire demo | 展示接續驗收 | 否 | Draft API、Revision |
| 家長正式提交 | 未開始 | 無正式 Parent Submit 路由 | 展示提交不算正式 | 否 | 作者、去重、共同交易 |
| 本人答案查詢 | 未開始 | 無正式 Own Response 路由 | 僅展示紀錄 | 否 | 作者與 Guardian |
| 家長完成狀態 | 部分完成 | P07、P08；Task status | 清單可讀，展示計數已修 | 未證實 | 正式 Submit、Workflow |
| 教師完成度供家長查詢 | 已完成：讀取 | P07；teacher-completion | 不洩露答案隔離測試 | 未證實 | 共用任務、真實資料 |
| 教師授權碼 | 部分完成 | P20、P21、P31 | Dev 驗碼、失效 | 最新整合未證實 | 正式家長入口、同意、院所 |
| 專屬連結／QR 圖片 | 部分完成 | P24、P31 | Dev 可產生，限本機 Origin | 最新整合未證實 | 正式網址與 Grant |
| 相機 QR 掃描 | 未開始：實際掃描 | P34 掃描示意 | 無取像／解碼 | 不適用 | 需求待確認 |
| Teacher Grant／Session | 部分完成 | P20、P21 | 固定個案、八小時 Session | 最新整合未證實 | 正式入口、共用 Session |
| 教師問卷讀取 | 部分完成 | P20、P21；Dev task | 固定開發版本 | 最新整合未證實 | 一般化任務、正式授權 |
| 教師草稿 | 部分完成 | P22、P23、P27、P31 | Revision、衝突、Rollback | 最新整合未證實 | 正式授權、MySQL |
| 教師提交／回執 | 部分完成 | P22、P23；submit／receipt | Grant＋Key 去重、全部完成失效 | 最新整合未證實 | 正式 API、跨模組交易 |
| 授權撤銷／重用限制 | 部分完成 | P21、P22 | Session 撤銷、USED、Receipt 窗 | 最新整合未證實 | Guardian／Consent 連動待決策 |
| 知情同意書 | 部分完成：畫面草稿 | P33、P08 | 可讀，不保存正式同意 | 否 | 核定本文、版本 |
| 同意紀錄／撤回 | 未開始：正式保存 | 無正式 Entity／API | 無正式操作 | 否 | 規則、Schema、Grant |
| 家長作者／Idempotency | 未開始：正式儲存 | P28、P29 缺家長作者 | Grant 欄位不能替代 | 否 | 16 模型及交易 |
| 共用 RBAC | 未開始：本分支接入 | P30 未註冊共用 Handler／Policy | 現為角色及資料範圍檢查 | 未驗收 | 16 核心、三方整合 |
| 同院所檢查 | 部分完成 | P05、P06 | 家長及綁定有實際檢查 | 未證實 | 醫療 Case scope、教師一般化 |
| Case 新增／查詢／修改 | 部分完成：交接待整合 | P06、P07、P15 | 程式及隔離測試；無 Delete | 未證實 | 07 接手、共用檔案 |
| 全域 Session 撤銷 | 未開始：本分支整合 | P16 無 SessionVersion | 家長本地登出；教師 Dev Session 可撤銷 | 未驗收 | 07／16 共用規格 |
| 最新模型雲端部署／驗收 | 部分完成：基礎環境；正式整合未完成 | P19、驗收文件 | 無最新完整驗收證據 | 未證實 | 三方模型、16 Migration、部署 |

---

**結束狀態：完成現況盤點及文件整理，等待新需求與後續指示。未修改程式、Schema 或分工，未執行 Migration、資料庫操作或 Git 提交／推送。**
