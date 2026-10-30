# V5.3 三人整合執行計畫

- 日期：2026-10-30。
- 狀態：供三人審查的執行建議，尚未共同核定；不是正式整合完成或資料庫驗收證明。
- 本文件交付範圍：整理四輪唯讀盤點，僅新增本計畫。後續程式修改、Git 整合、Migration、服務及資料庫操作均須依另行授權執行。
- 里程碑：11/09 為暫定內部整合目標；安全、契約或院方政策未固定時縮小驗收範圍，不跳過檢查。

## 1. 固定版本與證據規則

| 組員 | 固定分支 | 唯一比較基準 Commit |
|---|---|---|
| 16 | `codex/v53-parent-core` | `87edb22208ab992db5e00774d85c51d4c510e946` |
| 11 | `feature/case-guardian-management` | `e2affb548bb427cd02d001d5bfd5761d7ecb4648` |
| 07 | `feature/medical-backend` | `b9f11330f756747ee6cd9e6fcdf0488ea520d773` |

三個版本均可由本機 Git objects 讀取。盤點未 Fetch、切換分支、合併、建置、執行測試、連線資料庫或啟動服務。本文的版本證據以固定 SHA 為準，不以分支未來 tip 取代。

證據分成：**原始碼已確認**（固定版本存在的程式或 Git blob）、**歷史紀錄／文件宣稱**（未於本輪重跑）、**建議方案**、**待共同決策／待院方核准**、**尚未驗收**。任何單元、Fake、離線 ADO 或 SQLite 成果均不等於三人正式 API／MySQL 整合成功。

參考文件：

- [共用契約](shared-contract-v1.md)、[共用授權契約](shared-authorization-contract.md)。舊文件與本次固定原始碼不同時，記錄差異並共同決策，不自動採用較寬鬆規則。
- [16 家長核心交接](v53-16-parent-core-handoff.md)、[16 驗證紀錄](v53-16-verification.md)、[Gateway 準備文件](v53-16-preparation/gateway-handoff.md)。後者基準為較早的 bb74e72，不能取代本輪固定 SHA。
- [隔離資料庫規劃](v53-16-preparation/mysql-plan.md)、[驗收案例](v53-16-preparation/acceptance-cases.md)、[RBAC 矩陣](v53-16-preparation/rbac-matrix.md)、[地端操作手冊](v53-16-local-operations.md)。
- 11 固定 Git 內容：`docs/integration-handoff-11-20261030.md`、`docs/v53-11-consent-gateway-handoff.md`、`docs/v53-11-integration-model-teacher.md`、`docs/v53-11-validation.md`。
- 07 固定 Git 內容：`docs/v5.3/integration-manifest.md`、`EarlyInterventionCare.Api/Services/Workflow/ICaseStatusWorkflowService.cs`、醫療 API Client 與 Program。其他版本的文件不複製到本 Worktree，使用固定 Commit 唯讀查閱。

## 2. 已有成果與未交付邊界

| 組員 | 原始碼／歷史證據已有成果 | 尚未交付、未接妥或未驗收 |
|---|---|---|
| 16 | 家長 Gateway、Draft Revision、Submit Idempotency、Response／Task 交易核心；EF Store；RBAC、ScopedReadAuthorization／ICaseTaskReadScope 接點；家長作者模型；History 第五筆 Migration；Level 1 與地端準備文件 | 正式 11 授權 Adapter／HTTP Controller 組合、醫療正式 Scope／端點接線、共同 Schema／增量 Migration、真 MySQL 並行／持久化／還原驗收 |
| 11 | 唯一 Case CRUD、CaseAccess、Guardian／綁定邀請、Email 登入相關程式；Consent Entity／Mapping／Service；家長 Controller 與 Gateway 抽象、Definition 入口 | 預設 Gateway 仍不可用；Consent 預設關閉；正式本文與 Scope 未核准；與 16 真交易橋接、共同 Schema 及三人服務驗收未完成；教師 localStorage 展示不是正式授權保存 |
| 07 | 醫療登入／Cookie／CSRF、Case／Task API Client、靜態前端準備、受保護 Demo；CaseStatus／History 接點及 Workflow 契約／純規則 | 正式醫療 Scope 與角色矩陣、三方 API／DB 實際串接；未 Commit Workflow Service／Store 不列正式交付；有效輪次／封存／自動狀態寫入待協調 |

16 驗證文件記錄 ParentQuestionnaire 126 PASS、EfParentStore 86 PASS 等歷史成果；EfParentStore 使用無 Socket ADO 替身，沒有驗證 MySQL 引擎。CoreDatabase／History offline 的模型 Snapshot gate 缺口不能改寫為通過。Level 1 health 的 `databaseChecked=false` 也不是資料庫驗收。

07 Manifest 記錄的未追蹤 `CaseStatusWorkflowService.cs`、`ICaseStatusWorkflowStore.cs`、對應 Harness、`docs/database/init-0-to-04.generated.sql` 及名為 `git` 的檔案不納入固定交付；本輪沒有檢查其他 Worktree 的即時內容。

## 3. 契約差異與對齊方案

### 3.1 16＋11 Gateway／Consent

主要依據：16 的 `Services/Questionnaires/ParentQuestionnaireGateway.cs`、`EfParentQuestionnaireStore.cs`、`ParentQuestionnaireContracts.cs`、`DTOs/Questionnaires/QuestionnaireWorkflowContracts.cs`；11 的 `ParentModule/ParentControllers.cs`、`ParentQuestionnaireContracts.cs`、`ConsentService.cs`、`ParentModuleRegistration.cs`。

| 差異／風險 | 建議方案 | 決策／負責人 |
|---|---|---|
| 11 ParentRequestContext 與 16 ClaimsPrincipal 不同 | 由專用橋接 Adapter 使用伺服器可信 Actor；不得由 body 合成作者或把 context 當成資格證明；交易內重新查 User／院所／Case／Guardian／Consent | 11 提供 Actor／Scope，16 定交易邊界；共同確認 |
| HTTP Revision 字串，核心 ulong | 邊界嚴格解析十進位字串並檢查範圍／overflow；輸出仍為字串，前端不經 Number；DB 保留 bigint unsigned | 11 HTTP、16 核心；固定解析契約 |
| 11 有 RespondentRelationship，核心未保存 | 候選為 Draft／Response nullable varchar(32)；舊列／教師保留 NULL；不以關係文字作授權依據 | 11＋16 固定代碼、必填及 Hash 語意 |
| SaveDraft 有 ExpectedRevision；Submit 尚無一致規則 | 決定提交以輸入完整 payload 或指定草稿版本為準；若加入提交 Revision，先定首次提交及成功重送的判定順序，不能讓成功重送被新草稿衝突誤拒 | 11＋16 決策，16 核心主責 |
| canonical payload／Content Hash 與 DTO 不同 | 固定欄位、排序、日期、空值／空白、答案去重及關係欄位是否入 Hash；保留舊重送相容性，不混用 Consent 本文 Hash 與 Response PayloadHash | 16 提案、11 確認 |
| 錯誤碼與 HTTP envelope 不完全相同 | 建立可重用映射：400 輸入、401 身分、403／404 Scope 遮罩、409 Revision／Idempotency、503 未整合／儲存異常；既有錯誤碼逐項對照，不只改 HTTP 數字 | 11 HTTP boundary，16 核心；07 消費端 review |
| Consent 先查一次不足以處理撤銷競態 | 共用 scoped DbContext／transaction，授權與提交前重驗；共同協調所有撤銷、綁定與提交 writer 的鎖順序，不能只在 Controller 查一次 | 11 寫入端、16 交易端共同負責 |

16 的候選鎖序為 Organization → User → Case → Guardian／Link → Consent → Task，同類 ID 排序；這是交接規格，不是已證明 11 所有 writer 均遵守。必須共同確認，並以真 MySQL 競態驗證。可信 Task scope 的初次定位不能取代鎖內重查。

Consent 現有程式要求開關及指定文件／Scope，文件核准、生效、未退休且本文 Hash 正確；有效紀錄還須符合 Actor、院所、Scope、本文 Hash、未撤銷及 DataUseDecision=AGREED。教師參與同意不能混為家長本人填答的同一決策。正式本文、適用 Scope 與啟用政策由院方確認。

11 Controller 應透過橋接呼叫 16 唯一引擎；不新增另一套 Response Service／表／交易。未接妥保持預設拒絕／503。

### 3.2 07＋16 醫療 Permission／Scope

主要依據：固定版 `Controllers/QuestionnairesController.cs`、`Services/Authorization/`、11 的 CaseAccess／Guardian Service、07 `wwwroot/js/medical-api.js` 及醫療 Case／Task consumer。

| API／行為 | 現況差異 | 建議授權邊界 |
|---|---|---|
| Case 讀取 | 11 CaseAccess 有同院角色判定；07 消費固定 Case DTO；角色本身不證明可讀特定 Case | case.read ＋可信院所／Case Scope，11 維護唯一 producer |
| Task／completion 讀取 | 07／16 Questionnaire Controller 仍有 Development 固定 Case 與 ADMIN／CASE_MANAGER 限制；醫療登入接受 MEDICAL_STAFF；11 有不同 CaseAccess 接法 | questionnaire.task.read ＋ Case／Task Scope；共同核准正式接線後才替換開發 Gate |
| Definition 讀取 | 必須區分家長指定 Task 讀題與醫療管理讀題 | 綁定版本及資源 Scope；不可因 Task 可讀就授予版本修改／發布 |
| Assignment／Task 寫入 | 讀取權限不能推導指派能力 | 另訂指派 Permission／Case Scope／輪次規則，不隱含授予醫護 |
| Response 原始答案 | 一般醫療正式讀取／投影契約未完整固定；Demo 不是正式答案 API | 另定最小投影及讀取權限；家長教師完成狀態 DTO 不含教師逐題答案、分數或草稿 |
| CaseStatus／History | 既有 DenyAll authorizer 尚未正式替換 | 狀態寫入、History 讀取各自核准，不因 Case 可讀而放行 |

ScopedReadAuthorization、ICaseTaskReadScope、Policy 的存在不證明 Controller／DI 已接妥。正式路徑須同時驗身分、Permission、院所及資源 Scope；ADMIN 不視為隱含全權。不得直接移除 Development／Demo 邊界來解角色不相容。

現有 completion 為跨輪次原始任務計數，不等於有效輪次完成度。Task／completion 分開查詢也不保證同一快照。前端不得自行選最大輪次或重算已取消必要任務政策。

### 3.3 Entity／DbContext／Migration

主要依據：各固定版 `Data/ApplicationDbContext.cs`、`CoreModelConfiguration.cs`、`IdentityModelConfiguration.cs`、`Data/Entities/`、`Data/Migrations/`；11 `GuardianModelConfiguration.cs`／`ParentModule/ConsentModels.cs`；16 `ParentQuestionnaireModelConfiguration.cs`。

| 範圍 | 原始碼已確認 | 建議聯集／待決策 |
|---|---|---|
| DbContext | 16／07 相同；11 增 Guardian 三 DbSet、缺 History | 保留核心、History、身分權限、Guardian 共 19 個既有 DbSet；Consent Configuration 由 assembly scan 納入，無命名 DbSet 不代表缺模型 |
| Case／User | 11 新增 nullable Case.OrganizationId、uint Revision、NationalId／Address 及 User Email／驗證時間 | 保留擴充；舊院所與必填欄位回填先決策，禁止虛構身分／同意 |
| Draft／Response | 16 有 nullable ParentAuthorUserId、User Restrict FK、作者索引及作者＋IdempotencyKey 唯一鍵 | 保留教師／舊列 NULL；維持 Task 唯一、版本／Grant 複合 FK；不讓舊分支覆蓋作者 |
| Guardian | 院所＋Phone 唯一；CaseGuardian.ActiveCaseId 為 stored computed unique，ACTIVE 且 Primary 即占位，不要求 BoundAtUtc | 11 確認 Email-only 與主要聯絡人語意；FK 不會自動證明跨表同院 |
| Consent | Actor＋RequestId、Actor＋院所＋文件版本＋ScopeKey 唯一；撤銷列仍占唯一鍵 | 同版本重同意及歷史保存需共同決策；不自動連結 TeacherGrant.ConsentReferenceId |
| History | 16／07 有模型，僅 16 有第五筆 Migration | 全部保留；現有 History 無提交事件去重唯一鍵，Workflow 如需新鍵另提案 |
| Mapping／版本 | UUID char(36) ascii_bin；核心 JSON／ENUM／unsigned；Email binary collation；Consent 字串比較需明確核對 | 審查合併後 metadata、computed column／NULL 唯一性及舊資料；不依賴 Configuration 掃描順序解衝突 |

五筆不可刪改／重建的歷史（每筆 `.cs` 與 `.Designer.cs` 均保留）：

1. `20261005152359_InitialSharedCore`
2. `20261005180101_AddAuditLogs`
3. `20261005184434_AddSubmissionReceiptWindow`
4. `20261006074424_IntegrateIdentityAndAuthorization`
5. `20261019131342_AddCaseStatusHistories`

前四組檔案三方 blob 相同。16 Snapshot blob 為 `5156015938053ea679d2e05c060c89b63e898e61`；11／07 同為 `c8ad951e49108a62be0974ae377305254bcbbdce`。16 Snapshot 尚缺家長作者擴充；11 尚缺 Guardian／Email／Consent 等擴充；07 尚缺 History。不得採舊 Snapshot 覆蓋第五版。07 舊 Manifest 的「三方僅四份且相同」描述不適用本輪固定版本。

程式均宣告 MySQL 8.0.46、net8.0、EFCore.Design 8.0.13、Pomelo 8.0.3；11 文件的 MySQL 8.4 驗收目標須先協調。11 多 MailKit 4.18.1；07 的 MedicalDemoFixture 為 EmbeddedResource，不能被專案檔整檔覆蓋成公開 Content。實際還原套件及資料庫版本尚未於本輪驗證。

## 4. 決策清單

### 4.1 已確定的執行基準／安全邊界

- 使用本文件三個固定 SHA；不納未 Commit Workflow、不以舊交接 pin 代替。
- Case／Guardian／Consent 主責 11；共用問卷／RBAC／Migration 主責 16；醫療 UI／Client／Git 協調主責 07，延續 V5.3 所有權。
- 保留單一 Case、Response、DbContext 與五筆 Migration 歷史；保留原始答案，提交後不提供家長／教師修改原答案能力。
- 未整合權限保持拒絕；虛構資料不能成為院方政策。以上不是宣告本計畫細節已獲三人核定。

### 4.2 待 07／11／16 共同確認

| 決策 | 提案／確認人 | 阻塞範圍 |
|---|---|---|
| Context／Principal 橋接、Revision、關係欄位、Submit Revision、canonical payload／錯誤 envelope | 16＋11 | 正式 Gateway Adapter／Controller 契約 |
| 全部 writer 鎖序、撤銷／綁定／提交競態、成功重送順序 | 11＋16 | 真交易接入與並行驗收 |
| 醫療角色 Permission、Case／Task Scope 接法、403／404 遮罩、唯讀 DTO | 16＋11＋07 | 正式 MEDICAL_STAFF 接入 |
| 原始計數或有效輪次完成度；取消必要任務及無必要／空輪次語意 | 07＋16，涉及醫療規則時送院方 | completion／Workflow 狀態更新 |
| Guardian Phone／Email-only、主要聯絡人占位、Consent 同版本重同意、舊列回填 | 11＋16 | 新 Schema 與資料升級 |
| MySQL 8.0.46／8.4、Program／DbContext 唯一落筆及新整合版本 | 三人，使用者核准環境 | 套件／模型／部署驗收 |

### 4.3 待院方核准

- Consent 正式本文、ACCOUNT／CASE 適用範圍、蒐集前程序、啟用／撤銷／重同意政策。
- 教師參與撤回後授權及既有答案處理；期限／重新開啟與有效輪次規則。
- 醫療人員可讀非負責個案的範圍、指派與答案查閱權限。
- 未核定項目只保留模型／拒絕介面，不填入假核准 seed 或推定同意。

## 5. 建議分工、最小修改面與依賴

| 工作 | 建議主責 | 最小候選範圍／依賴 |
|---|---|---|
| Gateway 核心與 HTTP 橋接 | 16 核心、11 Controller／Actor／Consent | ParentQuestionnaireContracts／Gateway、11 ParentModule／Adapter、Contract Suite；先完成決策 |
| 醫療讀取接線 | 07 Client、11 CaseAccess、16 問卷／授權核心 | QuestionnairesController、ScopedReadAuthorization／ICaseTaskReadScope、Case／Task consumer tests；先核准 Scope |
| 共同模型 | 11 提供模組 Entity、16 統整 | ApplicationDbContext、CaseRecord／User、Guardian／Consent Mapping、Draft／Response／Parent Mapping；不整檔取代 |
| Program／DI | 建議 07 唯一最終落筆；11／16 審查 | 保留 RBAC、Cookie／CSRF、Demo、IsolatedTest、Parent filter；測試實際解析型別與 middleware 順序 |
| Migration／Snapshot | 16 單一出口，使用者統整核准 | 第五版歷史後新增增量；不改舊 Migration／Designer；SQL 草案不是第二條正式歷史 |
| Workflow | 07 提供固定交付，16 整合交易審查 | 同 scoped DbContext／交易；Submit coordinator 統一 SaveChanges／Commit；權威輪次、去重、鎖序先固定 |

依賴順序：固定決策與版本 → Gateway／Scope 契約 → Adapter／DI 與模型聯集 → 離線回歸 → 增量 Migration 審查 → 核准隔離環境 → 真 API／MySQL 驗收。Workflow 可暫緩；家長最小提交不應為等待未交付 Workflow 而重做核心。

## 6. 可先進行與須等待的工作

以下是下一批可授權的開發建議；**本次僅新增本文件，不授權執行它們**。

可優先做的非破壞性工作：

- 整理合成 DTO／JSON fixture：UUID、Revision 上界／錯字串、日期、空值、跨院與撤銷情境；不內嵌真憑證或個資。
- 重用 ParentQuestionnaire、EfParentStore、ScopedReadSafety、RBAC、11 Contract Suite、07 consumer tests；先列出既有覆蓋，僅補邊界缺口。
- 設計無 Socket Contract Tests：可信 Actor 橋接、Revision 無損轉換、拒絕跨個案／院所、503 不回退 Fake、錯誤 envelope 不洩 SQL／Token。
- 準備 DI／模型預期清單、升級資料樣本、分層驗收表與操作失敗紀錄；保留現有地端工具及手冊，不另寫重複服務。

必須等待的工作：

- 契約未固定前，不實作具正式語意的橋接、Submit Revision／Hash 變更、Response Relationship 寫入。
- 未核准 Scope 前，不移除 Development Gate、DenyAll、不擴 MEDICAL_STAFF／ADMIN 權限。
- 模型／舊資料／版本未確認前，不產生 Migration 或更新 Snapshot；真 DB 操作另需環境核准。
- 未取得已 Commit Workflow 與有效輪次權威來源前，不啟用 Case 自動完成／新增推測欄位。

11/09 前可獨立完成的候選是合成 fixture、離線契約／拒絕案例、DI／metadata 檢查設計、驗收與復原紀錄格式。正式家長流程、醫療 Scope、共同 Schema 與真 MySQL 驗收取決於三方固定交付及另行核准，不承諾日期即等於驗收完成。

## 7. 分階段驗收與停止／回復

| 階段 | 進入條件 | 必驗項目／通過標準 | 失敗處置 |
|---|---|---|---|
| A 契約凍結 | 三人確認決策與固定 SHA | DTO 型別／日期／Revision／錯誤、Scope、鎖序、關係與 Hash 有明確版本；未定政策列阻塞 | 保留提案，停止受影響實作；不以測試替身代定政策 |
| B 程式與離線整合 | 另獲修改／建置／測試授權 | 編譯、既有 regression、Controller↔橋接↔唯一 Gateway；DI 型別／同 scoped context；Cookie／CSRF／Demo／IsolatedTest 不退化 | 停止接線；保留 diff／測試輸出，由檔案主責修正，不覆蓋他人成果 |
| C 模型／Migration 審查 | 共同模型、舊資料與 MySQL 版本核定，另獲產生授權 | DbSet 聯集、FK／索引／collation、五筆歷史完整；只含核准增量；空庫／第四版／第五版升級計畫 | 出現 Drop／重建歷史、未批准欄位或不明回填即停；不套用 |
| D 隔離 MySQL | 專用 127.0.0.1:33326、資料目錄、帳號、備份及套用步驟逐項核准 | FK／唯一鍵、持久化／重讀、Revision、同 key／異 payload、並行提交、撤銷競態、失敗回滾；一個 Response 與 Task 狀態一致 | 保留去識別日誌、版本／Migration 歷史摘要；停止寫入，不反覆盲重跑 DDL |
| E 三方真服務 | Scope／Consent 合規，核准虛構帳號與正式 Adapter | 家長登入→有效同意→綁定→讀題→草稿→續填→提交→重讀；醫療登入→可讀 Case／Task；跨院／越權拒絕、故障不顯成功 | 停止受影響入口；不得切回 Demo／Fake 冒充成功；記錄每層失敗 |
| F 重啟／還原 | D／E 證據與獨立還原目標／工具核准 | 重啟資料保留、備份可用、獨立庫還原、筆數／關聯／狀態核對；不覆蓋原庫 | 保留原庫與備份；由核准人決定 forward fix 或已驗證還原，不自動 Down |

真資料庫驗收須包含故障發生於 Response 寫入、Task 更新及提交前資格重驗等不同位置。交易內重查不等於已處理撤銷競態；須確認同一鎖協定及實際 MySQL 行為。

Workflow 若納入 E：Submit coordinator 先在同交易保存 Response／SUBMITTED Task，再由 Workflow 查可信輪次及全部必要任務，僅 staging Case／History，最後統一提交。未知輪次／封存／取消政策不更新狀態；重送不重複 History；不能另開 DbContext／交易自行 Commit。

每次驗收至少記錄：固定 SHA／設定版本、執行者與時間、環境與證據層級、案例 ID、合成資料識別、預期／實際 HTTP 或資料結果、PASS／FAIL／BLOCKED、脫敏日誌及停止清理結果。不記錄密碼、Token、Cookie 或完整私密設定。

資料回復原則：先辨認失敗停在哪個增量步驟，DDL 不假設整批可交易回滾；未核准不得自動執行 Down／刪欄／重建庫。獨立還原庫的名稱及驗證連線方式另審查，不為使用還原庫而放寬既有 API guard。程式回復以保留修改與已知版本為基礎，由後續授權決定；不使用 reset／clean 清除成果。

## 8. 不可跨越的安全界線

- 不使用既有 MySQL80／3306；只在另行核准後操作專用 127.0.0.1:33326，資料目錄／tmp／logs 與現有服務分離。
- Runtime 與 Migration 帳號分離；Runtime 必須符合既有 guard（目前規劃 earlycare_v53_app／earlycare_v53_synthetic），不擴權或放寬 guard 以求通過。帳密由安全設定提供，不寫入本文件。
- 不刪除、覆蓋、重新建立五筆歷史 Migration／Designer；不將 raw SQL 草案或未追蹤建庫 SQL 當正式升級來源。
- 不自動核准 Consent 本文／Scope、不回填假同意、不將 Guardian 綁定或登入視為同意；不跳過交易內資格檢查。
- 不授予醫療讀取者指派、修改、提交能力；不自行決定非負責個案可讀範圍；不把前端隱藏當後端授權。
- 不納入未 Commit Workflow，不修改其他 Worktree／組員模組；跨模組缺陷交由對應主責修正。
- 僅虛構資料；不對外開放 API／MySQL。遠端裝置、HTTPS、防火牆及網路調整列後續獨立核准。
- 本計畫未執行 Merge、Commit、Push、Migration、資料庫連線或服務啟動。文件完成只表示計畫可供審查，不表示正式整合、院方核准或 MySQL 驗收完成。
