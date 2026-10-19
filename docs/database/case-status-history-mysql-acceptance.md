# CaseStatusHistory 隔離 MySQL 驗收計畫

狀態：驗收程式與離線檢查；真正 MySQL 尚待使用者確認環境後執行。
基準：`7fe3bf1ca600d784b72b2848895b2c1fcec9210a`，分支 `codex/case-status-history-migration`。
本文件不是正式資料庫操作授權，也不是已完成 MySQL 驗收的紀錄。

## 1. 範圍與固定環境

專案：`tests/CaseStatusHistoryMySql/CaseStatusHistoryMySql.csproj`。
不啟動 API、不讀 appsettings、User Secrets、環境變數密碼或既有 Runtime 連線。
預設與 `--offline` 僅檢查程式入口、安全規則、編譯模型、Migration operations 與 SQL。
沒有建庫、刪庫、清空資料、Down Migration、修改 Migration／Snapshot 或自動修復功能。

唯一允許的實體環境：

| 項目 | 固定值 |
| --- | --- |
| 主機 | `127.0.0.1`，禁止將該埠轉送至雲端／共用資料庫 |
| TCP 埠 | `33328`，專用臨時 MySQL 實例；不是既有 3306、33318 或 13306 |
| MySQL | `8.0.46`，非 MariaDB；另核對 `@@server_uuid` |
| 帳號 | `earlycare_history_acceptance`，不可與網站 Runtime 帳號共用 |
| 空庫情境 | `earlycare_history_empty_20261019` |
| 升級情境 | `earlycare_history_upgrade_20261019` |

主機／埠限制不能單獨證明服務隔離。操作人員須先確認程序／容器、資料目錄／Volume 與正式環境分開，且沒有 SSH tunnel／port forwarding；由管理員在該獨立實例取得 server UUID，透過非密鑰方式交付。
不要在聊天提供密碼。Harness 不接受連線字串、任意資料庫名稱或密碼參數。

## 2. Migration 順序

1. `20261005152359_InitialSharedCore`
2. `20261005180101_AddAuditLogs`
3. `20261005184434_AddSubmissionReceiptWindow`
4. `20261006074424_IntegrateIdentityAndAuthorization`
5. `20261019131342_AddCaseStatusHistories`

第五份 Up 僅新增八欄歷程表、主鍵、兩索引、兩個 Restrict FK；不回填歷史。
離線測試固定此順序、最小 Up／Down operations 及 Model／Snapshot 一致性。
EF `IMigrator.GenerateScript` 僅透過離線 Factory 產生五段固定的正向 SQL：0→1、1→2、2→3、3→4、4→5。
實際操作不呼叫 `Migrate/MigrateAsync`，也不依現場資料庫狀態選擇目標方向，不使用 `EnsureCreated` 或手工 SQL 草案。
每段執行前核對已套用清單必須等於預期前綴；未知、超前、缺漏或順序不符立即拒絕。執行後再核對預期下一前綴。
第五份已套用時，重複呼叫只驗證完整五份清單，不送出 Migration SQL。
即使不合作的程序在前置檢查後改變 History，本 Harness 也只有固定的正向 SQL 可執行，沒有 Down dispatch 路徑。
此時仍可能有正向 DDL 衝突、部分 DDL 或錯誤紀錄；不能宣稱已排除所有外部競爭。

## 3. 帳號與前置權限（待管理員準備，未執行）

由環境管理員使用另行授權的管理身份建立兩個全空資料庫與專用測試帳號；Harness 不負責這些操作，也不使用 root。
兩個資料庫皆採 `utf8mb4`／`utf8mb4_unicode_ci`。帳號 host 應限於該隔離服務可辨識的本機來源。

僅在上述兩個資料庫各授予：

```text
SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES
```

不授予 `DROP`、`ALL PRIVILEGES`、`GRANT OPTION`、global 權限、角色、PROXY、其他資料庫或網站資料表權限。
Harness 強制 `@@global.partial_revokes=0`，並僅接受 SHOW GRANTS 中所有底線均以反斜線跳脫的兩個精確資料庫 scope。
未跳脫的 `_`、`%`、其他資料庫及格式不明的 scope 均拒絕，不會自行猜測或放寬權限。
若管理員使用不同 partial_revokes 設定或 SHOW GRANTS 表示，須另行審查相容性，不得為執行測試修改共用伺服器設定。
允許的 global `USAGE` 不提供額外資料權限。Harness 要求每個資料庫的權限集合與上列完全相同，不自行增減權限。
information_schema 的可見資訊與 SHOW GRANTS 使用該帳號既有權限，無需授予 global SELECT。
不需要 FILE、PROCESS、SUPER、CREATE USER 或建／刪資料庫權限。

每次正式執行前，兩庫都必須完全沒有 table／view／routine／trigger／event，包括 `__EFMigrationsHistory`。兩庫 preflight 都通過才開始第一次 Migration。
重跑完整驗收須由管理員另行核准後重新準備空庫；Harness 不提供自動清理。

## 4. 已可離線執行的指令

在 Migration Worktree 根目錄：

```powershell
dotnet build tests/CaseStatusHistoryMySql/CaseStatusHistoryMySql.csproj -p:NuGetAudit=false
dotnet run --project tests/CaseStatusHistoryMySql/CaseStatusHistoryMySql.csproj --no-build -- --offline
dotnet run --project tests/CaseStatusHistoryMySql/CaseStatusHistoryMySql.csproj --no-build -- --help
```

離線檢查會注入 spy 代替實體 Runner，確認錯誤參數／help 不進入 MySQL 路徑，合法 MySQL 參數僅在 spy 中驗證 dispatch。
錯誤輸出不含密碼、SQL、連線字串或資料列。離線生成 SQL 使用既有禁止連線的 Factory。
`NuGetAudit=false` 僅作用於這次命令，不修改設定，也不代表完成線上弱點檢查。

## 5. 待確認後才可執行的指令

以下不是本批已執行項目。須先由使用者確認隔離實例、兩庫、帳號權限及 UUID：

```powershell
dotnet run --project tests/CaseStatusHistoryMySql/CaseStatusHistoryMySql.csproj --no-build -- --mysql --server-uuid <管理員確認的隔離實例UUID>
```

使用互動終端機；無法由 redirected stdin 輸入。程式要求逐字輸入：

```text
APPROVED ISOLATED HISTORY MYSQL 33328
```

其後才以不回顯方式輸入專用帳號密碼；不從磁碟、環境變數、命令列或網站設定取密碼，不將密碼寫入報告。密碼仍需在測試程序記憶體中短暫存在；無法宣稱 managed string 可保證清零。
每庫建立一條持續開啟的 `VerifiedSession`，首次開啟即驗證 server UUID、版本、選定 database、CURRENT_USER、grants、FK／unique checks、autocommit、partial_revokes 及 strict SQL mode。
所有查詢、資料指紋、正向 SQL，以及 EF command／交易開始／提交前，都在實際使用的相同實體連線重新驗證身分、設定、權限與互斥鎖持有人。
EF Context 借用此連線，不另建連線；攔截器禁止重開及同步 SQL／交易入口，沒有自動重試。斷線後立即停止，不重連。
guard 本身的唯讀 probes、取得／釋放鎖與失敗時 rollback／dispose 是連線保護及資源清理，不能依賴業務命令的 guard 遞迴驗證。

第一條連線取得固定 MySQL named lock `earlycare_history_acceptance_20261019`，`GET_LOCK(...,0)` 失敗立即停止；此鎖涵蓋兩庫完整驗收。
兩庫先確認為空，每個情境開始、首次寫入前再次檢查空庫。第二條連線每次操作也核對鎖仍由第一條連線持有。
正常與例外路徑透過 finally 釋放，並 dispose 兩條連線；程序強制終止則由 MySQL 在偵測連線中斷後釋放，不能保證零延遲。
named lock 只防止遵守相同 lock 協議的 Harness，不能禁止管理員／其他不合作程序寫入或 DDL；環境仍須獨占。

## 6. 真正 MySQL 測試矩陣（全部尚待執行）

| 情境 | 驗收條件 |
| --- | --- |
| 空庫 0→5 | 逐份套用並逐步核對 Migration History 的完整有序前綴 |
| 舊庫 4→5 | 前四份後加入合成 fixture，15 張既有表的資料與結構指紋升級後不變 |
| 不回填 History | 升級後 History 筆數為 0，保留既有 Case，不製造初始事件／個資 |
| 八欄／PK／索引 | information_schema 核對欄位型別、可空性、主鍵及兩個索引名稱／唯一性／欄位順序 |
| Case／User FK | 核對兩個 FK 名稱、來源／目標欄位與 RESTRICT；不存在 Case／User 產生 MySQL 1452 且命中預期約束 |
| 正確 FK | 合法 Case＋User 的 History 寫入後，以新 DbContext 查回 |
| 系統事件 | ChangedByUserId 與 Reason 皆 NULL 可保存 |
| 原子 Commit | 使用正式 EfCaseStatusStore：Case 更新與 History 一次 Commit，新 DbContext 查回兩者 |
| 主動 Rollback | 同交易 SaveChanges 後不 Commit，真實 Store disposal 回滾兩者；以新 DbContext 驗證 |
| 故障 Rollback | Case 已 SaveChanges 後，History 使用不存在 User 觸發 FK 失敗；Dispose 後 Case 與 History 都沒有部分提交 |
| 重複 Migration | 再執行兩次最新 Migration，結構、資料指紋及五份 History 不變 |
| 儲存引擎 | 16 張業務表與 EF History 表皆為 InnoDB，表集合完整 |

Fixture 覆蓋 15 張既有表，使用固定 SYNTHETIC 字樣、隨機 UUID、非登入用 hash 字串、無效可用性憑證 hash 與非臨床問卷 JSON；不呼叫登入／OTP／臨床計分。這是儲存層 fixture，不是合法正式業務流程或真實帳號。
結構指紋涵蓋既有表 engine／collation、欄位、default、索引與 FK，資料指紋涵蓋整列而非僅列數；不輸出資料列或指紋值。
本批不驗證完整 Schema 等價、跨版本 MySQL 相容性、並行死結／鎖定次序、正式授權、DB append-only trigger、服務重啟或家長流程。

## 7. 結果與失敗紀錄

程式逐項輸出 `PASS <SCENARIO> <CHECK_CODE>`；所有失敗出口（含一般例外）輸出安全 scenario、step、code 並 Exit 1，成功 Exit 0。
固定 `PARTIAL_STATE_POSSIBLE` 訊息提醒可能殘留 Schema、Migration 或合成資料；不列印底層例外、密碼或完整連線字串。
離線檢查結尾明示 `MYSQL ACCEPTANCE: NOT RUN`。必須分開記錄離線與實體驗收。

```text
Run UTC:
Git branch / full SHA / worktree dirty status:
Harness version (diff/commit):
.NET SDK / EF / Pomelo:
Mode: OFFLINE | MYSQL
Endpoint / databases / account name (no password):
Expected server UUID / actual verification result:
MySQL version:
Preflight: PASS | FAIL | NOT RUN
EMPTY_ZERO_TO_FIVE: checks / PASS | FAIL | NOT RUN
UPGRADE_FOUR_TO_FIVE: checks / PASS | FAIL | NOT RUN
Migration order and applied IDs:
FK 1452 / commit / rollback / repeat / preservation:
Exit code:
Failure safe code / last successful step:
Leftover state / operator recovery decision:
```

失敗後立即保留兩個合成資料庫供檢查，不再繼續其他情境，不自動 DROP／重試／修復。
MySQL DDL 可能部分完成，不能依賴交易包裝完整復原。記錄最後成功步驟，由管理員決定重新準備隔離空庫。
全程不操作 `earlycare_dev`、既有 33318 實例或雲端資料庫；此保證依賴管理員確認端口與實例隔離，以及專用帳號沒有其他權限。

## 8. 本批離線結果（2026-10-19）

- 新 Harness Build：0 警告／0 錯誤；Exit 0。
- 安全修正後 `--offline` 與無參數預設入口：70 項通過；`--help` Exit 0（舊版 37 項結果不作為本版驗收）。
- 新增測試直接使用實際的 observation、prefix、空庫、lease 及 forward dispatch 邏輯；涵蓋新連線 UUID／帳號不符、非空、未知／超前／亂序 Migration、互斥失敗／例外釋放、未跳脫 GRANT、一般及領域錯誤的安全提示。
- 以 executor spy 模擬檢查後 History 被外部改變，確認僅選擇固定 Up SQL 且後置核對失敗；這不是實體 MySQL 競態測試。
- 另外直接測試實際 command／transaction／reopen 攔截器；只建立未開啟的連線物件，確認閉合連線、非預期連線、同步命令及重開入口遭拒，沒有實體連線。
- CoreDatabaseHarness：16 表／歷程結構／Model-Snapshot 一致性通過；Exit 0。
- QuestionnairePhase22Harness：158 項通過；Exit 0；使用 Fake Store，非 MySQL。
- CaseStatusServiceHarness：17 項通過；Exit 0；替身交易，非 MySQL。
- CaseStatusHistoryPolicyHarness：33 項通過；Exit 0。
- CaseStatusHistoryQueryHarness：12 項通過；Exit 0；非 MySQL。
- 工具環境：.NET SDK `10.0.100-rc.2.25502.107`，目標 net8.0；沒有更動專案既有套件版本。
- 真實 MySQL、密碼輸入、preflight 連線、Migration 套用、FK／交易實測：全部 NOT RUN。

這些結果不能替代第 6 節的真正 MySQL 驗收，也不代表正式家長／教師 API 已可使用。
