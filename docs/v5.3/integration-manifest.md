# V5.3 07-07 Integration Manifest

盤點日期：2026-10-25（Asia/Taipei）。本文件只記錄本機 Git、已取得分支原始碼及歷史驗收紀錄；不是 Merge、部署或資料庫變更指令。固定物件優先於可移動 branch 名稱。

## 1. 狀態字彙與查證界線

- **已確認**：本機物件／檔案／契約或歷史驗收有可核對依據；不自動代表正式整合成功。
- **待確認**：需由主責人或三人核准的版本、分工細節、授權／完成度政策。
- **阻塞**：目前缺少開始對應正式驗收的必要版本、契約或環境。
- **待驗收**：已有程式或計畫，但指定正式 Host／真服務／MySQL 組合未被本次驗證。

本次未 Fetch／Pull／Push／Merge／Rebase／Reset／Cherry-pick，未查詢 GitHub 即時狀態。`origin/*` 全部是本機 remote-tracking refs，commit 日期不是最後 Fetch 時間，也不是組員目前工作目錄狀態。`origin/HEAD -> origin/main`，不是另一個整合分支。本機 origin/main 最近 reflog 記錄為 2026-10-12 00:18:23 +0800 的 fetch fast-forward；不代表其他 refs 的更新時間。

本次執行前工作目錄沒有已追蹤檔案差異、暫存區為空，但有六個既有未追蹤成果，並非完全乾淨。完成本次後只新增本 Manifest；這六檔不得順手納入整合：

```text
EarlyInterventionCare.Api/Services/Workflow/CaseStatusWorkflowService.cs
EarlyInterventionCare.Api/Services/Workflow/ICaseStatusWorkflowStore.cs
docs/database/init-0-to-04.generated.sql
git
tests/CaseStatusWorkflowServiceHarness.cs
tests/CaseStatusWorkflowServiceHarness.csproj
```

## 2. 三人版本鎖定表

|主責／用途|Branch／本機可讀來源|固定 Commit|狀態與界線|
|---|---|---|---|
|07 最新成果|本機 `feature/medical-backend`|`e589783281838fcc8e59753e41f97db3efd5446f`|已確認；目前 HEAD，可作候選整合基準，是否採用仍待三人確認|
|07 本機追蹤快照|`origin/feature/medical-backend`|`f642ec813515cc96f7c93e76e0b4991ad26036a3`|已確認；相對此本機快照，07 HEAD 多 13 個 Commit／對方獨有 0；不可據此斷言即時遠端尚未 Push|
|11 使用者指定 Case 固定版|使用者提供 `feature/case-guardian-management`|`0e4e16939b2fb711190aa490ca80098f0615ff47`|**阻塞**：`git cat-file -t` 查無物件，沒有核對此版原始碼|
|11 已取得舊快照|`origin/feature/case-guardian-management`|`505224ab82fd7eb37bbebca0fe1530ec6d6f0364`|已確認；可分析舊實作，不能冒充指定固定版；本機無同名 local branch|
|16 文件明確交接版|`origin/feature/shared-rbac-core` 歷史物件|`1a87aaba71a49de531f2b118ab5531fc7c9de03b`|已確認本機 commit 存在；`docs/questionnaire-integration-handoff.md` 明載此版，是否作 V5.3 正式整合基準仍待 16 ACK|
|16 已取得分支最新快照|`origin/feature/shared-rbac-core`|`a9de56965004921c50de0c306dcecf19da4c8e08`|已確認；在 1a87 後新增交接文件；沒有取得比此更新的正式固定版聲明|
|16 較早問卷階段|`origin/feature/questionnaire-phase2-1`|`197dbf259939b664426d570aed7eab6173bcb9ce`|已確認；已是 main／07 祖先，不再當新功能整合一次|

07 工作包固定版本：

|工作包|Commit|
|---|---|
|07-02 API Client|`f630cdc682d570bc65b3b45159200ce147fcdd30`|
|07-04 前端準備|`0a0644ae0d6a41879158e6afe97a9cfb8f08dc68`|
|07-04 完整 CaseDto／遮罩|`44ec4e4713ae26a2d367d4e64335c949bddfe3ce`|
|07-05 Task／completion|`bd9d19da1d4e4c82e4dd89e25d90da431a34475f`|
|07-06 靜態發布|`e589783281838fcc8e59753e41f97db3efd5446f`|

07-06 必須在 07-05 Task 資源存在的基準上使用，不能獨立放到只有舊 main 的 checkout 而宣稱可打包。實際發布包在 Repository 外，只保留程式白名單／工具，不能將產出包加入版本整合。

## 3. main 差異與其他已取得分支

本機 `main` 與 `origin/main` 都是 `a68ed6d0041cec6efef4549013acdcbf8c0b5a8b`；只是本機相等，未查即時遠端。下表來自 `git rev-list --left-right --count main...REF` 與 `git diff --name-only main REF`。左／右是各自獨有 Commit 數；檔數為兩個 tip 的內容差異，**不是衝突數**。舊祖先分支的反向 diff 看似刪檔，不是刪除提案。

|REF|tip 短碼|main 獨有／REF 獨有|tip 差異檔數|
|---|---|---:|---:|
|feature/medical-backend|e589783|0／30|185|
|origin/feature/medical-backend|f642ec8|0／17|89|
|origin/feature/case-guardian-management|505224a|0／4|57|
|origin/feature/shared-rbac-core|a9de569|0／6|27|
|origin/feature/questionnaire-phase2-1|197dbf2|29／0|69|
|codex/integration-medical-rbac、origin/codex/integration-medical-rbac|1af22c3|0／24|115|
|codex/medical-v53-readonly|7c811aa|0／24|136|
|feature/authorization、origin/feature/authorization|f40a788|32／0|155|
|origin/feature/cloud-migration-preflight|6f9eb60|0／2|14|
|origin/feature/teacher-authorization|25cb35f|44／0|114|
|origin/integration/phase1|b922a40|33／0|91|
|origin/integration/frontend|a68ed6d|0／0|0|
|origin/chore/backend-skeleton|1a78a57|61／0|182|
|origin/feature/gitignore-settings|3395236|59／2|184|
|main、origin/main、origin/HEAD|a68ed6d|0／0|0|

其他完整物件識別：

- `codex/integration-medical-rbac` = `1af22c384284ea0f794585bcb9e45c56b8e93e45`，已取得的舊 RBAC 整合 Commit，但不是目前 07 HEAD 的祖先（兩者各有 13／7 個獨有 Commit）。不可直接以此取代較新醫療成果。
- `codex/medical-v53-readonly` = `7c811aa075eaf4d55e008252ff474f8a9a430195`，是目前 07 祖先；其 worktree 名稱並不代表還有一批新功能必須合併。
- cloud preflight = `6f9eb603b2de98e2e7ae75443861db28fb507abd`；授權舊分支 = `f40a788c2406d624d730bb9861b51f76ea631486`；teacher 舊分支 = `25cb35f67a479c0abd3cf0d8fc306302d4f5f6fd`；phase1 = `b922a40dc124f599fc5ce7901519ec7f40de8fcd`。這些僅是歷史／輔助來源，不自動指定新主責或加入本次必合清單。

既有 worktree：目前 Repository、`medical-v53`、`.codex/worktrees/integration-medical-rbac/Smart-Early-Intervention-Care-System`。只查 Git worktree inventory，沒有切換、修改或宣稱檢查其他 checkout 的未提交內容；未來使用前須各自再查 status。

## 4. 內容已接入不等於分支歷史已合併

`a9de569`／`1a87aab`／`6df33080cacf4aa75e027c11da790b987c451f9b` 都不是目前 07 HEAD 的祖先。但 `git diff origin/feature/shared-rbac-core HEAD -- EarlyInterventionCare.Api/Services/Authorization` 無差異，相關四組 RBAC harness 原始碼比較也無差異。07 已透過自己的整合 Commit 接入相同內容，不能因 ancestry=false 就重寫或重複註冊服務。

相對目前 07，16 仍有真正尚未接入的 `QuestionnaireAnswerValidator.cs` 修改與 `QuestionnaireRoundCompletionEvaluator.cs` 新檔。後者只有純計算核心，**沒有替換 Completion API、沒有 DI／HTTP／CaseStatus 寫入接線**；不能將其存在當成指定有效輪次完成度已上線。07 另有 `Services/Workflow/RoundCompletionEvaluator.cs`；名稱不同不代表業務規則可永久各自演進，選用／Adapter 方式待 07／16 確認，不在唯讀查閱範圍順便重寫 Workflow。

與舊 `1af22c3` 的 Program 比較：舊版本缺目前 `UseMedicalDemoBoundary`／`MapMedicalDemoEndpoints` 四行。整檔採用舊 Program 會遺失已完成的受保護 Demo 接點，不可只因舊 Commit 名稱有 integrate 就全檔覆蓋。

## 5. 主責與唯一落筆人

本節以使用者 V5.3 最新邊界為準，優先於舊 11 文件中「Case CRUD 交接 7」或 16 文件中的建議分工。

|範圍|主責／整合角色|狀態|
|---|---|---|
|醫療 HTML／JS／CSS、MedicalApi Client、醫療 Cookie 登入消費端、靜態打包與 Git 整合協調|07|已確認；不另寫 Case CRUD／問卷交易服務|
|唯一 Case CRUD、Guardian／Consent、家長與教師專用 API、CaseAccess 資源範圍契約|11；問卷共用授權接法與 16 協調|已確認主責；指定 Commit 取得阻塞|
|Questionnaire／Task／Draft／Submit／Response、共用 RBAC、Migration／Snapshot、地端 API／MySQL 環境|16|已確認主責；V5.3 固定版 ACK 待確認|
|Program.cs 唯一最終修改者|建議由 07 整合維護者單一落筆，11／16 審核 DI、授權與安全順序|**待三人確認**；本次未修改、也不是授權日後自行改安全規則|
|DbContext／model configuration／Migration／Snapshot 唯一最終整併者|建議 16 單一落筆，07／11 提供模型差異，使用者核准 schema／套用計畫|DbContext 最終落筆安排待確認；Migration 依既定 16 主責與使用者統整，不平行產生 SQL／EF 歷史|

## 6. API／DTO／安全依賴

|接點|目前依據|狀態／最低確認事項|
|---|---|---|
|醫療登入／me／logout|目前 StaffSessionController、AuthController、StaffAuth；同源 Cookie／CSRF／HTTPS|已確認有程式及離線回歸；正式整合 Host／帳號／Cookie 待驗收，不改 SessionVersion 或登入規則來遷就分支|
|Case GET 列表／詳細|07-04 依使用者固定契約：陣列／單一 11 欄 CaseDto；UUID、uint revision、DateOnly／UTC，page=1、size=20／max100，無 totalCount|消費契約已確認；11 指定 producer 缺失阻塞。舊 505224 的 DTO／路由文字相符，也不等於已核對 0e4e169|
|Task GET／completion|本機 QuestionnairesController／QuestionnaireService／QuestionnaireContracts；13 欄 Task、7 欄 completion，四種 taskStatus|已確認舊實作與 07 消費端；正式 scope／功能 Policy 及固定 producer 待確認，真 API 待驗收|
|醫療結果／匯出|response-detail 目前 Demo；沒有已接妥的一般醫療 Response 路由契約|待確認；不造假 URL，不把正式 SUBMITTED 導向 Demo，不列作本次最小唯讀整合必須新增的功能|
|RBAC 功能 Policy|共用四個已註冊 Policy：case.read、case.status.update、case.status_history.read、questionnaire.task.read|核心內容已接入；註冊不等於角色授權 Seed 或正式端點已套用。沒有 ADMIN 隱含全權，Permission 不能取代 Case scope|
|CaseStatus／History|07 Policy 接點與兩個正式 DenyAll authorizer|已確認保持拒絕；不因整合順便解除。CaseDto 的狀態讀取不等於可改狀態／查歷程，此能力不列唯讀最小路徑先決交付|

Task 與 Case 的必要關係：先由可信 Session 與後端帳號／院所取得可讀 Case，再由 Task producer 驗同一 Case 的功能權限及資源範圍；前端只傳 UUID，逐筆核對回傳 caseId，不能以 browser role／organizationId 或 Case GET 成功替 Task API 授權。Task／completion 是兩次讀取，不保證同一快照；需要原子快照時另訂版本契約。

**現況差異不可自動解決：**

- 目前 07／16 已取得 QuestionnairesController 仍要求 Development 與固定 CoreDevelopmentSeed.CaseId，CheckUser 僅 ADMIN／CASE_MANAGER。StaffAuth 接受 CASE_MANAGER／MEDICAL_STAFF，角色交集及一般醫護查閱範圍尚待核准。
- 舊 11 的 QuestionnairesController 已改成自己的 `CanAccessCaseAsync`，內部使用 `CaseAccessService.ActorAsync`／`ReadableCases`，取消原 `CaseAllowed` 檢查；其拒絕訊息仍沿用 CASE_ACCESS_NOT_INTEGRATED。這是**語意與安全邊界變更**，不是單純解文字衝突。16 為 Controller／問卷主責，11 為 scope 提供者，須共同確認正式接法／403 或 404，不由 07 解除限制。
- 舊 11 `CaseAccessService` 允許同院所 ADMIN／CASE_MANAGER／MEDICAL_STAFF；但 Task CheckUser 仍較窄。不可自行擴權來消除不一致。
- 舊 completion 計全部輪次 IsRequired，取消必要任務仍在分母。16 純 evaluator 明確區分取消待政策／無必要／空輪次，不可硬轉成現有 bool／前端自行選最大 round。先確認 07-08 是否僅接受「原始任務計數」呈現；新評估完成率另議。
- SessionVersion 目前是離線核心與契約，正式權威 Store／Schema／claim 核發與 HTTP 驗證尚未同批啟用。沿用既有 Cookie Session 不等於已實作跨裝置持久撤銷；是否納入 07-08 安全驗收需明確標範圍，不可只啟用一半或用記憶體替身過關。

## 7. 衝突盤點：文字重疊與語意風險分開

以各 pair 的 merge-base 比對兩邊 changed paths，**未執行 merge／merge-tree**，不能宣稱精確衝突 hunk 或可自動合併。07 對 11 各有 30／4 個獨有 Commit；07 對 16 各有 30／6 個獨有 Commit。

已確認 changed-path 交集：

- 07 × 11：`Program.cs`、`Data/ApplicationDbContext.cs`、`wwwroot/index.html`、`tests/Directory.Build.props`（前述三個 .cs／html 路徑均在 EarlyInterventionCare.Api 下）。
- 07 × 16：`Program.cs`；`Services/Authorization/` 九個同名檔；四組 PermissionServiceOffline／RbacOffline／RbacHttp／RbacPolicy 的 `.cs`／`.csproj`。除 Program 外這批 RBAC 內容目前相同，屬 add/add 或相同內容的潛在整合重疊，不是已證實內容衝突。
- 11 × 16：`Program.cs`、`tests/QuestionnairePhase22Harness.cs`。

|檔案／範圍|具體風險|建議處理人／規則|
|---|---|---|
|Program.cs|07 Cookie／CSRF／RateLimiter／Demo、安全 Middleware 順序；11 Case／Guardian／OTP DI；16 RBAC／例外 Middleware，不能全檔 ours/theirs|待確認的唯一落筆 07；11／16 逐段審核，不重複 Policy／DI，保留 HTTPS→Authentication→Cookie CSRF→Authorization 的既有相對要求及窄範圍例外邊界|
|Data/ApplicationDbContext.cs|07 CaseStatusHistories；11 Guardians／CaseGuardians／ParentBindingInvitations 都要保留，非二選一|16 整併候選，07／11 review；不得順手執行 Migration|
|Data/Entities/CaseRecord.cs、GuardianModelConfiguration.cs、IdentityModelConfiguration.cs、Models/Authentication/User.cs|11 新增 nullable OrganizationId、uint Revision、NationalId／Address、PhoneVerifiedAtUtc 等；07 CaseStatus EF store 也依賴同一 cases|11 提供固定模型及 scope，16 review 映射／回填／並發；不建第二張 Case／Guardian|
|Data/CaseStatusHistoryConfiguration.cs、CaseStatusHistory Entity、Data/Migrations/*|模型已增加但無對應新 migration；Snapshot 相同不等於已可用|16 唯一 Migration 出口；使用者核准資料與套用計畫；未追蹤 SQL 排除|
|Controllers/CasesController.cs、Services/Cases/CaseGuardianService.cs、CaseAccessService.cs|目前 07 無 Case GET producer；11 Case／Guardian 邏輯同檔；舊交接分工與 V5.3 不同|11 維護原實作，07 只消費 DTO；共用 scope 方法改動先協調，不重寫 Service|
|Controllers/QuestionnairesController.cs|11 改動 scope，16 擁有問卷核心；即使 Git 沒報衝突仍可能移除正式安全閘門|16＋11 定契約與審核；07 不自行移除 Development 限制|
|DTOs/Cases/CaseContracts.cs／CaseStatusContracts.cs；DTOs/Questionnaires/QuestionnaireContracts.cs|CaseDto 與狀態 DTO 不同檔、無現有同名重複證據，但 GUID／uint／UTC、nullable、scope／錯誤語意須核對；16 新 evaluator 不是舊 completion DTO|11 固定 Case DTO，16 固定 Task／completion；07 consumer contract test；不要為 UI 添加未核准欄位或重算分母|
|Services/Questionnaires/QuestionnaireAnswerValidator.cs、QuestionnaireRoundCompletionEvaluator.cs；Services/Workflow/RoundCompletionEvaluator.cs|16 仍有未接入修正／新 evaluator；與 07 計算語意重疊|16 核心主責＋07 Workflow 契約協調；純讀路徑不順便啟用狀態寫入|
|wwwroot/index.html、case-create.html、css/system-interface.css，以及 parent／teacher JS|index 是實際交集；其餘即使不同 Commit 沒重疊也有共享導覽／CSS／腳本語意影響；11 改 teacher-draft/workspace 等檔|醫療檔 07、家長教師 11；共享資源逐頁回歸，禁止只測醫療頁就接受全站 CSS|
|wwwroot/js/medical-api.js、medical-case-*、medical-task-*；tools/medical-frontend/*|11／16 不應用舊 Client／靜態工具覆蓋；07-06 allowlist 依賴 07-05|07 單一 client 與資源清單；全部前端回歸及發布包 hash／路徑檢查|
|tests/Directory.Build.props、QuestionnairePhase22Harness.cs|11 多設定 MSBuildProjectExtensionsPath；07 已隔離 BaseIntermediateOutputPath；16 新增不同測試；錯用共用 obj 會跑錯 harness|07 整合檢查、11／16 保留各自案例，確認實際執行 DLL，不能用一方整檔蓋掉|

## 8. Migration 與資料庫順序（計畫，未執行）

目前 07、已取得 11／16 的 Migration 及 Snapshot **內容無差異**，都有四份：

1. `20261005152359_InitialSharedCore`
2. `20261005180101_AddAuditLogs`
3. `20261005184434_AddSubmissionReceiptWindow`
4. `20261006074424_IntegrateIdentityAndAuthorization`

這只是檔案歷史，不是任何資料庫的已套用清單。本次未連 MySQL，`__EFMigrationsHistory`、現有資料與待套用狀態均未知。11 舊交接已明示其模型與四份 Migration 不同步；07 的 CaseStatusHistory 模型同樣未附新 Migration。

建議順序：先固定原始碼與三方 model 差異 → 16 檢查合併後 model／Snapshot 與受控環境既有歷程 → 確認 Case 院所回填、Guardian／PhoneVerified、CaseStatusHistory 是否為本期所需及既有資料政策 → 16 統一產生「後續」Migration／Snapshot 與升級／失敗處置計畫 → 使用者核准 → 由授權人員在全虛構地端資料庫套用及驗收。不得刪改既有已套用歷史、重跑 seed 覆蓋資料、與 raw SQL 平行維護正式 schema，或拿未追蹤 SQL 當整合入口。SessionVersion 若未納本期，不偷偷附加欄位／啟用；若要啟用須整批契約另核准。

## 9. 建議整合順序與停止點

1. **版本／權限 gate**：07 e589 固定；取得 11 的 0e4e169 物件（另行許可的取得方式，不在本次 Fetch），16 ACK 1a87／a9de 或提供新 pin；確認最新 main、分工與 Program／DbContext 唯一落筆者。任一安全／schema 契約未明，停止該部分，不猜測。
2. **保護基準**：後續另行核准建立隔離 integration branch／worktree；建議以目前 07 e589 為候選基準保存既有成果，因它包含已取得 main 的歷史。真正動作前重新核對 pins／所有 worktree status；不要在 feature 或 main 直接合併，也不要拿舊 1af 整檔替換。
3. **16 增量核對**：先處理已重複的 RBAC 相同內容與 Program 註冊，再由 16 核對尚未接入的答案驗證修正／輪次元件。具體 Merge 或選擇性套用方式須另行核准；本文件不是 Cherry-pick 清單，不重複開發服務。
4. **11 唯一 Case／Guardian producer**：整合指定固定版、CaseAccess、User／model／DI，原有 Service 沿用不重寫。Program 與 DbContext 單一落筆、雙方 review。家長教師前端以 11 為主責，保留 07 醫療與共享 CSS 契約。
5. **Case／Task 授權與 DTO gate**：由 11／16 固定正式 Task scope、功能 Policy、角色與 403／404；確認 Case GET／Task／completion 真正相容，不自行解除 Development／DenyAll。先維持最小醫療唯讀路徑，不加入狀態寫入／完整 Excel／排程。
6. **程式回歸 gate（不需真 DB）**：Build、DTO consumer tests、Cookie／CSRF／Permission／parent／teacher／Task 回歸與靜態包測試；確認沒有第二套 Client、Demo fallback、憑證／資料持久化或移除 26 題。保留已取得測試的完整案例。
7. **Schema／環境 gate**：依上一節由 16 與使用者固定單一 Migration／Snapshot、授權套用環境、全虛構資料與同源 HTTPS 設定。這是正式測試前準備，不能用 Fake Host 或編譯成功取代。
8. **07-08 真服務驗收**：取得明確批准後才使用固定 integration SHA＋地端 HTTPS API／MySQL；至少醫療 Cookie 登入→me→Case GET→Task／completion→重新查詢提交狀態，驗空資料、未登入、越權／跨院所、503、不跨個案殘留、不回退 Demo、登出與重啟後持久狀態。報告分開列前端、真 API、真 MySQL；若 C 方案也驗正式家長單一填答，需 11／16 固定 Parent Draft／Submit／Response scope／交易，Development 教師流程不能替代。

## 10. 三人確認與最低驗收清單

### 先問 11

- [ ] 請提供可取得的 `0e4e16939b2fb711190aa490ca80098f0615ff47`，或明確批准替代 pin；與本機 505224 差異是什麼？禁止默認用舊版代替。
- [ ] 確認唯一 Case CRUD 仍由 11 維護，舊文件交接 7 的文字已不適用；CaseGuardianService／CaseAccess 同檔哪些方法不能由 07 修改？
- [ ] 確認 CaseDto 十一欄／nullable／page-size／無 totalCount／404／遮罩界線；提供同院可讀與跨院不可讀的全虛構案例，不提供真個資。
- [ ] 提供 Case／Guardian／User 的 model 與回填需求給 16；正式 scope 接到 Task 的方法、錯誤碼與父母綁定撤銷如何協調？
- [ ] 與 16 固定 C 方案家長單一填答所需的題目／Draft／Submit／Response 契約與可驗收 pin；先區分已有 UI、Fake 測試與正式交易。

### 先問 16

- [ ] 以交接文件 1a87（a9de 為文件 tip）作固定版，還是有更新且可取得的 Questionnaire／RBAC pin？請列實際端點與測試範圍。
- [ ] RBAC 核心已與 07 內容相同，如何避免重複註冊？Task API 的功能 Policy、CaseAccess 接法／角色矩陣／Development 限制由哪個核准版本完成？
- [ ] completion 目前只呈現原始任務計數是否可驗收？有效輪次、取消必要任務、無必要與空輪次的語意哪些仍待主任決定？不要求前端代算。
- [ ] 請統一 07／11 model 與四份既有 Migration 的差異，提供唯一後續 Migration／Snapshot、已套用歷程的去識別摘要與地端全虛構環境；不是請現在執行套用。
- [ ] 正式家長 Workflow／作者／Case scope／Consent／交易是否在固定版可用？提供同源 HTTPS／API 運行與授權方式，不分享 DB 密碼、JWT 或 Cookie。

### 開始 07-08 正式跨模組測試前

- [ ] 三方 ACK 固定 SHA、唯一最終修改者與整合分支基準，取得缺失物件後重新算差異／交集。
- [ ] 整合後 Build／HTTP／契約／前端回歸可重現，沒有誤執行其他 harness；所有必要測試依賴在本機可用。
- [ ] Case CRUD／Task producer 真正可編譯且 scope、Permission、角色、DTO／錯誤契約獲核准；不靠解除安全閘門冒充整合。
- [ ] Migration／Snapshot 與 model 對齊，資料庫已套用歷程、回填、測試資料及恢復方案由主責確認；沒有平行 raw SQL。
- [ ] 明確授權地端同源 HTTPS／全虛構 MySQL 測試，Cookie／CSRF／Origin 保持既有安全規則；真患者資料不在測試或文件中。
- [ ] C 方案三條路徑各有負責人及固定版本；不因 07 離線通過就宣稱正式家長流程／全隊已完成。

## 11. Demo／Fake／正式 API／真 MySQL 驗收矩陣

|層級|已取得依據|本次狀態|
|---|---|---|
|Demo|07 Development 保護的 DEMO-001 SNAP-IV 26 題、原 wwwroot 與明確 Demo 包|已確認有實作及前輪瀏覽器紀錄；不是正式身分／寫入證據|
|Fake API／Contract Test|07-04／05／06 文件：完整前端 220 通過、Release Build 0 警告／錯誤；提交前 Task／包裝 27 通過|已確認歷史紀錄，本次只有 Git／原始碼／文件查核，**未重跑測試**；不將歷史數字當成本次新測試|
|正式 API|Case 指定 producer 缺本機物件；Task 當前仍 Development scope；RBAC／Cookie 有程式但跨分支未驗|阻塞／待確認／待驗收，分模組處理，不能統稱正式串接完成|
|真實 MySQL|歷史文件有舊底座驗收聲明，但不是 e589＋指定 11／16 的新組合|待驗收；本次沒有連線、讀取 schema、執行 Migration／Seed 或使用真資料|

07-05 曾記錄 Phase 2-2 在本機 `--no-restore` 缺 project.assets.json（NETSDK1004），未能執行；16 分支文件的舊版本通過聲明不能代替這個 checkout 的驗收。tests/Directory.Build.props 變更及本機依賴須在正式測試前由整合者確認，不在本次下載／restore 套件。

**結論：尚未具備直接開始 07-08「正式 API＋真 MySQL 跨模組驗收」的條件。** 可先三人確認本 Manifest 與既有離線接點；11 指定 Commit、16 版本 ACK／正式 scope、model／Migration 及 HTTPS／虛構 DB 環境未固定前，不 Merge、不修改他人服務、不解除 DenyAll、不宣稱正式完成。本文件尚未 Commit，待使用者下一步。

## 12. 唯讀查核重現

```powershell
git status --short
git branch --show-current
git log -1
git worktree list
git for-each-ref --format='%(refname:short)|%(objectname)|%(subject)' refs/heads refs/remotes
git cat-file -t 0e4e16939b2fb711190aa490ca80098f0615ff47
git cat-file -t 1a87aaba71a49de531f2b118ab5531fc7c9de03b
git rev-list --left-right --count main...feature/medical-backend
git rev-list --left-right --count main...origin/feature/case-guardian-management
git rev-list --left-right --count main...origin/feature/shared-rbac-core
git merge-base HEAD origin/feature/shared-rbac-core
git diff origin/feature/shared-rbac-core HEAD -- EarlyInterventionCare.Api/Services/Authorization
git diff --name-status main origin/feature/case-guardian-management
git diff --name-status HEAD origin/feature/shared-rbac-core -- EarlyInterventionCare.Api/Services/Questionnaires
```

`git merge-base` 只讀祖先，並未執行 Merge。舊分支交接文件用 `git show REF:path` 唯讀，不要求把對方文件 checkout 到本工作目錄。
