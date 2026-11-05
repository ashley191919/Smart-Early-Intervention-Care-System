# 共同 Snapshot／增量差異候選白名單審查包

日期：2026/11/05。模型基準：11＋16 最小聯集 c213e051034c5bd6d82bdd90a6aae7818e600f0c。分支 codex/integration-parent-model-union。

**用途是提供 16 審查，不是 Migration／DDL／回填授權，也不是已核准白名單。** 本輪僅使用 EF 模型 metadata，未讀取實際資料庫、User Secrets 或 runtime connection。

## 可重現的離線比對

工具：tests/ParentV53/ParentSchemaReview.cs；入口位於既有 Harness，--schema-review 模式在 SQLite／HTTP fixture 啟動前直接完成並返回。

```powershell
dotnet run --project tests/ParentV53/ParentV53Harness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-schema-review/ -- --schema-review docs/parent-schema-difference-review-20261105.json
```

使用現有 ApplicationDbContextFactory：明確 MySQL provider 8.0.46，Connection interceptor 禁止開啟連線。這是設計時 provider，不是雲端 MySQL 8.4.11 實機驗收。CreateMigration 在本工具中僅具現化既有 Migration 類別取得 TargetModel；**沒有呼叫 Up／Down、Migrate、EnsureCreated、SQL generator、Migration scaffolder 或產生新 Migration**。

輸出 parent-schema-difference-review-20261105.json 保存兩組實際 IMigrationsModelDiffer 結果，包含表／欄位／型別／nullable／預設值／collation／charset、FK、索引與 unique。

| 比對 | 結果 | 審查結論 |
|---|---|---|
| 11 目前舊 Snapshot → c213e05 Runtime Model | 43 操作；包含 Create case_status_histories 及其 2 個索引 | 不能直接依舊 Snapshot scaffolding，會與第五筆 Migration 重複建 History。 |
| 第五筆 AddCaseStatusHistories.TargetModel → Runtime Model | 40 操作；History 新建為 0 | 只作已保留五筆歷史的候選邏輯基準；不等於已驗證某資料庫套用五筆。 |

第五筆 TargetModel 已含 History，但仍無完整 11 家長模型，因此不能直接作為完整共同 Snapshot；更不能先把 Snapshot 改成完整 Runtime Model 再產生零差異 Migration，否則新增欄位沒有建置操作。16 須先核定與五筆既有 Migration 一致的歷史基準，再由候選差異及政策產生合法增量。

## 40 項差異的候選範圍

精確名稱／細節以 JSON 的 historyTargetToUnion 為準，以下只分組，不表示均可直接套用。

| 類別 | 數量 | 範圍 |
|---|---:|---|
| AddColumn | 13 | users 的 email、normalized_email、email_verified_at_utc、phone_verified_at_utc；問卷作者五欄；cases 的 organization_id、revision、national_id、address。 |
| CreateTable | 5 | guardians、case_guardians、parent_binding_invitations、consent_document_versions、parent_consents；包含各表原 Mapping 的 PK／FK。 |
| CreateIndex | 19 | User 信箱查找、家長作者索引／Key、Case 院所、Guardian／邀請／Consent 的查找及 unique，精確列集見 JSON。 |
| AddForeignKey | 3 | cases→organizations；Draft／Response 的 parent_author_user_id→users，Restrict。 |

### 可先共同審查的加法項目

- 問卷五欄全部 nullable、無作者／關係／HashVersion 預設或推定回填；保留教師 Grant FK、Task 唯一鍵、教師 Key；家長作者 FK／索引一併審查。
- User 的四個新增欄位保持 nullable；不能把聯絡信箱或既有 Phone 視為已驗證身分。
- Case.organization_id 保持未知 NULL，不能填目前登入者院所。Case.revision 的資料並發用途與資格世代分開。
- 五張 Guardian／Consent 表及索引雖在現有 Runtime Model，仍要由 16 審查 FK、唯一性及舊資料導入政策，不能憑此文件自動建置。

### 必須暫緩／先決策的項目

1. **cases.address／national_id 非 NULL 且 differ 帶 defaultValue=""**：這是 EF 差異工具對新增必填欄位產生的操作，不代表同意空字串回填。須決定分階段 nullable、核實回填或其他合法導入策略，未決前不可照抄套用。
2. cases.revision 的候選 AddColumn defaultValue="0" 需單獨確認資料初始規則；它不是 BindingGeneration，不得作資格初始世代證據。
3. ParentConsent 的 actor／organization／document／scope unique 會影響撤銷後同版本重新同意；不得自動刪舊列、取消撤銷或改唯一鍵來通過導入。
4. guardians 的院所／電話 unique、case_guardians 的有效主要聯絡人 unique 與家長回填來源，需要實際舊資料盤點後再核准；本輪沒有資料證據。
5. HashVersion 欄位目前 varchar／utf8mb4_unicode_ci；核心要求精確版本識別，若未來在 SQL 用值比較或建約束，需額外核對大小寫／collation 語意，不自行改 Mapping。
6. 本次沒有 BindingGeneration、Epoch、六項 ABA、輪次／Workflow 新模型，不能把候選 Stamp 或有效輪次設計混入此 40 項差異。

### 任何出現都必須重新審查的項目

Drop／Rename、作者／答案／教師 Grant／Hash bytes 的資料更新、教師 FK 或既有 unique 移除、重建 History、修改前五筆歷史 Migration、刪改歷史 Snapshot 以隱藏差異、非白名單表欄位、SQL 回填及 seed 身分均不得自動接受。

本次 historyTargetToUnion 無 Drop／Alter／Rename／Data Operation；但 IsDestructiveChange=false 也不能把上述必填欄位預設或資料政策視為安全。

## 由誰接續

**11 已完成：** c213e05 模型／Reader 聯集、API 編譯、249 項家長／Reader／模型及 209 項 Guardian 隔離回歸；本輪離線差異工具、JSON、History 重複建表證據及候選範圍清單。舊 Schema gate 失敗仍保留，不是本輪修復項目。

**16 接續：** 審查 43／40 兩組差異、確立五筆歷史基準及共同 Snapshot 修復方式；將政策核准後的確切欄位／FK／索引定為正式白名單，再另外核准增量 Migration。亦請提供 guarded 核心／canonical encoder／Bridge 固定來源與最小相依清單。

**共同／院方：** Case 舊資料與 Consent 再同意唯一性政策；Stamp／ABA、跨程序鎖序及交易接口。Snapshot 歷史基準核定與這些業務決策可平行討論，但未定政策不能先填假資料。

**後續才做：** 正式 Migration scaffolding、核對 SQL、隔離資料庫 Applied Migration／升級及回復／約束／並發驗收、修復 Schema gate、正式資格接線與入口審核。本次沒有操作任何 MySQL 或改 Snapshot。
