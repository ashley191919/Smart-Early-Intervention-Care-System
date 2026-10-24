# V5.3：11 模組註冊、資料模型與教師界線

日期：2026-10-22。給 07／16 的固定整合差異；未 Commit／Push、未套用 Migration。

## 模組註冊

- `ParentModuleRegistration.AddParentV53()` 為冪等註冊，提供 Case／Guardian、Email／SMS adapter、Consent、Actor、Questionnaire Access／拒絕型 Gateway、MVC 保護與安全錯誤。
- 目前由 11 已有的 `AddParentQuestionnaireDefinitions()` 呼叫，讓既有網站註冊點生效；**沒有修改 Program.cs**。
- 07 最終整合 Program 時可使用此模組，保留 JWT／RateLimiter／Middleware 順序；不要重複建立認證方案。
- 新 Entity Configuration 自既有 ApplyConfigurationsFromAssembly 被發現，Service 使用 db.Set<T>()；**沒有修改 ApplicationDbContext.cs**。16 可於整合階段加入正式 DbSet 清單，但不能另建 DbContext。
- 未修改 RBAC 核心、Questionnaire Response Entity／交易引擎、Migration 或 Snapshot。
- API 使用 Bearer。沿用同來源開發；不開 CORS、不調整 Cookie／HTTPS／代理信任。

## 新模型（提案，非已部署表）

### consent_document_versions

- PK：document_version_id，char(36)、ascii_bin。
- document_code(64)、version(32)、title(200)、content(longtext)、content_hash(64)。
- scope_code(32)：ACCOUNT／CASE；預設未確認，必須與明確啟用組態一致。
- approval_status(16)、approval_reference(200)、approved_at_utc、effective_from_utc、retired_at_utc。
- Unique(document_code, version)。本文由伺服器 UTF-8 SHA256 識別；公開介面不提供修改已核定本文的功能。

### parent_consents

- PK：consent_id。
- FK：document_version_id→文件；actor_user_id→users；organization_id→organizations；nullable case_id→cases；全為 Restrict。
- scope_key(40)：ACCOUNT 或 CaseId N 字串，避免 MySQL NULL 唯一性差異。
- document_content_hash(64)、data_use_decision(16)、teacher_participation(16)。
- request_id、recorded_at_utc、nullable revoked_at_utc；均由可信 actor／伺服器時鐘產生。
- Unique(actor_user_id, request_id)。
- Unique(actor_user_id, organization_id, document_version_id, scope_key)。
- 撤回欄位僅預留；本期沒有撤回或改選 API，避免擅自決定院方政策。

所有 UTC 時間採 datetime(6)。新表外鍵保障存在性；院所、Case 與 actor 的一致性由 Service 檢查，沒有宣稱單欄外鍵本身能保證租戶範圍。

## 舊資料策略

- 不回填假的「已同意」，不把登入、舊勾選、Guardian 綁定或教師 Grant 當作同意。
- 原 Case／Guardian／User IDs 保留，現有必填個資不改。本期採事先建立的虛構個案，不代定首次蒐集政策。
- 新部署沒有核定文件／同意紀錄時拒絕受保護家長存取；不要為維持舊展示而停用 gate。
- 家長作者與填表關係需加入共用 Draft／Response，由 16 統整；本輪只提供 DTO／語意，不修改共用模型。
- 既有 Migration 歷史完全保留，不重寫四／五份舊 Migration，也不建立競爭的新正式 Migration。以整合版本實際檔案清單為準。

## 需 16 驗收

1. 模型審查、增量 Migration、Snapshot、FK／索引／編碼及空資料升級。
2. 依規則準備核准虛構帳號、綁定、文件，不使用真實個資。
3. 真 Gateway 執行同一 Contract Suite。
4. MySQL 8.4 交易／唯一鍵／並發／回復、重啟資料保留與備份還原。
5. Case 協調鎖及撤銷共同規格；本輪 SQLite 不證明 MySQL 鎖定正確。

## 教師最小契約（本期保護既有成果）

- 11：家長建立／撤銷／重開的業務資格、Consent、期限政策、QR／連結／Session 入口與 UI。
- 16：指定 Task 範圍的答案驗證、草稿 Revision、Submit 去重、Response＋Task 交易及交易內授權回呼。
- 現有 TeacherGrant／GrantTask／TeacherSession、Dev Workflow、QR 產生保留，沒有把 IsDevelopment 改為允許正式資料。
- 現有 Grant 無期限；Session 八小時與 Receipt 窗不是連結有效期，不能誤報已完成新期限。
- 新期限、重開者、起算點、已提交 Task、重發後舊 Session 及 Consent 撤回規則仍待確認。
- 不將現有 TeacherGrant.ConsentReferenceId 預留欄位自動回填為新 parent_consents 外鍵。

### 候選測試（規格案例，尚未宣稱正式功能執行通過）

| 案例 | 待核定後的必要檢查 |
| --- | --- |
| 有效連結 | 指定教師任務、有效同意與 Session 均符合才可讀寫 |
| 到期邊界 | 以測試時鐘驗證邊界；每次讀取／Draft／Submit 都檢查，不只驗碼時檢查 |
| 到期 Session | Session 尚未到期也不能繞過 Grant 到期 |
| 撤銷 | 舊碼、舊 Session 停止操作，原答案不刪除 |
| 未完成任務重開 | 是否新 Grant／舊碼失效／草稿作者不變須共同定案 |
| 已完成任務 | 不直接把 USED 改 ACTIVE 或覆蓋 Response；依重填政策另議 |
| 兩次重開並發 | 有一致結果與稽核，不能產生兩個不受控有效授權 |
| 不同意教師參與 | 禁止教師授權，但不阻擋家長自己的問卷 |

正式教師重開、指定學校問卷及完整教師提交流程不屬本輪交付。
