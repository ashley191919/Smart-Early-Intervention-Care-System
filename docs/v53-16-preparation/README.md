# V5.3／16 獨立準備批次（2026-10-30）

本批僅文件、測試案例與不可直接執行的腳本草案；沒有完成正式整合或 MySQL 驗收。

## 基準與保護範圍

- 核心核對來源固定為 `bb74e72c1ed00f601646ba669c8ea73952359e98`，原始準備批次以 `git show` 唯讀查閱。
- 文件原始產出於主 `feature/cloud-migration-preflight` Worktree，HEAD `6f9eb603b2de98e2e7ae75443861db28fb507abd` 的 `docs/v53-16-preparation/`。經使用者核准，八份文件已以禁止覆寫方式安全複製至 `.codex-worktrees/v53-parent-core/docs/v53-16-preparation/`；專用分支為 `codex/v53-parent-core`，HEAD 仍為上述固定 Commit。來源完整保留，複製後八份大小與 SHA256 均一致，再僅修正本目標 README；其餘七份保持逐位元一致。
- 起始已有未追蹤 `.codex-worktrees/`、`EarlyInterventionCare.Api/Services/Authorization/`、兩份 RBAC 文件及四份 Permission／RBAC Harness 檔，均保留。其他 Worktree 僅盤點路徑與 HEAD，不檢查或更動其未提交內容。
- 未 Fetch、Push、Merge、Rebase、Commit；未改產品碼、其他組員模組、Migration／Snapshot、系統設定；未啟動 API／MySQL、未連 DB 或使用真實資料。
- Git 擁有者檢查以命令限定 `-c safe.directory=目前路徑` 處理，沒有持久化設定。

## 依正式編號交付索引

| 正式編號／工作項目 | 本批文件 | 完成程度 |
|---|---|---|
| 16-01 共用問卷契約 | [Gateway 與 07／11 交接](gateway-handoff.md) | 固定 SHA 契約整理完成，正式接入待整合 |
| 16-03 Draft 儲存與修改 | [Gateway 與 07／11 交接](gateway-handoff.md)、[案例矩陣](acceptance-cases.md) | 契約及歷史離線證據整理完成，真 DB 待驗收 |
| 16-04 Submit 正式化 | [Gateway 與 07／11 交接](gateway-handoff.md)、[案例矩陣](acceptance-cases.md) | 去重、原子性及錯誤契約整理完成，真 DB 待驗收 |
| 16-06 RBAC 最小整合 | [RBAC 現況與邊界](rbac-matrix.md) | 盤點及測試設計完成，正式權限／Scope 待整合，待決權限不放行 |
| 16-10 隔離測試資料庫規劃 | [隔離環境與復原方案](mysql-plan.md)、[腳本草案](drafts/) | 方案與草案完成，尚未建置 |
| 16-12 基本資料保存驗證 | [案例矩陣](acceptance-cases.md)、[隔離環境與復原方案](mysql-plan.md) | 測試準備完成，實際尚未驗收 |
| 16-13 重啟與備份還原 | [隔離環境與復原方案](mysql-plan.md)、[案例矩陣](acceptance-cases.md)、[腳本草案](drafts/) | 測試準備完成，實際尚未驗收 |

正式編號依使用者已提供的《V5.3 系統開發調整與三人分工規劃紀錄》（規劃日期 2026-10-21）第十二節（三）核對。其餘編號為：16-02 正式家長作者、16-05 Consent／Scope 整合、16-07 Schema／Migration 統整、16-08 MySQL 資料一致性驗收、16-09 地端環境檢查、16-11 本機 API 啟動、16-14 地端操作文件。本次文件複製與文字修正不提升這些工作的驗收狀態；保留既有紀錄及模組所有權，不以文件或替身測試代表正式驗收完成。

## 依賴與下一步

| 依賴 | 提供者／確認者 | 未固定前可做 | 固定後才做 |
|---|---|---|---|
| 07／11 完整交接 SHA（正式分工表已提供） | 使用者／07／11 | 案例、介面對照、審查問題 | 宣告整合基準一致 |
| Organization／Case／Guardian／Consent 模型與撤銷政策 | 11／院方、07 Case scope 協作 | 虛構情境圖、鎖順序審查 | 真 Adapter、FK／撤銷競爭驗收 |
| Permission 指派與醫護讀取／指派／匯出範圍 | 院方＋三人 | 拒絕測試、現況矩陣 | 修改授權、正式 HTTP 驗收 |
| 正式家長問卷版本與內容 | 院方／11／16 交接 | 單題合成 snapshot 規劃 | 正式版本驗收 |
| 共同 Schema、Migration IDs、SQL hash | 使用者統整 | 六項作者差異審查、案例及腳本模板 | Migration 生成／套用、DB 測試 |

16 可獨立繼續補充案例輸入／預期輸出、錯誤與 SQL 證據欄位、離線腳本靜態檢查，以及對收到的 SHA 做唯讀差異審查；不重做已通過的核心。

後續需額外核准：建立隔離實例／資料目錄、啟停 MySQL、建立 DB／帳號／授權、生成或套用 Migration、合成 seed 寫入、真 DB 故障注入／備份／還原，以及任何 Git 發布／整合操作。核准請求須先附具體 SHA、目標 server_uuid／port／庫名、帳號權限、SQL hash、影響與復原步驟；本批沒有要求空白授權。

## 本批驗證界線

只核對固定 Commit 原始碼與既有驗證紀錄，檢查新增文件／草案及 Git 狀態；不在其他 Worktree 跑測試。歷史 PASS 數量在交接文件標明來源，不冒充本次重跑。產品畫面未變更，故沒有 viewport 驗收。

2026-10-30 實際靜態檢查：SQL 與備份還原草案所有非空行皆為註解（COMMENT_ONLY_PASS）；本目錄 Markdown 相對連結皆存在（LOCAL_LINKS_PASS）；`git diff --stat` 無已追蹤檔差異，`git status --short` 相較起始僅新增本目錄。instance 設定草案含刻意未替換的路徑占位符、以 `.draft.txt` 保存，未載入或驗證 MySQL 設定語法。
