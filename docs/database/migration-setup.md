# 本機九張核心表與第一份 migration

日期：2026-10-05。已完成程式模型、InitialSharedCore migration 及驗證。使用者已回報本機 `--apply --seed` 全部通過：migration 已套用 1 份、待套用 0 份，seed 重跑無重複資料且透過新 DbContext 查回成功。2026-10-06 教師開發 API 授權與任務查詢已改接 MySQL，獨立資料庫整合測試通過；使用者本機網站待驗收。

## 已建立

- `EarlyInterventionCare.Api/Data/Entities/`：九個 persistence entities，不直接當 API 回應。
- `CoreModelConfiguration.cs`：表／欄位、UUID CHAR(36) ascii_bin、角色版本與個案範圍的複合外鍵、每 task 一份正式答案唯一鍵、草稿 revision 樂觀鎖。
- `ApplicationDbContext.cs`：九個 DbSet 與 Fluent mapping。
- `ApplicationDbContextFactory.cs`：供工具使用，不靠網站啟動；離線生成使用 MySQL 8.0.46 設定。實際操作限定 localhost／earlycare_dev，從本機 user-secrets 或環境設定讀取 DefaultConnection。
- `Data/Migrations/`：第一份 InitialSharedCore migration、designer 與 model snapshot。
- `.config/dotnet-tools.json`：固定 dotnet-ef 8.0.13，組員可還原同版工具。
- `Development/CoreDevelopmentSeed.cs`：明確呼叫才建立虛構 case、教師 SNAP-IV 版本與任務；不自動在網站啟動執行，不建立家長同意或正式授權，不覆蓋既有版本及答案。
- `tests/CoreDatabaseHarness.csproj`：模型驗證與本機受限的 migration／seed 操作工具，不輸出憑證。

生成的 `initial-shared-core.generated.sql` 是 migration 的供審閱輸出，包含九張核心表與 `__EFMigrationsHistory`。不要另外手動執行此檔或舊 shared-schema-v1.sql；正式 schema 統一由 migration 管理。

## 操作（在儲存庫根目錄）

先確認當前專案的 UserSecretsId 對應到已保存的 secrets.json；連線指定 localhost、3306、earlycare_dev、earlycare_dev_user，使用該專案帳號密碼。密碼只放本機，不能提交或貼到聊天。

若手動保存的位置或格式無法讀取，可在自己的互動式終端機執行以下工具，依提示輸入專案帳號密碼（輸入不回顯）。工具會從專案 UserSecretsId 決定正確本機位置、保留其他 JSON 設定，並正確處理密碼中的特殊字元；不在網站或聊天中詢問密碼。

```powershell
dotnet run --project tests/CoreDatabaseHarness.csproj -- --configure
```

```powershell
dotnet tool restore
dotnet build EarlyInterventionCare.Api
dotnet run --project tests/CoreDatabaseHarness.csproj
```

上述只驗證模型，不連線也不建立表。

```powershell
dotnet run --project tests/CoreDatabaseHarness.csproj -- --database
```

這一步檢查本機連線與 migration 狀態，不改變 schema。成功後才執行：

```powershell
dotnet run --project tests/CoreDatabaseHarness.csproj -- --apply --seed
```

工具先確認資料庫為空或已由本批 migration 管理，再套用 migration；遇到未受管理的既有表則拒絕，不刪除資料。seed 重複執行兩次，並以另一個 DbContext 查回，確認不重複新增及跨連線保存。整體操作若失敗，依錯誤修復後重跑；MySQL DDL 不保證整份 migration 失敗時全部回滾，不用刪資料庫作為第一個修復手段。

初版 1064 修正：布林映射由會生成非法 boolean(1) 的設定改為 tinyint(1)，同步修正尚未成功套用的 InitialSharedCore 及 snapshot。Windows 歷程表大小寫亦已處理。若首次 migration 失敗留下核心表，工具只在沒有已套用 migration、只待套用 InitialSharedCore、現有核心表全為空且欄位／鍵相符時接續剩餘語句，不刪表；表內有資料或定義不符則拒絕。後續已成功套用的 migration 不依此方式原地改寫。

若需要新增正式資料庫變更，由使用者統整 migration；不可用 `EnsureCreated` 取代既有 migration 管理。網站啟動不自動套用 migration 或 seed。

## 驗收

Workbench 刷新 earlycare_dev，應有九張核心表及一張 EF 歷程表。執行：

```sql
USE earlycare_dev;
SHOW TABLES;
SELECT case_code FROM cases WHERE case_code = 'CASE-DEMO-001';
SELECT task_status FROM case_questionnaires
WHERE task_id = '40000000-0000-4000-8000-000000000001';
```

預期一個虛構個案、一份 PENDING 教師任務。使用者本機工具已驗證跨連線查回及 seed 不重複；網站重啟後的教師流程已於獨立測試資料庫通過，使用者本機仍需照操作文件驗收。這只完成共用資料底座，教師授權與任務查詢已改接 MySQL，草稿及正式提交 API 尚未接上 MySQL。

正式身分、院所、家長綁定、同意與完成收據表仍需後續 migration；本模型不能當成正式家長同意與權限已完成。狀態轉移、版本不可變、教師角色限定及同時只有一筆 ACTIVE grant 等規則仍須服務交易驗證。

## 本次已驗證／未驗證

- 已驗證：build 0 警告／0 錯誤；九表模型；版本與角色外鍵；授權個案範圍外鍵；draft concurrency token；response task 唯一鍵；刪除限制；migration snapshot 無待產生的模型變更；生成 SQL 有九張核心表與一張 EF 歷程表。
- 已於獨立臨時 MySQL 8.0.46 驗證：全新資料庫套用、重現舊 1064 後保留兩張空表接續、Windows 小寫歷程表、重複執行、seed 保存及新 DbContext 查回。臨時實例使用另一個本機連接埠，不操作使用者 earlycare_dev。
- 使用者本機已驗證：修正版工具接續匹配的空表、套用 InitialSharedCore、已套用 1 份／待套用 0 份、seed 重跑不重複及新 DbContext 查回。依據為使用者回報的終端機成功輸出。
- 未驗證：MySQL 外鍵拒絕的實際寫入測試及網站重啟整合；模型關聯檢查不等於已測試資料庫拒絕所有非法寫入。

## 2026-10-06 操作紀錄擴充

新增第二份 AddAuditLogs migration，僅建立 audit_logs 與兩個查詢索引，原九張核心表與 InitialSharedCore 保留。目前模型為九張核心表加一張操作紀錄表；套用後 migration 數量為 2。獨立 port 33318 已完成升級及重啟／故障測試，使用者已回報本機套用與驗收成功。沿用 CoreDatabaseHarness --apply，不需要重建資料庫或重新 seed；詳細操作見 [操作紀錄驗收](../teacher-audit-testing.md)。初始空表接續工具仍只接續第一份 migration，完成後再正常套用後續 migration。


## 提交收據 migration（2026-10-06）

第三份 AddSubmissionReceiptWindow 僅在 teacher_sessions 新增 nullable datetime(6) receipt_expires_at_utc，保存短期原請求去重收據通道，不新增憑證明碼。獨立 port 33318 已套用 migration=3／pending=0；使用者已確認本機整合驗收可保存推送。既有 response 表已具 task 與 grant+key 唯一鍵、payload_hash，因此不重建答案表。詳見 [一次提交驗收](../teacher-submission-testing.md)。
