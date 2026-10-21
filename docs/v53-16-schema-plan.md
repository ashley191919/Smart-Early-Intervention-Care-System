# V5.3 Schema 差異與待核准升級

基準：533b659，五份 Migration／Designer／Snapshot 完整保留。本批沒有產生第六份、沒有修改 Snapshot、沒有 EnsureCreated。

## 精確作者模型提案

1. questionnaire_drafts 加 nullable `parent_author_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin`。
2. questionnaire_responses 同上。
3. draft author → users.user_id，Restrict FK `fk_drafts_parent_author`。
4. response author → users.user_id，Restrict FK `fk_responses_parent_author`。
5. `ix_drafts_parent_author` 非唯一索引。
6. `uq_responses_parent_key(parent_author_user_id, idempotency_key)` 唯一索引。

六項由實際 EF ModelDiffer 離線驗證；保留原 task 唯一 response、task/version 複合 FK、grant/task scope FK、teacher idempotency 與 revision。
舊教師／未知作者列保留 NULL，不推定作者，不對未知資料加破壞性 NOT NULL。Parent gateway 拒絕舊無作者或教師作者列，不能用有 Case 權限來接管。

這是尚未發布的模型提案。執行既有預設 HasPendingModelChanges 檢查預期失敗；proposal mode 只做回歸，不放行部署。所有需新作者欄位的 EF 路徑要等升級；不能將本分支直接部署到舊 DB。

## 尚缺的領域模型

11 提供：Case／Guardian／Consent Entity、Configuration、最小欄位、院所／User／Case FK、唯一鍵、鎖定協定與回填提案。
Case／Guardian 與同意先行有建立順序問題，未定前不自行加 NOT NULL、身份證、地址、院所假資料。
QuestionnaireVersion 必須先確認家長內容與版本 ID；教師來源不得直接當家長正式版本。
Guardian／Consent 的正式 Migration 等最小模型契約固定，不等所有 UI。

## D4 真 MySQL 操作前的核准清單

候選唯一目標：新隔離實例 `127.0.0.1:33326`，database `earlycare_v53_synthetic`。
不操作目前 MySQL80／3306／33060、earlycare_dev、共享／雲端／院內資料庫。
目前沒有提供執行 Migration 的可用命令，因第六份尚未產生，不應對未定稿 SQL 請求空白授權。

模型固定後，核准申請必須列出：

- 完整整合 SHA、Migration IDs（0→latest 或既有五份→新增量）及離線生成 SQL 的 SHA256。
- 獨立實例 server_uuid／版本／port、資料庫、執行帳號與權限；兩人核對隔離端點。
- 空庫安裝和五份升級分開驗收；先保存 schema/history／合成資料備份及 SHA256。
- 不使用 runtime app 帳號執行 DDL；不自動 Migrate／Seed。
- FK 1452、duplicate 1062、commit／rollback、同 key 重送、stale revision、撤銷競爭、重啟重讀與原資料保留。
- MySQL DDL 可能部分完成；失敗立即停止保留隔離庫，不自動 Down／DROP／重跑。
- 復原先對獨立 `earlycare_v53_restorecheck` 驗證備份，再由使用者核准是否重建專用合成庫；不改現有共享 DB。

真正 Migration、帳密使用、備份與還原均尚未執行，必須另行明確核准。
