# V5.3 共同 Schema 增量白名單與隔離資料庫驗收清單

狀態：**三人審查提案，尚未共同核准，不是 Migration／資料庫操作授權。** 本文件僅做已提交原始碼的靜態整理；所有驗收案例均為「待執行」，沒有本輪 MySQL、資料遷移、備份或還原 PASS。

## 1. 固定版本、來源與分類

| 主責 | 固定 Commit | 使用範圍 |
|---|---|---|
| 16 | `dfcd950a22bb94b88e107a355c36ea303869135d` | 目前 `codex/v53-parent-core`；共用核心、五份歷史 Migration、目前 Snapshot |
| 11 | `e2affb548bb427cd02d001d5bfd5761d7ecb4648` | Case／Guardian／Email／Consent 已提交模型；不假設後續 Adapter 已交付 |
| 07 | `b9f11330f756747ee6cd9e6fcdf0488ea520d773` | 醫療端已提交模型／輪次介面；不納入未 Commit Workflow Service／Store |

分類：A = 16 已提交模型、尚未進 Migration；B = 11 已提交模型、尚未合入 16／尚未進其 Snapshot，須 11 與三人確認納入；C = 尚未核定設計，**不在任何預設 Migration 白名單內**。A／B 都不代表已核准執行。

固定來源（11／07 路徑是 Git 物件內的路徑，不是目前 Worktree 已存在的檔案）：

- 16：[ParentQuestionnaireModelConfiguration](../EarlyInterventionCare.Api/Data/ParentQuestionnaireModelConfiguration.cs)、[CoreModelConfiguration](../EarlyInterventionCare.Api/Data/CoreModelConfiguration.cs)、[ApplicationDbContext](../EarlyInterventionCare.Api/Data/ApplicationDbContext.cs)、[CaseStatusHistoryConfiguration](../EarlyInterventionCare.Api/Data/CaseStatusHistoryConfiguration.cs)。
- 16：[Snapshot](../EarlyInterventionCare.Api/Data/Migrations/ApplicationDbContextModelSnapshot.cs)、[ParentAuthorSchemaProposal](../tests/ParentAuthorSchemaProposal.cs)、[ParentQuestionnaire gate](../tests/ParentQuestionnaire/Program.cs)、[Phase22 gate](../tests/QuestionnairePhase22Harness.cs)。
- 11：`Data/Entities/GuardianContact.cs`（同檔含 CaseGuardian／ParentBindingInvitation）、`Data/GuardianModelConfiguration.cs`、`Data/Entities/CaseRecord.cs`、`Data/IdentityModelConfiguration.cs`、`Models/Authentication/User.cs`、`ParentModule/ConsentModels.cs`，皆相對於上述固定 Commit 的 `EarlyInterventionCare.Api/`。
- 07：固定 Commit 的 `EarlyInterventionCare.Api/Data/ApplicationDbContext.cs`、`Data/CaseStatusHistoryConfiguration.cs`、`Data/Entities/QuestionnaireTask.cs`、`Services/Workflow/RoundCompletionEvaluator.cs`。
- 16 純輪次計算：[QuestionnaireRoundCompletionEvaluator](../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireRoundCompletionEvaluator.cs)；接線限制及歷史操作證據另見 [地端操作手冊](v53-16-local-operations.md)。

來源核對以 `git show <固定 Commit>:<路徑>` 為準；不要用目前主 Worktree、遠端最新分支或未提交成果替代。

## 2. 不可改寫的歷史與既有約束

以下五份 `.cs` 及各自 `.Designer.cs` 必須全部保留，順序不變：

1. `20261005152359_InitialSharedCore`
2. `20261005180101_AddAuditLogs`
3. `20261005184434_AddSubmissionReceiptWindow`
4. `20261006074424_IntegrateIdentityAndAuthorization`
5. `20261019131342_AddCaseStatusHistories`

三人前四份 Migration／Designer 的 Git blob 一致。07／11 固定版本僅有前四份 Snapshot；16 多第五份。07 雖已有 History Entity／Configuration／DbSet，但沒有第五份 Migration，不得再產生一份重複建立 History 表的 Migration。

目前 16 Snapshot 記錄 16 張表：cases、questionnaires、questionnaire_versions、case_questionnaires、questionnaire_drafts、questionnaire_responses、teacher_grants、teacher_grant_tasks、teacher_sessions、audit_logs、case_status_histories、users、organizations、roles、permissions、role_permissions。11／07 Snapshot 不含 History 表，也不含本文件 A／B 新模型。

不得拿其他分支 Snapshot 覆蓋目前檔案。本輪不修改 Snapshot；未來經核准產生增量時，才由固定共同模型正常更新 Snapshot，並審查差異，不手工補成假一致。

既有必保留約束（主責 16；Workflow 語意由 07／16 確認）：

| 表 | 必保留內容 |
|---|---|
| case_questionnaires | `assignment_round int unsigned NOT NULL`、`is_required tinyint(1) NOT NULL`、四種 TaskStatus；`uq_tasks_assignment(case_id, questionnaire_id, respondent_role, assignment_round)`；既有 Case／版本及角色範圍 FK |
| questionnaire_drafts | Task PK；`revision bigint unsigned NOT NULL` concurrency token；Task＋Version FK；Grant＋Task FK，均 Restrict |
| questionnaire_responses | `payload_hash binary(32) NOT NULL`；`uq_responses_task(task_id)`；`uq_responses_grant_key(submitted_by_grant_id, idempotency_key)`；既有 Task／Version／Grant 範圍 FK |
| case_status_histories | History PK；Case FK、ChangedByUser FK 皆 Restrict；`ix_case_status_histories_case_time(case_id, changed_at_utc, history_id)`、操作者索引；狀態與 TriggerSource enum |
| teacher_sessions | 既有 session／receipt window 與索引，不改教師授權或收據行為 |

不能因新增 parent author 而移除教師唯一鍵、改寫舊 PayloadHash 或放寬 FK。

## 3. A 白名單：16 已提交、未遷移的最小加法模型

主責：16。UUID 使用 `char(36)`／ascii／ascii_bin；以下五個新欄位均允許 NULL，**沒有資料庫預設值、沒有回填**。

| ID | 表／欄位 | 型別／NULL | 索引、FK 與刪除行為 |
|---|---|---|---|
| A01 | questionnaire_drafts.parent_author_user_id | `char(36) NULL` | `ix_drafts_parent_author` 非唯一；`fk_drafts_parent_author` → users.user_id，Restrict |
| A02 | questionnaire_responses.parent_author_user_id | `char(36) NULL` | `uq_responses_parent_key(parent_author_user_id, idempotency_key)` 唯一；`fk_responses_parent_author` → users.user_id，Restrict |
| A03 | questionnaire_drafts.respondent_relationship | `varchar(32) NULL` | 無新增索引、FK 或預設值 |
| A04 | questionnaire_responses.respondent_relationship | `varchar(32) NULL` | 無新增索引、FK 或預設值 |
| A05 | questionnaire_responses.hash_version | `varchar(64) NULL` | 無新增索引、FK、binary collation 或預設值 |

靜態預期是 5 個 AddColumn、2 個 CreateIndex、2 個 AddForeignKey，共 9 項加法操作；這不是已執行 EF differ 或已生成 Migration 的結果。未來產物若超出此集合，停止審查原因，不自動接受。

`uq_responses_parent_key` 不含 Task、HashVersion 或 Relationship；同作者同 Key 不能藉換 Task／版本另建第二列。保留現有每 Task 唯一。允許舊列／教師列 author NULL 不等於允許新版家長未保存作者；應用程式仍須驗證可信作者。

HashVersion 只由新版成功首次提交明確保存 `earlycare.parent-submit.canonical/v1`，比對使用應用程式 ordinal。DB 欄位採現有表預設 collation，不能依它判定版本相等；額外 binary collation 設定屬 C 候選。

## 4. B 清單：11 已提交、待共同納入的模型

主責：11 提供語意與完整 Entity／Mapping；16 審查並整合；三人共同核准。以下不是可直接套用的 DDL 白名單。

11 DbContext 有 Guardian、CaseGuardian、Invitation DbSet；Consent 雖無明確 DbSet，已有 IEntityTypeConfiguration 並由 ApplyConfigurationsFromAssembly 掃描，不能漏算模型。三人 CoreModelConfiguration 的 Git blob 相同；擴充應保留共同核心並整合額外 Configuration，不以其他分支 DbContext／Snapshot 覆蓋。

標記「慣例」的 MySQL 型別為 CLR／EF／Pomelo 對應推定，尚未在合併後 EF 模型產物核實；DDL 產生前必須固定 Provider 版本並核對。`NN` = NOT NULL，`N` = NULL。除計算欄位外，這些增量 Mapping 沒有明訂 DB 預設值；CLR 的空字串／0／狀態初值不視為資料修復或 DB default。

共通 Mapping：GuardianMapping 將欄位轉 snake_case，Guid／Guid? 為 char(36) ascii/ascii_bin，DateTime／DateTime? 為 datetime(6)。其餘 string 長度依 Mapping；uint → int unsigned、bool → tinyint(1) 屬慣例。未明訂索引／FK 名稱者，名稱待合併 EF 產物核對，不能假稱已固定。

### B01 cases 擴充

| 欄位 | 型別／NULL | 約束 |
|---|---|---|
| organization_id | char(36) ascii/ascii_bin，N | 非唯一索引；FK → organizations.organization_id，Restrict；名稱未明訂 |
| revision | CLR uint，慣例 int unsigned，NN | concurrency token，無新增唯一鍵 |
| national_id | varchar(10)，NN | 無新增索引／FK |
| address | varchar(300)，NN | 無新增索引／FK |

舊 Case 的院所不可推定；必要個資是否應蒐集、舊資料如何滿足新 NN 欄位尚待確認。**未有合法舊資料方案前，B01 不得直接對含資料的表套用**，不可因 CLR 初值而自動填空字串或 0。分階段 nullable／驗證／收緊的策略也需另行核准。

### B02 users 擴充

| 欄位 | 型別／NULL | 約束 |
|---|---|---|
| phone_verified_at_utc | datetime(6)，N | 無新 FK／索引 |
| email | varchar(254)，N | 無新唯一鍵 |
| normalized_email | varchar(254)，N，utf8mb4_bin | `ix_users_normalized_email` 非唯一 |
| email_verified_at_utc | datetime(6)，N | 無新 FK／索引 |

不可從既有 Phone、Guardian Email 或邀請資料推定已驗證 Email。NormalizedEmail 索引不是唯一，不能未經審查改成唯一；遇到同 Email 的模糊帳號，不能自動合併或選一個帳號放行。

### B03 guardians（新表）

| 欄位群 | 型別／NULL |
|---|---|
| guardian_id（PK）、organization_id | UUID char(36)，NN |
| parent_user_id | UUID char(36)，N |
| display_name／email／phone | 慣例 varchar(100)／varchar(254)／varchar(20)，NN |
| revision | CLR uint／慣例 int unsigned，NN；concurrency token |
| created_at_utc／updated_at_utc | datetime(6)，NN |

約束：`uq_guardians_organization_phone(organization_id, phone)` 唯一；`(organization_id, parent_user_id)` 非唯一索引（名稱未明訂）；organization_id → organizations、parent_user_id → users，均 Restrict。此唯一鍵的院所／手機語意須 11 確認，不自行決定共用電話政策。

### B04 case_guardians（新表）

| 欄位群 | 型別／NULL |
|---|---|
| case_id、guardian_id | UUID char(36)，NN；複合 PK |
| relationship／link_status | 慣例 varchar(32)／varchar(20)，NN |
| is_primary_contact | CLR bool／慣例 tinyint(1)，NN |
| created_at_utc | datetime(6)，NN |
| bound_at_utc／revoked_at_utc | datetime(6)，N |
| active_case_id | UUID char(36)，N；stored computed，非手填 |

計算式為 `CASE WHEN link_status = 'ACTIVE' AND is_primary_contact = 1 THEN case_id ELSE NULL END`；`uq_case_guardians_active_case(active_case_id)` 唯一。Case、Guardian FK 均 Restrict。這只限制同 Case 有效主要聯絡人，不代表只能有一個 Guardian，也不能當成資格世代。

計算欄位型別／collation、MySQL 版本與唯一索引相容性必須在隔離實例驗證。CaseGuardian.relationship 是業務關係，不可拿來補舊 Response.respondent_relationship。

### B05 parent_binding_invitations（新表）

| 欄位群 | 型別／NULL |
|---|---|
| invitation_id（PK）、case_id、guardian_id、issued_by_user_id | UUID char(36)，NN |
| consumed_by_user_id | UUID char(36)，N |
| code_hash／target_phone | 慣例 varchar(64)／varchar(20)，NN |
| target_email_normalized | 慣例 varchar(254)，N，utf8mb4_bin |
| created_at_utc | datetime(6)，NN |
| expires_at_utc／consumed_at_utc／revoked_at_utc | datetime(6)，N |

CodeHash 唯一索引（名稱未明訂）；`(case_id, guardian_id)` FK → case_guardians 複合 PK，issued_by／consumed_by → users，均 Restrict。FK 所需索引由 EF 產物核對，不能省略。舊電話邀請的 Email 欄位保持 NULL，不推定驗證狀態。

### B06 consent_document_versions（已提交模型提案的新表）

| 欄位群 | 型別／NULL |
|---|---|
| document_version_id（PK） | UUID char(36)，NN |
| document_code／version | 慣例 varchar(64)／varchar(32)，NN |
| title／content | 慣例 varchar(200)／明訂 longtext，NN |
| content_hash／scope_code | 慣例 varchar(64)／varchar(32)，NN |
| approval_status／approval_reference | 慣例 varchar(16)／varchar(200)，NN |
| approved_at_utc／effective_from_utc／retired_at_utc | datetime(6)，N |

`(document_code, version)` 唯一；沒有額外 FK。來源沒有供應核准院方本文／Migration／seed。CLR DRAFT／UNCONFIRMED 不是核准政策；不以合成內容填入正式同意文件。

### B07 parent_consents（已提交模型提案的新表）

| 欄位群 | 型別／NULL |
|---|---|
| consent_id（PK）、document_version_id、actor_user_id、organization_id、request_id | UUID char(36)，NN |
| case_id | UUID char(36)，N |
| scope_key／document_content_hash | 慣例 varchar(40)／varchar(64)，NN |
| data_use_decision／teacher_participation | 慣例 varchar(16)，NN |
| recorded_at_utc／revoked_at_utc | datetime(6)，NN／N |

唯一鍵：`(actor_user_id, request_id)`；`(actor_user_id, organization_id, document_version_id, scope_key)`，名稱未明訂。ScopeKey 用於明確表示 ACCOUNT／Case 範圍，不能改成只靠 nullable CaseId 的唯一鍵。

FK：文件 → consent_document_versions、作者 → users、院所 → organizations、可空 Case → cases，均 Restrict。所需 FK 索引由 EF 產物核對。撤銷後同版本／Scope 如何再次記錄及保留歷史，與第二個唯一鍵可能互相限制，須 11 先確認，不自動刪列、改 Key 或放寬唯一鍵。

B03～B07 的個別 FK 僅保證目標列存在，**不充分保證所有列同院所或使用者具資格**；跨院所與 Consent Scope 仍須真正授權 Adapter 驗證，不能用建表成功代替。

## 5. C 候選設計：明確排除於預設白名單

| 候選 | 現有事實／缺口 | 決策者 |
|---|---|---|
| 綁定資格世代／anchor／新的 Binding ID | CaseGuardian 複合 PK、綁定撤銷時間不能單獨證明每次重綁新世代；GuardianContact.Revision 是資料 concurrency，不能直接替代 | 11／16 |
| EligibilityStamp 儲存欄位或算法 | 現有為伺服器內部 expectation；不是授權 Token，不假設必須持久化 | 11／16 |
| Consent 世代、撤銷／重同意歷史結構 | 舊記錄、唯一鍵與完整寫入端協定待定 | 11／16；政策由院方確認 |
| 有效輪次表、CurrentRound、IsEffective 等 | 只有 AssignmentRound；兩個 evaluator 都要求呼叫者明確提供 Case／輪次，沒有自動挑最大輪次的授權 | 07／16 |
| 取消／重複指派後重開與完成度規則 | 既有 Task 唯一鍵及取消政策不可自行改動；未 Commit Workflow 不算交付 | 07／16；業務政策共同確認 |
| HashVersion 欄位 binary collation | 目前 ordinal 應用程式比對；如需欄位 collation 增量，需單獨審查，不改全域 | 16／三人 |
| TeacherGrant.ConsentReferenceId 的正式 FK | 不直接指向新 Consent 表；教師參與政策與資料生命週期尚待確認 | 11／16／院方 |

任何 C 項若未取得明確決策，Migration 出現該欄位／表／約束即停止，不以方便測試納入。

## 6. 舊資料相容與禁止回填

主責：16 執行證據；11 確認 Guardian／Consent 語意；07 確認既有醫療查閱需求；政策三人／院方核准。

- 舊作者、舊草稿／教師關係、HashVersion 的未知狀態保持 NULL。不得填 FATHER／MOTHER、現在的 Guardian 關係或推測的作者。
- 不重算舊 PayloadHash，不用 NULL 版本推定舊算法；新 Command 對未知版本重送回 `IDEMPOTENCY_VERSION_UNSUPPORTED`／409。已授權 GetReceipt 與成功重送是不同操作，不用遷移「修好」歷史重送。
- 保留 ResponseId、TaskId、Key、原 Hash bytes、原始答案、提交時間及狀態。Migration 本身不改寫草稿 Revision、教師 Grant／Session 或 Case History。
- Consent 原文、版本、ContentHash、決定、Scope、撤銷時間與 RequestId 原樣保留；不存在證據不建立「已同意」記錄，也不補批准／驗證時間。
- 如果未來盤點發現真實 Schema 已有人手工增欄、部分套用或已有 B 表，先比對資料與歷史，停止自動套用，不靠手改 __EFMigrationsHistory 假裝一致。
- 必填新欄位、未知院所、模糊 Email／電話、重複 Key／Consent 不自動清理、不刪除、不合併；將受影響列數與匿名分類交主責決策。不得輸出真實個資或憑證。

## 7. 合成情境與預期結果（全部待執行）

只用合成 Org-A／Org-B、User-A／User-B、Case-A、Task-A／Task-B、非臨床單選問卷與隨機測試 Hash；不是院方資料或政策。未完成共同模型前可先準備資料格式與斷言；真正資料庫結論須核准後取得。

| ID | 合成情境 | 預期／停止規則 | 主責 |
|---|---|---|---|
| T01 | 舊教師／未知作者 Response 與 Draft | 新欄 NULL，舊 Hash bytes、內容、筆數及教師重送行為不變 | 16 |
| T02 | 同作者同 Key、不同 Task | parent 唯一鍵拒絕第二 Response；既有每 Task 唯一仍成立；不可自動換 Key | 16 |
| T03 | 不同作者同 Key、各自不同 Task | parent 唯一鍵不應單憑 Key 跨作者拒絕；仍須各自真實授權 | 16 |
| T04 | author NULL 的舊列／教師列，Key 相同、Task 不同 | 預期符合 MySQL nullable unique 語意；舊 teacher key 約束亦須滿足；實際結果須驗證 | 16 |
| T05 | 作者指向不存在 User | FK 拒絕；不得關閉 foreign_key_checks；無部分寫入 | 16 |
| T06 | 刪除仍被 Draft／Response 引用的 User | Restrict 拒絕，不 cascade 刪原始答案 | 16 |
| T07 | 新家長合法提交、重送、更改關係／答案、stale Revision | 首次保存完整 v1 元資料；相同內容回原收據；變更內容／Revision 分別依契約拒絕 | 16 |
| T08 | NULL／未知／大小寫不同 HashVersion，舊關係 NULL | 新 Command 安全拒絕未知版本，不猜測舊 Hash；歷史查閱另經授權 | 16 |
| T09 | 同院所重複 Guardian Phone／同 Case 兩個 ACTIVE primary | 依 B 唯一鍵拒絕；多個非 primary 是否允許依來源模型及政策另驗 | 11／16 |
| T10 | 孤兒 CaseGuardian、Invitation、Consent FK | FK 拒絕；nullable 的 FK 真 NULL 仍可存在，不用虛構 ID 代替 | 11／16 |
| T11 | 同 DocumentCode＋Version、同作者＋RequestId、同 Consent Scope 重複 | 各自唯一鍵拒絕；撤銷後重同意結果待決，不能預填 PASS | 11／16 |
| T12 | 舊 Case 無院所／必要新欄未知；舊邀請無 Email | 不推定／不填補；B01 舊資料策略未核准即停止；Email 不變成已驗證 | 11 |
| T13 | User.normalized_email 重複 | DB 非唯一索引不應被偷改成唯一；模糊登入安全拒絕另由 11 業務測試驗證 | 11／16 |
| T14 | 目標列存在但跨院所拼接 FK | 不把 FK 通過當授權通過；Case／Guardian／Consent Adapter 必須拒絕 | 11／16 |
| T15 | 失敗寫入、Commit 失敗、並行相同 Key、撤銷並行 | 驗證真正 DB 回滾／唯一鍵／鎖；Fake PASS 不算此案例通過；協定未固定即 BLOCKED | 11／16 |
| T16 | 同 Case 多輪、取消必要 Task、零必要 Task | 不猜有效輪次、不自動結案；以 07／16 固定契約驗證 Task／History | 07／16 |
| T17 | 備份後獨立還原 | 表／筆數／逐列 Hash／歷史相符；還原庫不可被 API 誤連 | 16 |

T02～T06／T09～T11 應分別記錄 DB 約束錯誤與應用程式錯誤碼；不得將兩者混為同一層驗收。DDL 前若既有合成基線已含重複／孤兒資料，預期是預檢阻擋，不自動修復或等待 Migration 靜默填值。

## 8. Schema gate 的合法更新條件

1. Phase22 一般模式要求 `!HasPendingModelChanges()`；目前因 A 模型尚未遷移而失敗是正常部署阻擋，不是應移除的檢查。
2. ParentQuestionnaire 先驗五份 Migration 與存在 pending changes，再用 ParentAuthorSchemaProposal 限定原六項。新增 Relationship／HashVersion 後，舊六項預期不足；不可直接改成「任意有差異就 PASS」。
3. 三人先核准固定 A／B 範圍。16 才可另批更新「精確提案測試」，檢查每項表、欄位型別、NULL、預設值、索引與 FK；白名單外任何新增／刪除／修改仍 FAIL。
4. 經核准新增正式 Migration 後，Migration 數量及提案模式必須按固定歷史更新；不能永久要求恰好五份或 pending=true。另保留歷史內容校驗、部署 pending=false 與升級證據三種檢查。
5. 套件中業務測試與部署 gate 可另批設計分開報告，但失敗 gate 必須保持 FAIL／BLOCKED，不以跳過或吞例外回報整套成功；既有測試本輪一律不修改。

## 9. 未來 Migration 前置條件與隔離驗收清單

以下都是未執行清單，需逐項另行核准。地端只考慮獨立 MySQL 8.0.46、127.0.0.1:33326、專用 datadir／tmp／logs 與核准帳號；不使用 MySQL80／3306、不對外開放。具體設定沿用現有規劃，不由本文件啟用。

### 9.1 套用前（16 主責；模型範圍三人共同）

- [ ] 固定共同整合 Commit、SDK／EF Core／Pomelo 版本及模型白名單。16 目前 net8.0、EF Design 8.0.13、Pomelo 8.0.3；不同團隊模型合併後重核 Provider 實際版本。
- [ ] 11 確認 B 欄位／索引／Consent 舊資料策略；07 確認輪次／Workflow 是否確有 Schema 需求；C 未決一律排除。
- [ ] 確認 127.0.0.1:33326 的實例身分、獨立路徑、DB 名稱與帳號作用域；來源和還原目的地皆不得是 MySQL80／3306。Runtime／Migration／備份還原帳號權限分開審查。
- [ ] 唯讀盤點實際 Migration history、Schema、索引／FK、表筆數及匿名衝突計數；先確認目標，不能把本文件的 Git 歷史當成 DB 已套用歷史。
- [ ] 五份歷史 Migration／Designer 記錄 Git blob 或雜湊；目前 Snapshot 留存基線。確認不含重建 History、改寫 seed、Drop 或非白名單變更。
- [ ] 保全合成基線：逐表筆數；逐 ResponseId 的 PayloadHash 原始 bytes、Task／Key／提交時間；Draft Revision；NULL 分布；Consent 原始文件／Hash／撤銷資訊。不能只比較總筆數。
- [ ] 停止測試寫入，取得一致備份與必要歷史／操作證據，限制備份存取；先證明備份可獨立還原。備份檔不納 Git，不含真實醫療資料。
- [ ] 另批產生並審查增量 Migration／Designer／Snapshot／SQL，確認預期欄位沒有 DB default、資料 UPDATE／修復／自動回填；NN 新欄未有舊資料方案即阻擋。

### 9.2 獨立還原庫與隔離庫驗收（16 主責；相關模組共同）

- [ ] 優先於核准的獨立還原庫驗證增量，再於隔離測試庫重現；API 停止，無正式網路入口。
- [ ] 核對執行目標及實際 DDL／history；前五份保持原樣，只增加經核准的新版本，不手工改 history。
- [ ] 記錄每一步時間、狀態、約束／索引與錯誤；MySQL DDL 不假設可由應用程式交易整批回滾。
- [ ] 比對前後逐表筆數、逐列 Hash bytes／原始內容、ID／時間、NULL 與 Revision；新表的初始化內容也必須有核准來源，不 seed 同意或資格。
- [ ] 實際跑 T01～T17 中已具備前置條件的案例；Schema、業務邏輯、真實並行、HTTP 分開回報。未知政策案例標 BLOCKED。
- [ ] 驗證實際 FK／唯一鍵／計算欄位／Collation、既有教師與新家長流程；確認資料與 history 重啟後仍一致。
- [ ] 重新備份、還原至另一獨立目的地並核對上述資料。既有 API guard 若不接受還原庫，不為了驗收放寬 guard；使用另行核准的驗證方式。

### 9.3 停止、Rollback 與 Recovery（16 協調；三人共同決策）

以下任一情況立刻停止後續步驟，保留錯誤、時間、Commit、Migration ID、匿名筆數與目標資訊，不自動重試／修復：目標或 Port 不符、備份無法還原、history 漂移／缺第五份、白名單外 DDL、重複唯一鍵／孤兒 FK、預期外 default／回填、Hash 或筆數改變、NN 舊資料未知、跨作者／跨院所洩漏、Adapter／鎖序未固定、部分 DDL 成功後失敗。

- [ ] 不關閉 FK 檢查、不改 Key、不手改 history、不直接清理 datadir 或強制處理 MySQL80。
- [ ] 分辨「應用程式 DML 回滾」與「Migration DDL 復原」。記錄實際已套用哪些操作，不把例外等同於什麼都沒改。
- [ ] 若尚未有新欄寫入，評估回退應用程式並保留加法 Schema；仍需驗證舊程式讀取相容，不保證任何版本均可直接降版。
- [ ] 已保存新作者／關係／版本／Consent 後，禁止直接 Down 丟欄／刪表；優先審查前向修正，或核准後從備份還原至獨立庫核對，不覆蓋來源庫。
- [ ] 只有確認新資料損失範圍、可重建性、備份可用與後續入口版本後，才核准復原操作。停止期間保持新版提交預設拒絕。

## 10. 分工、核准與下一個可獨立工作

| 工作／決策 | 主責／確認者 | 執行依賴 |
|---|---|---|
| A 精確操作白名單、歷史校驗與差異證據 | 16 | 可先離線準備；本輪不新增測試 |
| B 模型語意、舊 Case／Email／Consent 方案 | 11，16 審查 | 11 固定交付與三人核准 |
| 有效輪次／取消／Workflow History | 07／16 | 07 已提交固定版本與業務決策；未提交不算完成 |
| 資格世代、fresh read、共同鎖序與所有撤銷寫入端 | 11／16 | 共同技術協定，不可由 Fake 代替 |
| DbContext／Snapshot／增量 Migration 統整 | 16 | A／B 核定後，另行取得產生／套用授權 |
| 固定整合版本與合併協調 | 07；三人共同確認 | 不以任一分支整檔覆蓋其他模型 |
| 隔離主機、最小權限、備份還原及真實並行證據 | 16；三人模組驗收 | 目標實例、操作與復原方案逐項核准 |
| 欄位是否必要、保存年限／刪除／撤回後保留方式 | 老師／院方，三人記錄 | 不由 Migration 或合成 fixture 決定 |
| Consent 正文、Scope、正式啟用與教師撤回後處理 | 院方；11／16落實 | 未核准不 seed／啟用、不放寬授權 |
| 醫療個案可見範圍、輪次／重填／結案規則 | 老師／院方；07／16落實 | 不自動擴大同院可見或自動結案 |
| 備份保留年限、加密／存取與允許的資料損失／復原時間 | 老師／院方，16 提案 | 不把測試主機設定當正式維運標準 |

16 下一批可獨立提案：離線精確差異斷言、五份歷史校驗清單、合成衝突 fixture 與前後資料比對格式。這些準備不等於准許產生 Migration、連 DB 或改現有 gate；本輪只新增本 Markdown。
