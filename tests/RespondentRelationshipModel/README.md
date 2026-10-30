# RespondentRelationship 最小模型離線驗證

基準 `35da12a86c363e532aef56273acade63bf127199`。本批只修改 QuestionnaireDraft／QuestionnaireResponse 及 ParentQuestionnaireModelConfiguration：新增 `string? RespondentRelationship`，映射 `respondent_relationship varchar(32) NULL`，無 default／computed／generated 值，無新增索引／FK／Required 或 backfill。

測試使用既有離線 ApplicationDbContextFactory（阻擋資料庫開啟），只讀 EF metadata、ChangeTracker 及 ToQueryString；不執行查詢／SaveChanges／EnsureCreated／Migration。另以記憶體 store 呼叫原教師 Service 的 Draft 建立、更新、重讀、Submit、相同 key 重送及收據重讀，確認無需關係輸入，欄位保持 null，仍使用原教師 Hash。Fixture 不證明真資料庫交易、鎖或回滾。

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
- 實際 EF 查詢已會引用新欄位；舊庫缺欄位時查詢／寫入可能失敗。因此不要拿本批模型直接接現有 DB。正式導入需共同 Schema、核准的增量 Migration、舊列 NULL 與教師資料驗證、備份／回復方案及隔離 MySQL 驗收。
- Guardian／Consent／資格快照／共同鎖序、家長 Submit Revision／Hash／關係保存接線仍未完成。所有 MySQL80／3306 或其他 DB 操作均不在本批範圍。
