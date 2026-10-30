# 16-10／16-12／16-13：隔離 MySQL 準備方案

狀態：候選規格，未建庫、未建帳號、未啟動、未連線。依固定 `bb74e72` 的 schema-plan 與 shared-contract；`docs/database/shared-schema-v1.sql` 只作草案參考，不作另一套正式 schema。

## 實例規格與啟用閘門

| 項目 | 草案 |
|---|---|
| TCP | 僅 `127.0.0.1:33326`；禁止遠端及預設 3306／33060 |
| 庫名 | `earlycare_v53_synthetic`；還原專用 `earlycare_v53_restorecheck` |
| 程序與磁碟 | 獨立 executable／設定／datadir／tmp／error log／PID；路徑待核准，不共用 MySQL80 datadir 或服務 |
| 版本 | 與共同部署契約固定的 MySQL 版本一致；精確 patch、EF／Pomelo／MySqlConnector 版本及相容性另列證據，不先假定已安裝 |
| 字元與時間 | utf8mb4；ID 沿模型 CHAR(36) ascii_bin；DATETIME(6) UTC，連線 session UTC；記錄實際 sql_mode 與 collation |
| 交易 | InnoDB；初輪記錄並固定 REPEATABLE READ 候選，若共同契約另定則先更新案例；FOR UPDATE、deadlock／timeout 必須真引擎驗證 |
| 耐久 | 候選 innodb_flush_log_at_trx_commit=1；若啟 binlog 再評估 sync_binlog=1；未驗證磁碟／程序故障前不宣稱零遺失 |
| 隔離 | 不開 X protocol／外網；不改防火牆／服務／系統政策；設定語法依核定版本再核對 |
| 機密 | 不提交密碼／連線秘密，採短期受控憑證檔；應用只拿 Runtime，DDL 憑證不注入 API |

未來每次操作先核對 host、port、database、server_uuid、版本、datadir、帳號及執行 SQL hash，任一不符停止。只靠庫名或連線字串不足以證明獨立實例。禁止 EnsureCreated、自動 Migrate、自動 Seed。原五份 Migration 保留；六項家長作者提案尚未有正式新增 Migration，預設 MODEL_SNAPSHOT gate 繼續拒絕。

## Runtime／Migration 最小權限設計

兩者均為本實例專用、Host 限 loopback、不含 GRANT OPTION；實際帳號比對方式與連線 Host 須在核定版本驗證。未知 Guardian／Consent 表名不得先發庫級 DML。

| 帳號 | 候選最小授權 | 明確不給 |
|---|---|---|
| `v53_runtime` | 核定帳號／角色／權限／院所／Case／Guardian／Consent／問卷／版本 SELECT；Task SELECT、UPDATE；Draft SELECT、INSERT、UPDATE；Response SELECT、INSERT。正式 Adapter 若有其他寫入，須逐表另審 | DDL、帳號管理、GRANT、FILE、PROCESS、SUPER、跨庫、Response UPDATE／DELETE；本路徑不需要 DELETE |
| `v53_migration` | 僅目標庫、僅核定 SQL 所需 CREATE／ALTER／INDEX／REFERENCES，以及 migration history 所需 SELECT／INSERT；資料變換所需 SELECT／UPDATE／DELETE 等逐句審核後加，完成後撤回 | 全域權限、CREATE USER、GRANT、FILE、跨庫；DROP 不預授，僅核准 SQL 有必要且復原備妥時另准 |

Runtime 不負責指派 Task／建問卷／建立 Guardian／Consent；這些模組若共用 runtime，需另附逐表操作矩陣，不用本表直接擴權。備份與還原不借 Runtime／Migration：由核准的短期操作身分處理，依 dump 選項及實際版本逐項授權。驗收 SHOW GRANTS 與負向 DDL／跨庫／Response 修改測試；migration 帳號不可登入 API。方案不是已完成的 DB 權限配置。

## 完全虛構 fixture 方案

資料 manifest 用邏輯 alias，待模型固定才轉 UUID／INSERT；不用真手機、身份證、病歷號、地址、病史或院方資料。名稱為「合成院所 A／B」「合成幼兒 A1」「合成家長 P1／P2／P3」，不從 production 脫敏複製。若手機欄位強制，須由 11 核定不可對外發送的測試格式並停用 SMS adapter，不填可接通號碼。

| 層級 | 合成集合／關係 | 用途 |
|---|---|---|
| Organization／User | O-A、O-B；P1/P2 同 O-A、P3 在 O-B；各一有效／停用醫護候選 | 跨院、停用、假 role claim |
| Case | C-A1、C-A2 屬 O-A；C-B1 屬 O-B | 同院不同 Case、跨院隔離 |
| Guardian | P1→C-A1 有效；P2→C-A1 有效但非既存作者；P1→C-A2 已撤銷；P3→C-B1 有效 | 綁定與作者是不同門檻 |
| Consent | 適用／缺少／撤回／錯用途／錯版本各一份獨立案例 | 政策由 11／院方固定；合成記錄不是本人同意 |
| Questionnaire／Version | Q-SYNTH、V-P1（單題 PARENT）、V-P2（版本不符）、V-T1（TEACHER）、RETIRED／損壞 snapshot | 不沿用教師量表充當核定家長版 |
| Task | A1 PARENT round1 必要 PENDING；round2；TEACHER 指定任務；CANCELLED；非必要 | 輪次、角色、取消與完成度 |
| Draft | 無草稿、revision1／2、其他作者、NULL 舊作者、overflow（單獨 fixture） | revision 與歷史作者拒絕 |
| Response | 正常 SUBMITTED、同 key replay、相同 key 不同內容；不一致 Task/Response 分離案例 | 收據、去重、一致性；不一致 fixture 僅受控故障庫 |

每案例使用獨立 Task／key 或重置後的新庫，禁止靠修改已提交答案來重用正常情境。建立順序：Organization／User → Case → Guardian → Consent → Questionnaire／Version → Task → Draft 或同交易 Response＋Task。確切 FK 拓樸以共同模型為準；禁止 SET FOREIGN_KEY_CHECKS=0 來通過測試。教師場景另須 Grant／GrantTask／Session 合成資料；不能用家長作者取代 TeacherGrant。

## 重啟、備份與故障復原

1. 正常保存草稿及提交，記錄 taskId、revision、responseId、時間、答案 canonical hash；新 DbContext 重讀，再正常停啟專用 API／實例後重讀。核對 PID／server_uuid／port／原 datadir，不接其他服務。重啟是未來需核准操作。
2. 備份前固定整合 SHA、Migration IDs、schema 指紋、版本、UTC 時點、case row counts、資料 hash。只備份合成庫；候選 consistent single-transaction dump，期間禁止 DDL。dump 選項及所需權限待核定版本審核，密碼不放命令列。
3. 備份檔加 SHA256、限制檔案 ACL，保留至少最近兩個已驗證世代（候選保存 14 天，待使用者確認）。備份不含帳密，帳號重建另由受控規格管理；邏輯 dump 不宣稱包含帳號／binlog／完整 PITR。
4. 先檢視 dump 無來源庫 USE／CREATE DATABASE 或跨庫寫入，再還原至獨立 `earlycare_v53_restorecheck`。不對 synthetic 原庫直接匯入；以無來源庫寫權限的操作身分阻擋誤寫。核對 schema／history、FK／索引、筆數、hash、作者、revision、收據與去重；記錄還原耗時。
5. 目標 RPO：最近一次驗證備份；目標 RTO 候選 60 分鐘，需演練量測後才能承諾。沒有 binlog 備份與演練，不宣稱時間點復原。
6. 連線中斷／commit 回覆遺失：保持原 key，新 scope 重讀；結果未知不重送新 key。deadlock／timeout：整筆結束、清 tracker，受控有限次新交易重試；不能在失敗交易內繼續。
7. Migration 部分 DDL 失敗：停止、保存錯誤及 history／schema，禁止自動 Down／DROP／重跑。磁碟滿／實例故障也先停止寫入，先驗備份還原庫；原合成庫重建、切換或故障注入均另外核准。不破壞共用磁碟來模擬故障。

各次執行記錄開始／結束 UTC、操作者、前置／後置結果與退出碼；帳密、逐題醫療資料不進 log。案例詳細步驟見 acceptance-cases.md。
