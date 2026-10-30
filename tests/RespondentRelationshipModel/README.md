# RespondentRelationship／HashVersion 最小模型離線驗證

基準 `35da12a86c363e532aef56273acade63bf127199`。本批只修改 QuestionnaireDraft／QuestionnaireResponse 及 ParentQuestionnaireModelConfiguration：新增 `string? RespondentRelationship`，映射 `respondent_relationship varchar(32) NULL`，無 default／computed／generated 值，無新增索引／FK／Required 或 backfill。

HashVersion 增量基準為 `5ff10f0d9c5d7eacc832e5c8589394c4a20d95b3`：只在 QuestionnaireResponse 新增 `string? HashVersion`，映射 `hash_version varchar(64) NULL`，無 default／computed／generated 值、索引／FK 或 backfill。QuestionnaireDraft 不新增版本欄位；既有 PayloadHash、三個唯一索引、Idempotency 查詢均不修改。本輪沿用此 harness，補充版本 metadata／NULL／教師相容案例，不複製既有教師 fixture。

NULL 表示「未記錄／未確認版本」，不得視為已確認舊演算法，也不推定為 v1。新家長 encoder 標識仍為 `earlycare.parent-submit.canonical/v1`；本輪只測明確賦值，不讓正式 Submit 寫入、不重算舊 Hash。測試的清除／賦值僅在記憶體 ChangeTracker，沒有資料庫更新。

Collation 待決：本輪不設定欄位、資料表或全域 Collation，該欄位會繼承現有資料表設定；繼承的比較不保證區分大小寫。未來版本選擇必須在應用層使用 ordinal 精確比較，不用資料庫預設等號、不 Trim／轉大小寫、不把 NULL 當 v1。欄位級 binary collation（如協調後採 ascii_bin）仍待審查／核准，不在本批改動。此次 metadata 測試只確認未附加 column collation，沒有驗證真資料庫比較。

測試使用既有離線 ApplicationDbContextFactory（阻擋資料庫開啟），只讀 EF metadata、ChangeTracker 及 ToQueryString；不執行查詢／SaveChanges／EnsureCreated／Migration。另以記憶體 store 呼叫原教師 Service 的 Draft 建立、更新、重讀、Submit、相同 key 重送及收據重讀，確認無需關係輸入，欄位保持 null，仍使用原教師 Hash；HashVersion 在提交／重送／收據後保持 NULL。Fixture 不證明真資料庫交易、鎖或回滾。

```powershell
dotnet restore tests/RespondentRelationshipModel/RespondentRelationshipModelHarness.csproj --source tests/RespondentRelationshipModel -p:NuGetAudit=false
dotnet run --project tests/RespondentRelationshipModel/RespondentRelationshipModelHarness.csproj --no-restore
```

只使用本機套件快取；需 SDK／targeting pack。API ProjectReference 會編譯 API，但不執行 Program，不啟動 Host／載入 secrets。產生忽略的 bin／obj。max length 是 EF metadata／資料庫規格，不宣稱 EF 會在 SaveChanges 前自動驗證長度。

重要限制：

- 舊資料未知值保持 NULL，不填 FATHER／MOTHER／Guardian 關係；本批沒有資料庫寫入、資料遷移或正式家長關係儲存接線。
- 原有教師邏輯、DTO、Gateway、Validator／Hash、DI、Program 均不修改。正式資料庫相容仍需 Schema 升級後驗收。
- 所有五筆歷史 Migration／Designer 與 Snapshot 保留原樣。模型現在有更多未遷移差異；這是準備狀態，不能宣稱模型與 Snapshot 同步。
- 舊 ParentAuthorSchemaProposal／Phase22 的 exact-six-operations gate 僅承認作者提案，現在還有兩個關係欄位，不能沿用其 PASS 或跳過 gate。此次新增獨立模型／教師測試，不改舊提案或放寬其檢查；後續另核准統整模型差異。
- 實際 EF 查詢已會引用關係／hash_version 新欄位；舊庫缺欄位時查詢／寫入可能失敗。因此不要拿本批模型直接接現有 DB。正式導入需共同 Schema、核准的增量 Migration、舊列 NULL 與教師資料驗證、備份／回復方案及隔離 MySQL 驗收。
- 直接序列化 Response Entity 會多出 nullable HashVersion；既有公開 DTO 不變，正式 API 仍應回傳受限 DTO，不直接曝露 Entity／內部 Hash metadata。
- 家長 HashVersion 的 writer／reader 接線、未知／不支援版本的安全拒絕、舊 Hash 相容策略與 HTTP code 尚待完成；不可因加欄位就回成功重送收據。舊六項作者 proposal gate 也不包含此次版本欄位，不修改或放寬它。
- Guardian／Consent／資格快照／共同鎖序、家長 Submit Revision／Hash／關係保存接線仍未完成。所有 MySQL80／3306 或其他 DB 操作均不在本批範圍。
