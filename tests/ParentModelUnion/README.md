# 11＋16 模型聯集離線可行性驗證

此目錄是獨立測試組件，不是正式模型、Migration 或部署授權。16 基準為 `3c0adadc62dc85eaca0858caf5a4d03b2b44a751`，11 固定來源為 `b5573b5311641d9b108930cbdf5e461dbc1bcea0`。

## 來源與驗證界線

[source-manifest.json](source-manifest.json) 記錄 GitHub 唯讀取得的七份 11 原始碼及其 Git blob、21 份連結的 16 來源，以及五份歷史 Migration、Designer 與 Snapshot 的 blob。Pinned11 保留原始宣告，不改寫成假模型；blob 檢查先將工作樹換行正規化為 LF。

組件採 11 的 CaseRecord、User、Guardian、Consent、Identity Configuration 與 ApplicationDbContext，搭配 16 的共用問卷 Entity、五欄 Parent Mapping、Core Mapping、History Mapping。未引用正式 API 專案，未修改任何既有來源。使用既有 OfflineAdo 連線替身，僅建構 EF 設計模型與關聯模型，測試確認沒有開連線或執行命令。

| 驗證層級 | 本輪結論 |
|---|---|
| 選定原始碼共同編譯 | 通過，並非完整三人 API 編譯 |
| EF／Pomelo 模型建構 | 通過；檢查五欄、FK、索引、教師關聯及 History |
| 完整 API／Service 聯集 | BLOCKED：11 Context 缺少 16 Store 使用的 History DbSet |
| 模型與 Snapshot／Migration 對齊 | BLOCKED，未產生任何 Migration |
| 真實 MySQL 約束、隔離、交易與升級 | BLOCKED，未連線或執行 |

執行結果為 **75 PASS、0 FAIL、3 BLOCKED**；退出碼 2 表示尚未整合就緒。這些檢查也包含來源／歷史檔案完整性，不能全部解讀為資料庫測試。

```powershell
dotnet restore tests/ParentModelUnion/ParentModelUnionHarness.csproj --source tests/ParentModelUnion -p:NuGetAudit=false
dotnet build tests/ParentModelUnion/ParentModelUnionHarness.csproj --no-restore
dotnet tests/ParentModelUnion/bin/Debug/net8.0/ParentModelUnionHarness.dll
```

僅使用已快取套件；缺少快取時停止，不藉此下載或啟動服務。測試只產生本目錄忽略的 bin／obj 建置輸出，不產生 SQL、Migration 或實際資料。

## 已證實的來源差異與未來最小修改

1. **16 五欄已具備**：Draft／Response 的 nullable ParentAuthorUserId 與 RespondentRelationship，以及 Response nullable HashVersion。作者 FK、家長 Key／教師 Key／Task 唯一鍵、教師 Grant 與 Task／版本關聯均保留；本輪不修改這些模型。
2. **Case／User 需逐欄合併**：未來由 11 提供 CaseRecord、User 與 IdentityModelConfiguration 的業務欄位差異，16 審查共同 Mapping。16 Case 尚缺 11 的 OrganizationId、Revision、NationalId、Address；User 另有電話／Email 驗證相關欄位差異。不可整檔覆蓋。
3. **Guardian／Consent 需納入**：11 提供 GuardianContact.cs、GuardianModelConfiguration.cs、ParentModule/ConsentModels.cs。其 Entity Configuration 必須一併納入共同模型；Consent 不應僅因沒有顯式 DbSet 就被漏掉。
4. **Context 需聯集**：共同 ApplicationDbContext 保留 11 Guardian、CaseGuardian、Invitation DbSet，並保留 16 CaseStatusHistories DbSet。11 的 Configuration 掃描在本測試確實納入 History Entity，但這不會自動產生 History DbSet 屬性，仍不足以編譯 16 的 EFCaseStatusStore。
5. **History 不可丟失**：保留 CaseStatusHistory Entity、Configuration、相關 Store 及第五筆 Migration；本輪只連結模型與政策依賴，沒有編譯或整合完整 Store／Workflow。

選定聯集沒有觀察到 EF 模型建構衝突，不代表兩份 Configuration 可以重複註冊。本測試每種 Mapping 只選一份，避免把掃描順序當成覆蓋策略。正式整合仍應逐項核對並只保留單一共同定義。

## 資料庫待決問題

- 舊 Case 的 OrganizationId 未知時不得填目前院所；先唯讀盤點，維持拒絕存取，經來源核實後才另行核准資料處理。NationalId／Address 必填與舊資料缺漏需討論分階段導入，不得用假值或空字串回填來滿足約束。Case Revision 不是 BindingGeneration。
- ParentConsent 現有 Actor／Organization／DocumentVersion／ScopeKey 唯一鍵可能阻止同文件同 Scope 撤銷後新增再同意紀錄。11 與院方須決定歷史保存及再同意方式；不得自動刪舊列、改 Key 或把撤銷列當有效同意。
- 五欄的未來增量需保留 NULL、無預設值、無作者／關係／HashVersion 推定。另有作者索引與 FK；完整共同增量尚包括 Case／User／Guardian／Consent，不能只產生五欄就稱模型已對齊。具體 DDL 須待共同模型固定後審查。
- 保留 `20261005152359_InitialSharedCore`、`20261005180101_AddAuditLogs`、`20261005184434_AddSubmissionReceiptWindow`、`20261006074424_IntegrateIdentityAndAuthorization`、`20261019131342_AddCaseStatusHistories` 及 Designer。本輪核對現有 Snapshot 不變；未來僅經核准的增量流程更新共同 Snapshot，不用另一分支 Snapshot 覆蓋。
- HashVersion 精確比較、舊 NULL 作者拒絕、教師 Grant 遮蔽與既有唯一鍵語意不因模型可建構而改變。Reader 的正式 DI／手動建構替換、共同交易、資格重驗、Stamp 與鎖序都仍未啟用。

## 依賴順序與分工

16 可先保存本離線證據、整理逐欄合併清單與隔離庫驗收案例。11 確認 Case 缺漏處理、Consent 歷史／再同意及來源欄位；16 統整共用模型、History、作者 Mapping 與未來 Migration 審查。07 確認醫療端 Case 消費契約、Workflow／Task 對共同模型的依賴；未提交功能不計入。

順序為：共同欄位與資料政策確認 → 核准逐檔模型／Context 聯集 → 完整 API 編譯及離線回歸 → 核准增量 Migration／Snapshot → 核准隔離庫備份、升級與資料驗收 → 正式 Adapter／鎖序及入口另行驗收。MySQL80／3306 不在測試範圍。

既有教師及舊家長 Schema gate 應維持原狀：模型尚未 Migration 對齊、舊測試只接受六個 author 增量操作的預期尚未合法更新。其失敗不得由本 Harness 的模型建構 PASS 取代；gate 後續驗證仍 BLOCKED。
