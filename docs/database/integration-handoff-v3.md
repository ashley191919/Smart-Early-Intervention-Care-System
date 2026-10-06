# MySQL 資料庫建置清單與組員 AI 整合比對

日期：2026-10-06。基準：feature/teacher-authorization，已推送提交 097af55。此文件依已提交的 EF 模型、三份 migration 與 seed 產生；本機功能驗收依使用者回報，本次未直接讀取 live 資料庫內容。實際列數及組員開發狀態未知。

本文件及配套 SQL 用於結構比對，不是另一套 schema 管理流程，也不代表要刪除任一組員資料庫。配套 current-v3-generated-reference.sql 由 EF migrations 0 → 第三份產生，僅供閱讀；不要在已有資料表的資料庫直接執行。

## 1. 環境與管理方式

| 項目 | 本專案現況 |
| --- | --- |
| MySQL | 使用者本機 MySQL 8.0.46，localhost / 127.0.0.1，通常 port 3306 |
| 開發資料庫 | earlycare_dev；尚無正式共用資料庫 |
| 專案 MySQL 帳號 | earlycare_dev_user；是資料庫連線帳號，不是家長／醫師的應用帳號 |
| Backend | ASP.NET Core / .NET 8；Microsoft.EntityFrameworkCore.Design 8.0.13；Pomelo.EntityFrameworkCore.MySql 8.0.3 |
| DbContext | EarlyInterventionCare.Api/Data/ApplicationDbContext.cs |
| 連線設定 | ConnectionStrings:DefaultConnection，以各機 User Secrets 或環境變數提供；不分享密碼、不提交 secrets.json |
| Schema 管理者 | 由 Ashley／目前使用者統整 EF migration；各模組提供需求及對接接口 |
| 建表方式 | EF Core migrations；網站啟動不自動 migrate 或 seed；沒有用 EnsureCreated 建正式表 |
| 業務資料表 | 10 張，另有 EF 自管 __EFMigrationsHistory（部分 Windows 實例顯示小寫） |

不同電腦的 localhost 是不同 MySQL 實例，即使都叫 earlycare_dev 也不會互撞。真正的衝突通常發生於合併程式／migration，或同一共用 schema 被兩套建表流程管理。請分別比較「連線環境」、「資料結構」、「EF migration 歷史」，不要只比較 schema 名稱。

## 2. 已有 migration：不得當作未套用草案

| 順序 | 完整 migration ID | 內容 |
| --- | --- | --- |
| 1 | 20261005152359_InitialSharedCore | 九張核心表、主鍵、複合外鍵、唯一鍵及索引 |
| 2 | 20261005180101_AddAuditLogs | 新增 audit_logs 及查詢索引 |
| 3 | 20261005184434_AddSubmissionReceiptWindow | teacher_sessions 新增 nullable datetime(6) receipt_expires_at_utc |

Migration 與 snapshot 位於 EarlyInterventionCare.Api/Data/Migrations。三份已進入已驗收／已推送節點；不能因組員也有 migration 就刪除、改寫已套用歷史，或手動刪除 __EFMigrationsHistory 紀錄。若對方歷史也已套用，需提供整併／升級方案；未套用的重複 migration 才能列入移除候選，由使用者統整。

## 3. 資料表用途與使用狀況

| 表 | 用途 | 是否已有功能使用 |
| --- | --- | --- |
| cases | 共用幼兒個案 | 有虛構 seed，教師讀取必要摘要；正式醫療端建立 API 待整合 |
| questionnaires | 問卷種類 | 有 SNAP_IV 種類 |
| questionnaire_versions | 角色版問卷的固定版本／題目 snapshot | 有教師 SNAP-IV 1.0.0、26 題；未正式啟用計分 |
| case_questionnaires | 個案 × 問卷版本 × 填答角色 × 輪次的任務 | 教師任務查詢、提交使用 |
| teacher_grants | 教師授權、碼雜湊及 ACTIVE／USED／REVOKED | 建立、驗證、撤銷、提交失效使用 |
| teacher_grant_tasks | 授權可填哪些任務及其個案／角色範圍 | 查詢與提交範圍驗證使用 |
| teacher_sessions | 教師碼驗證後的會話與短期收據通道 | 登入／登出／撤銷／重試使用，不是一般登入 sessions |
| questionnaire_drafts | 每任務一份草稿、revision | 已建表；後端草稿 API 未實作，現有前端只暫存本頁 |
| questionnaire_responses | 每任務一份不可修改的原始提交答案 | SNAP-IV 保存、去重及完成狀態使用 |
| audit_logs | 不含答案／憑證的操作 metadata | 教師事件持久化使用；一般登入／醫療端事件尚未串接 |

不要將「API 尚未使用」直接判斷為「冗餘表」。尤其 questionnaire_drafts 是已確認需求的預留表。

## 4. 通用型別與共同規則

- 主鍵與外部 ID 以 UUID / C# Guid 對接，MySQL CHAR(36)、ascii_bin；不以病歷號、姓名、手機或 case_code 當主鍵。session_hash 例外，以 BINARY(32) 作主鍵。
- snake_case 表／欄位、camelCase API、PascalCase C#。文字表採 utf8mb4 / utf8mb4_unicode_ci；特定代碼欄位及 UUID 採 ascii / ascii_bin。
- 時間 DATETIME(6) 存 UTC；畫面顯示臺灣時間。birthday / filled_on 類日期欄位為 DATE，不套時區。
- 布林使用 tinyint(1)，不用 boolean(1)。答案與問卷定義用 JSON。
- 下方型別／NULL／DEFAULT 直接摘自 migration；實際完整 DDL 以配套產生 SQL 為準。
- 所有業務外鍵 ON DELETE RESTRICT；不串連刪除答案與證據。audit_logs 沒有外鍵，也沒有修改／刪除 HTTP API。

## 5. 各表完整欄位、約束與索引

### cases

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `case_id` | `char(36) NOT NULL` |
| `case_code` | `varchar(64) NOT NULL` |
| `child_name` | `varchar(100) NOT NULL` |
| `birth_date` | `date NOT NULL` |
| `sex` | `ENUM('MALE', 'FEMALE', 'UNKNOWN') NOT NULL` |
| `case_status` | `ENUM('NEW', 'TO_CONTACT', 'APPOINTED', 'WAITING_FORM', 'FORM_COMPLETED', 'COMPLETED') NOT NULL DEFAULT 'NEW'` |
| `created_at_utc` | `datetime(6) NOT NULL` |
| `updated_at_utc` | `datetime(6) NOT NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_cases` PRIMARY KEY (`case_id`)`

索引（包含唯一索引）：

- `CREATE UNIQUE INDEX `uq_cases_code` ON `cases` (`case_code`)`

### questionnaires

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `questionnaire_id` | `char(36) NOT NULL` |
| `questionnaire_code` | `varchar(32) NOT NULL` |
| `title` | `varchar(200) NOT NULL` |
| `created_at_utc` | `datetime(6) NOT NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_questionnaires` PRIMARY KEY (`questionnaire_id`)`

索引（包含唯一索引）：

- `CREATE UNIQUE INDEX `uq_questionnaires_code` ON `questionnaires` (`questionnaire_code`)`

### questionnaire_versions

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `questionnaire_version_id` | `char(36) NOT NULL` |
| `questionnaire_id` | `char(36) NOT NULL` |
| `respondent_role` | `ENUM('PARENT', 'TEACHER') NOT NULL` |
| `version_number` | `varchar(32) NOT NULL` |
| `version_status` | `ENUM('DRAFT', 'PUBLISHED', 'RETIRED') NOT NULL DEFAULT 'DRAFT'` |
| `definition_snapshot` | `json NOT NULL` |
| `scoring_definition` | `json NULL` |
| `created_at_utc` | `datetime(6) NOT NULL` |
| `published_at_utc` | `datetime(6) NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_questionnaire_versions` PRIMARY KEY (`questionnaire_version_id`)`
- `CONSTRAINT `uq_versions_type` UNIQUE (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`)`
- `CONSTRAINT `fk_versions_questionnaire` FOREIGN KEY (`questionnaire_id`) REFERENCES `questionnaires` (`questionnaire_id`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE UNIQUE INDEX `uq_versions_number` ON `questionnaire_versions` (`questionnaire_id`, `respondent_role`, `version_number`)`

### case_questionnaires

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `task_id` | `char(36) NOT NULL` |
| `case_id` | `char(36) NOT NULL` |
| `questionnaire_id` | `char(36) NOT NULL` |
| `questionnaire_version_id` | `char(36) NOT NULL` |
| `respondent_role` | `ENUM('PARENT', 'TEACHER') NOT NULL` |
| `assignment_round` | `int unsigned NOT NULL DEFAULT 1` |
| `is_required` | `tinyint(1) NOT NULL DEFAULT TRUE` |
| `task_status` | `ENUM('PENDING', 'IN_PROGRESS', 'SUBMITTED', 'CANCELLED') NOT NULL DEFAULT 'PENDING'` |
| `created_at_utc` | `datetime(6) NOT NULL` |
| `updated_at_utc` | `datetime(6) NOT NULL` |
| `submitted_at_utc` | `datetime(6) NULL` |
| `cancelled_at_utc` | `datetime(6) NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_case_questionnaires` PRIMARY KEY (`task_id`)`
- `CONSTRAINT `uq_tasks_scope` UNIQUE (`task_id`, `case_id`, `respondent_role`)`
- `CONSTRAINT `uq_tasks_version` UNIQUE (`task_id`, `questionnaire_version_id`)`
- `CONSTRAINT `fk_tasks_case` FOREIGN KEY (`case_id`) REFERENCES `cases` (`case_id`) ON DELETE RESTRICT`
- `CONSTRAINT `fk_tasks_version_type` FOREIGN KEY (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`) REFERENCES `questionnaire_versions` (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE INDEX `IX_case_questionnaires_questionnaire_version_id_questionnaire_i~` ON `case_questionnaires` (`questionnaire_version_id`, `questionnaire_id`, `respondent_role`)`
- `CREATE UNIQUE INDEX `uq_tasks_assignment` ON `case_questionnaires` (`case_id`, `questionnaire_id`, `respondent_role`, `assignment_round`)`

### teacher_grants

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `grant_id` | `char(36) NOT NULL` |
| `case_id` | `char(36) NOT NULL` |
| `respondent_role` | `ENUM('PARENT', 'TEACHER') NOT NULL DEFAULT 'TEACHER'` |
| `code_hash` | `binary(32) NOT NULL` |
| `grant_status` | `ENUM('ACTIVE', 'USED', 'REVOKED') NOT NULL DEFAULT 'ACTIVE'` |
| `issued_by_user_id` | `char(36) NULL` |
| `consent_reference_id` | `char(36) NULL` |
| `is_development` | `tinyint(1) NOT NULL DEFAULT FALSE` |
| `created_at_utc` | `datetime(6) NOT NULL` |
| `used_at_utc` | `datetime(6) NULL` |
| `revoked_at_utc` | `datetime(6) NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_teacher_grants` PRIMARY KEY (`grant_id`)`
- `CONSTRAINT `uq_grants_scope` UNIQUE (`grant_id`, `case_id`, `respondent_role`)`
- `CONSTRAINT `fk_grants_case` FOREIGN KEY (`case_id`) REFERENCES `cases` (`case_id`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE INDEX `ix_grants_case_status` ON `teacher_grants` (`case_id`, `grant_status`)`
- `CREATE UNIQUE INDEX `uq_grants_code_hash` ON `teacher_grants` (`code_hash`)`

### teacher_grant_tasks

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `grant_id` | `char(36) NOT NULL` |
| `task_id` | `char(36) NOT NULL` |
| `case_id` | `char(36) NOT NULL` |
| `respondent_role` | `ENUM('PARENT', 'TEACHER') NOT NULL DEFAULT 'TEACHER'` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_teacher_grant_tasks` PRIMARY KEY (`grant_id`, `task_id`)`
- `CONSTRAINT `fk_grant_tasks_grant_scope` FOREIGN KEY (`grant_id`, `case_id`, `respondent_role`) REFERENCES `teacher_grants` (`grant_id`, `case_id`, `respondent_role`) ON DELETE RESTRICT`
- `CONSTRAINT `fk_grant_tasks_task_scope` FOREIGN KEY (`task_id`, `case_id`, `respondent_role`) REFERENCES `case_questionnaires` (`task_id`, `case_id`, `respondent_role`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE INDEX `ix_grant_tasks_task_scope` ON `teacher_grant_tasks` (`task_id`, `case_id`, `respondent_role`)`
- `CREATE INDEX `IX_teacher_grant_tasks_grant_id_case_id_respondent_role` ON `teacher_grant_tasks` (`grant_id`, `case_id`, `respondent_role`)`

### teacher_sessions

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `session_hash` | `binary(32) NOT NULL` |
| `grant_id` | `char(36) NOT NULL` |
| `session_status` | `ENUM('ACTIVE', 'REVOKED') NOT NULL DEFAULT 'ACTIVE'` |
| `created_at_utc` | `datetime(6) NOT NULL` |
| `last_seen_at_utc` | `datetime(6) NOT NULL` |
| `session_expires_at_utc` | `datetime(6) NOT NULL` |
| `revoked_at_utc` | `datetime(6) NULL` |
| `receipt_expires_at_utc` | `datetime(6) NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_teacher_sessions` PRIMARY KEY (`session_hash`)`
- `CONSTRAINT `fk_sessions_grant` FOREIGN KEY (`grant_id`) REFERENCES `teacher_grants` (`grant_id`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE INDEX `ix_sessions_grant_status` ON `teacher_sessions` (`grant_id`, `session_status`)`

### questionnaire_drafts

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `task_id` | `char(36) NOT NULL` |
| `questionnaire_version_id` | `char(36) NOT NULL` |
| `respondent_name` | `varchar(100) NULL` |
| `filled_on` | `date NULL` |
| `answers_json` | `json NOT NULL` |
| `observation` | `text NULL` |
| `revision` | `bigint unsigned NOT NULL` |
| `last_saved_by_grant_id` | `char(36) NULL` |
| `updated_at_utc` | `datetime(6) NOT NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_questionnaire_drafts` PRIMARY KEY (`task_id`)`
- `CONSTRAINT `fk_drafts_grant_scope` FOREIGN KEY (`last_saved_by_grant_id`, `task_id`) REFERENCES `teacher_grant_tasks` (`grant_id`, `task_id`) ON DELETE RESTRICT`
- `CONSTRAINT `fk_drafts_task_version` FOREIGN KEY (`task_id`, `questionnaire_version_id`) REFERENCES `case_questionnaires` (`task_id`, `questionnaire_version_id`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE INDEX `IX_questionnaire_drafts_last_saved_by_grant_id_task_id` ON `questionnaire_drafts` (`last_saved_by_grant_id`, `task_id`)`
- `CREATE INDEX `IX_questionnaire_drafts_task_id_questionnaire_version_id` ON `questionnaire_drafts` (`task_id`, `questionnaire_version_id`)`

### questionnaire_responses

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `response_id` | `char(36) NOT NULL` |
| `task_id` | `char(36) NOT NULL` |
| `questionnaire_version_id` | `char(36) NOT NULL` |
| `respondent_name` | `varchar(100) NOT NULL` |
| `filled_on` | `date NOT NULL` |
| `answers_json` | `json NOT NULL` |
| `observation` | `text NULL` |
| `submitted_by_grant_id` | `char(36) NULL` |
| `idempotency_key` | `char(36) NOT NULL` |
| `payload_hash` | `binary(32) NOT NULL` |
| `submitted_at_utc` | `datetime(6) NOT NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_questionnaire_responses` PRIMARY KEY (`response_id`)`
- `CONSTRAINT `fk_responses_grant_scope` FOREIGN KEY (`submitted_by_grant_id`, `task_id`) REFERENCES `teacher_grant_tasks` (`grant_id`, `task_id`) ON DELETE RESTRICT`
- `CONSTRAINT `fk_responses_task_version` FOREIGN KEY (`task_id`, `questionnaire_version_id`) REFERENCES `case_questionnaires` (`task_id`, `questionnaire_version_id`) ON DELETE RESTRICT`

索引（包含唯一索引）：

- `CREATE INDEX `IX_questionnaire_responses_submitted_by_grant_id_task_id` ON `questionnaire_responses` (`submitted_by_grant_id`, `task_id`)`
- `CREATE INDEX `IX_questionnaire_responses_task_id_questionnaire_version_id` ON `questionnaire_responses` (`task_id`, `questionnaire_version_id`)`
- `CREATE UNIQUE INDEX `uq_responses_grant_key` ON `questionnaire_responses` (`submitted_by_grant_id`, `idempotency_key`)`
- `CREATE UNIQUE INDEX `uq_responses_task` ON `questionnaire_responses` (`task_id`)`

### audit_logs

| 欄位 | 型別及 NULL／DEFAULT |
| --- | --- |
| `event_id` | `char(36) NOT NULL` |
| `occurred_at_utc` | `datetime(6) NOT NULL` |
| `actor_type` | `varchar(64) NOT NULL` |
| `actor_id` | `varchar(64) NOT NULL` |
| `action` | `varchar(64) NOT NULL` |
| `resource_type` | `varchar(64) NOT NULL` |
| `resource_id` | `varchar(64) NOT NULL` |
| `result` | `varchar(32) NOT NULL` |
| `request_correlation_id` | `varchar(128) NOT NULL` |

主鍵／唯一約束／外鍵：

- `CONSTRAINT `PK_audit_logs` PRIMARY KEY (`event_id`)`

索引（包含唯一索引）：

- `CREATE INDEX `ix_audit_resource_time` ON `audit_logs` (`resource_type`, `resource_id`, `occurred_at_utc`)`
- `CREATE INDEX `ix_audit_time` ON `audit_logs` (`occurred_at_utc`)`

## 6. 資料庫之外還需要保留的服務規則

以下不是只靠資料表就自動成立，整合時須保留服務檢查：

- 教師授權不限時，全部指定任務提交後 USED，或家長撤銷為 REVOKED。不要改回教師 grant expires_at / EXPIRED。
- 會話有效期 8 小時；收據重試最多 15 分鐘且不超過會話到期，只限成功提交會話的同 key／同內容，不能讀答案或再填。
- DB respondent_role ENUM 包含 PARENT、TEACHER 作共用角色關聯；教師授權服務只接受 TEACHER，不能直接因 ENUM 允許 PARENT 就放行家長任務。
- Grant 與 task 必須同 case／TEACHER；同 task 同時最多一筆 ACTIVE grant 由服務交易／資料列鎖檢查，不是 grant_tasks.task_id 的全域唯一鍵。
- 提交先鎖 grant → 排序 tasks，驗證指定版本、題號、選項與完整性，再同交易寫 response、task SUBMITTED、grant USED 及會話失效。
- responses.task_id 唯一；(submitted_by_grant_id, idempotency_key) 唯一，payload_hash 比對正規化內容。重送不能覆蓋答案。
- 撤銷不刪答案；USED 不改回 ACTIVE 或 REVOKED。家長／教師提交後不可修改，醫護只修改匯出 Excel，不匯回。
- 家長僅看到教師是否完成；不傳教師逐題答案、分數、觀察或草稿。
- Development fixture 不代表已確認教師本人、家長身分、個案綁定或同意。
- Audit 為 best-effort，與業務交易分開保存；正式不可遺失稽核尚未完成。其他模組若有 outbox／可靠稽核方案，先提對接差異。

## 7. 已有 seed 與測試資料

| 項目 | 固定 UUID／值 |
| --- | --- |
| 虛構 case | 10000000-0000-4000-8000-000000000001 / CASE-DEMO-001 |
| SNAP-IV 類型 | 20000000-0000-4000-8000-000000000001 / SNAP_IV |
| 教師版 1.0.0 | 30000000-0000-4000-8000-000000000001 |
| 初始任務 | 40000000-0000-4000-8000-000000000001 / round 1 |

題目是 SNAP_IV_Q01～SNAP_IV_Q26，optionValue 為字串 0～3。definition_snapshot 含 isDevelopment=true；scoring_definition 目前 NULL。seed 重跑不覆寫已發布版本或舊答案。開發入口可在已提交後建立新 UUID／輪次的虛構任務，故現有資料不一定只有 seed 那四筆。授權、會話、答案與操作紀錄亦會因驗收新增；不要把整個 earlycare_dev 當成空庫。

不要分享授權碼、cookie、連線密碼或真實個案／問卷資料給比對 AI；比對資料結構與去識別的數量／狀態即可。

## 8. 我方尚未建置：請 A／B 的 AI 確認

- users / ASP.NET Identity 帳號、roles、user_roles、一般 JWT／refresh token、一般登入 session。
- 手機 OTP challenge／發送／驗證紀錄。
- case_parent_links：家長 userId ↔ caseId 的有效關係。
- 家長首次綁定邀請：指定 case＋手機、一次性、有期限、可撤銷。
- consents／知情同意紀錄。
- 院所、醫護任職與個案存取範圍資料。

teacher_grants.issued_by_user_id、consent_reference_id 目前為可空 CHAR(36)，尚未接 users／consents 外鍵；開發資料可空。正式建立授權須取可信身分／綁定／同意，不能讓前端任填這兩個 ID。若 A/B 使用 INT 或非 UUID 使用者主鍵，先提型別與遷移對接方案，不直接把欄位改掉。

醫療端建立 case 的業務責任不代表要建立第二張獨立 children／patients 表。若組員已有這類表，先比較是否同一個「幼兒個案」概念、其醫療欄位／既有資料／引用關係，再決定擴充共用 cases、映射或整併。

## 9. 已知的草案與舊測試，避免誤判

- docs/database/shared-schema-v1.sql 是早期未執行設計草案，不是目前 schema 的權威來源，也不是組員需要一起執行的建表檔。
- 本次 current-v3-generated-reference.sql 是三份 EF migration 的產生結果，只作比對；後續變更仍以使用者統整的新 migration 管理。
- InMemoryTeacherGrantService、InMemoryAuditLogService 及兩題 teacher/test-form 是舊 fixture／獨立測試用途，不是另一套正式帳號表或 migration。
- 所有 /api/dev/* 只在 Development 開放；目前 localhost 邀請 QR 並非正式部署網址。

## 10. 給組員 AI 的比對任務（可直接使用）

請先唯讀檢視你負責的專案，與這份「097af55 共用資料庫基準」及配套產生 SQL 比對。先不要刪表、刪資料、移除已套用 migration、修改歷程或清空資料庫。使用者統整共用 schema 與 migration；你的目標是提供具體整併建議。

請回答：

1. 你目前的 MySQL schema、DbContext、provider／版本、migration 清單；哪些已套用？若未確認，標示未知。
2. 你的所有表與主鍵、外鍵、唯一鍵、狀態及時間型別。以相同業務意義辨識重複，不只按表名。
3. 哪些與 cases、問卷版本、任務、教師授權、session、draft、response、audit 相同？哪些是我方尚未做的帳號、OTP、家長關係、同意或院所模組？
4. 逐項輸出「我方項目／你方項目／差異／保留或合併建議／引用服務與資料影響／migration 是否已套用／仍需確認」。
5. 若建議移除，列出具體檔案／表的候選、原因、替代對接接口及資料搬移計畫。已套用歷史應以後續 migration 整合；不能只因本清單較新就判定另一邊可刪。
6. 你能提供的登入 userId／role、家長綁定查核、consentId／同意查核、醫護院所範圍接口，以及 ID 型別；不交付密碼或 JWT 明碼。

依據不足時列待確認，不自行替使用者決定刪哪一組的表。比較完成後由使用者統整保留／整併，再實作新 migration。

## 11. 可選的 Workbench 唯讀結構核對

以下只讀 metadata，不讀答案或憑證：

```sql
SELECT TABLE_NAME
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = 'earlycare_dev'
ORDER BY TABLE_NAME;

SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE,
       COLUMN_DEFAULT, COLLATION_NAME
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = 'earlycare_dev'
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE,
       GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS index_columns
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = 'earlycare_dev'
GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE
ORDER BY TABLE_NAME, INDEX_NAME;

SELECT TABLE_NAME, CONSTRAINT_NAME, COLUMN_NAME,
       REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = 'earlycare_dev'
ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION;
```

Migration 歷程可在 Workbench 對實際顯示的 __EFMigrationsHistory 表執行 Select Rows 核對三個 ID；不要手動插入／刪除其紀錄。

## 12. 權威來源

- EarlyInterventionCare.Api/Data/ApplicationDbContext.cs
- EarlyInterventionCare.Api/Data/CoreModelConfiguration.cs
- EarlyInterventionCare.Api/Data/AuditRecordConfiguration.cs
- EarlyInterventionCare.Api/Data/Entities/*
- EarlyInterventionCare.Api/Data/Migrations/*
- EarlyInterventionCare.Api/Development/CoreDevelopmentSeed.cs
- EarlyInterventionCare.Api/Development/TeacherWorkspaceService.cs、TeacherWorkspaceSubmission.cs
- docs/shared-contract-v1.md

與此摘要不一致時，先核對實際 migration／model／資料庫歷史，不用早期 raw SQL 草案覆蓋目前已保存的資料。
