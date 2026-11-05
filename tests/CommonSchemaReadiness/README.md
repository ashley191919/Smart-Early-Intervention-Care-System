# V5.3 共同 Schema／Migration 離線就緒度 Harness

## Runtime Model 第一批版本：User stage-one（優先於下方歷史結果）

整合起點為16 `e879649985d62465b5911194850e3a7269140990`；User與IdentityModelConfiguration逐項沿用11 `d268b7d70be33e04d44bdfa044b13339448815f6`。只新增四個nullable屬性及NormalizedEmail非唯一索引；不接登入、Controller、DI或任何資格流程。**新模型不可連接未升級資料庫；沒有產生或執行Migration。**

| 契約版本 | 實際差異與檢查方式 |
|---|---|
| author-v1 | 原9項白名單與30組負向案例完全保留。從真實Snapshot→目前模型的14項操作中，分出User操作後仍須精確符合原9項；不是讓14項任意通過舊9項gate |
| user-v1 | [UserStageOne.cs](UserStageOne.cs)獨立精確要求4個nullable AddColumn＋1個非唯一CreateIndex，檢查provider metadata及相依順序 |
| model-v2 | 合計必須精確為14項；User分組過寬或出現第六項User變更也會被精確集合拒絕 |
| 固定11完整模型 | 原40／43重現及23項政策BLOCKED保持不變；User進入本機不等於其餘Case／Guardian／Consent已整合 |

User四欄為phone_verified_at_utc datetime(6) NULL、email varchar(254) NULL、normalized_email varchar(254) NULL（utf8mb4_bin）、email_verified_at_utc datetime(6) NULL；無default／推定驗證時間。Email沿用users表utf8mb4_unicode_ci，屬性未明訂Collation時測試檢查其有效繼承值。索引ix_users_normalized_email(normalized_email)明確非唯一。

本輪新增20項檢查，結果為**158 PASS、0 FAIL、28 BLOCKED，退出碼2**；原138項保留（其中V03／V07改以隔離出的原9項驗證，整體另驗14項）。包含User未設定值保持NULL、既有Username／Password／Status／Role／Organization約束、來源blob及9組User負向案例。原SchemaWhitelist.IsExact不放寬；既有教師／舊家長Schema gate不修改。

正式AuthenticationService、醫療Session／RBAC／Program／DI沒有修改。登入離線回歸的SyntheticIdentityReader僅補四個明確NULL欄位以物化目前User，沒有回填或視為已驗證。本輪測試記錄與實際回歸範圍見本次工作回報；純離線結果不代表真實DB登入或前端驗收。

下方47／138項結果及「實際9項」為前一版本歷史；目前應採上述158項及14項模型差異。

基準：16 `8f03f8bd40e41eaf63dd8d6fd828052430b88113`。共同模型參考為11 `c213e051034c5bd6d82bdd90a6aae7818e600f0c`；本工具不下載、不合併該分支，也不把 Pinned11 當成本機正式模型。

本工具只讀取固定來源與建立 EF 記憶體 relational model／MigrationOperation 清單。**不產生 Migration、Designer、Snapshot 或 SQL，不呼叫 EnsureCreated／Migrate，不啟動 API，不連線任何資料庫。**編譯產物限一般 bin／obj，不修改正式來源。

## 執行

於16 Worktree根目錄：

```powershell
dotnet restore tests/CommonSchemaReadiness/CommonSchemaReadinessHarness.csproj --source tests/CommonSchemaReadiness -p:NuGetAudit=false
dotnet build tests/CommonSchemaReadiness/CommonSchemaReadinessHarness.csproj --no-restore
dotnet tests/CommonSchemaReadiness/bin/Debug/net8.0/CommonSchemaReadinessHarness.dll
$LASTEXITCODE
```

restore只指定本機測試目錄，使用已快取套件；若缺少依賴則停止，不新增套件來源。重用正式API ProjectReference與現有[OfflineAdo](../EfParentStore/OfflineAdo.cs)，不新增第三方依賴。ProjectReference只載入組件型別，不執行API EntryPoint。

原始本機套件：47 PASS、0 FAIL、5 BLOCKED。擴充後本輪實跑：**138 PASS、0 FAIL、28 BLOCKED；退出碼2**，其中原47項全部保留。建置0警告、0錯誤；使用的SDK為已安裝10.0 RC，NETSDK1057為預覽SDK資訊，不是正式SDK版本核定。

退出碼：1＝FAIL；2＝無FAIL但尚有BLOCKED。固定保留五個整合阻擋項，另逐項輸出23個政策BLOCKED，不能把exit2改成0以美化結果。

## 已執行的離線檢查

| 規格 | 實作 |
|---|---|
| V01 | 五筆歷史Migration與Designer共10個固定Git blob、Snapshot blob、組件Migration ID與順序。UTF-8嚴格解碼，CRLF正規化為LF後計算Git blob；不是原始磁碟bytes雜湊 |
| V02 | 真實第五筆Migration.TargetModel與真Snapshot經EF初始化後比較，要求relational differ為零，兩者都包含case_status_histories |
| V03～V06 | 真Snapshot對真Runtime design model執行IMigrationsModelDiffer；精確要求五個AddColumn、兩個CreateIndex、兩個AddForeignKey |
| V07～V08 | 精確加法差異排除既有結構修改；額外核對教師Key、Task唯一鍵、Grant／版本FK與History Restrict FK |
| V11、V14 | 30組負向操作測試，含同數量替換、額外欄位、錯FK／Cascade、型別／NULL／長度／字元集／Collation、default／computed、額外annotation、索引順序／唯一性、SQL回填及Insert／Update／DeleteData、Drop、缺漏／重複、依賴順序 |
| V15 | 使用無Socket能力的OfflineConnection，明確MySQL 8.0.46，不使用AutoDetect／連線字串／私人設定；要求連線Closed、事件0、ChangeTracker空 |

白名單採精確metadata簽章，檢查Schema、欄位／索引／FK名稱及順序、CLR／SQL型別、NULL、長度、default／computed、刪除／更新行為與provider annotations。新欄位必須先於使用它的索引／FK；彼此獨立的操作不強制單一排列。

初次為45 PASS／2 FAIL：預期字串欄位漏列既有表繼承的utf8mb4與utf8mb4_unicode_ci。核對現有Mapping及真實differ後補齊測試白名單，未更改正式模型。作者UUID仍固定ascii／ascii_bin；HashVersion仍為既有表collation，不以資料庫預設collation取代應用程式ordinal版本比較。

## 明確的九項白名單

- Draft／Response各新增parent_author_user_id char(36) NULL，ascii／ascii_bin；各Restrict FK至users.user_id。
- Draft／Response各新增respondent_relationship varchar(32) NULL；Response新增hash_version varchar(64) NULL。三欄utf8mb4／utf8mb4_unicode_ci，無預設／計算／回填。
- Draft新增非唯一ix_drafts_parent_author(parent_author_user_id)。
- Response新增唯一uq_responses_parent_key(parent_author_user_id,idempotency_key)。

任何History建表／刪表、既有約束變更、資料修復或其他操作均不在本白名單。本工具只檢查操作清單，不輸出可執行DDL。既有Migration的Up本體從未執行。

## 五個BLOCKED

1. V09／V10：11完整共同模型尚未進本機正式來源；不能宣稱已驗證完整API／共同模型。
2. Case舊資料必填與Consent再同意／歷史政策尚待11、團隊或院方確認。
3. 資格世代、ABA、Stamp與全域鎖序尚未核定／完成。
4. V12／V13：未來增量Migration、Designer、Snapshot與SQL尚未產生／審查；目前只核對既有歷史順序。
5. 真實MySQL 8.0.46的FK／唯一鍵／computed／Collation、資料保存、並行及復原均未執行。

47個PASS包含檔案完整性與負向檢查，**不代表47項真實資料庫測試**。SQL與DataOperation負向案例僅為記憶體物件，未交給SQL產生器或執行器。

既有[教師Schema gate](../QuestionnairePhase22Harness.cs)與[舊家長Schema gate](../ParentQuestionnaire/Program.cs)完全不改；本工具的九項差異PASS不取代它們的既有FAIL／後續BLOCKED。亦未修改仍只接受六項舊提案的[ParentAuthorSchemaProposal](../ParentAuthorSchemaProposal.cs)。

本工具不證明外部實例、帳號或權限已就緒；不讀取／處理既有未追蹤檔案。歷史或模型改變時失敗須先審查，不能自動更新固定雜湊或擴大白名單。

下一步由16在獲授權後，針對核准的共同模型增加另一份精確白名單與負向測試；保留本機九項驗證。11確認Case／Consent／Guardian語意，07確認Scope／輪次／Workflow依賴。Migration產生、套用與MySQL驗收仍須分別核准。

## 11／16共同候選擴充：固定d268b7d

實際固定來源：11 `d268b7d70be33e04d44bdfa044b13339448815f6`。來源路徑、blob與本地對應見[union-sources.json](union-sources.json)；[candidate-review.json](candidate-review.json)為該Commit原始報告，未改寫成核准白名單。

新[UnionModel組件](UnionModel/UnionCandidateModel.csproj)只編譯八份未改寫的Pinned11來源（Case／User／Guardian／Consent／Mapping／Context／舊Snapshot），連結31份與該Commit blob一致的本機模型、歷史Migration及必要依賴。所有快照以git cat-file原始bytes保存，執行時核對40筆blob（39份來源＋報告）；不使用11的ApplicationDbContextFactory或會輸出檔案的ParentSchemaReview入口。

此組件與正式API是不同組件，主Harness以`extern alias Union`參照，避免同名ApplicationDbContext混用；Pinned11不會進入正式API編譯或DI。不可將Pinned11直接複製成正式來源。它只用閉世界OfflineAdo替身，顯式MySQL8.0.46，無Socket、私人設定或服務入口。

真正執行`IMigrationsModelDiffer`：第五筆TargetModel→固定11模型為40項；舊11 Snapshot→同模型為43項，多出的只有History表與兩個索引。這是記憶體操作清單，沒有scaffolding、SQL產生、Up／Down、資料表建立或任何DB連線。

| 分類 | 實際涵蓋 | 輸出意義 |
|---|---|---|
| 9項OVERLAP | 作者五欄、兩索引、兩FK | 再用原SchemaWhitelist精確比對，確認只出現一次 |
| 8項REVIEW_ONLY | User四個nullable欄位＋非唯一Email索引；Case nullable院所＋索引＋Restrict FK | 只驗候選結構，不代表核准DDL |
| 23項POLICY | Case address／national_id／revision；五張新表與相關15個索引 | 逐項BLOCKED，不列成23項核准PASS |

[CandidateSuite.cs](CandidateSuite.cs)以實際操作名稱、表、類型及固定報告序號追溯全部40項；另驗13欄、5表、19索引、3外加FK的完整集合。5張表內嵌FK也逐一比較，不能只看外層操作數量。

[CandidateContract.cs](CandidateContract.cs)除了逐項比對報告的欄位／NULL／default／charset／Collation／FK／索引，還直接檢查MigrationOperation上JSON省略的CLR型別、MaxLength、default實際型別、computed IsStored、Schema、OnUpdate、索引方向及provider annotations，並驗證新表／欄位先於消費者。計算欄位只接受既有active_case_id；不視為資格世代。未核准的三個Case default只被辨識及列BLOCKED，不被當成安全導入。

27個新增負向案例與原30個均保留：額外或同數量替換DDL、重複History、錯FK／巢狀Cascade／OnUpdate、default變更或型別變更、MaxLength、computed stored、table charset／Collation／annotations、Consent唯一鍵放寬、Email變唯一、DML／SQL回填、操作缺漏／重複及相依順序，均須拒絕。

本輪修正只在測試範圍：補足Probe的EF metadata using；針對舊Snapshot用原始Git blob bytes保存，修正文字逐行重組造成的來源hash差異。沒有修改來源快照內容以配合模型，也沒有修改正式來源／歷史／Snapshot或任何既有gate。

**結構一致的PASS與政策核准不同。**目前仍不能進Migration產生階段：11需核定Case缺漏與default、Guardian唯一性及Consent再同意／歷史；11＋16固定資格／鎖序；07確認Scope／輪次／Workflow需求。三人另行固定共同模型與唯一歷史Snapshot基準後，才可核准產生／審查Migration，套用及MySQL驗收再另行授權。

## 11政策方向：既有規則、候選方案、院方待決

可追溯依據為11固定Commit `d268b7d70be33e04d44bdfa044b13339448815f6` 的 `docs/parent-schema-review-whitelist-20261105.md`，模型來源為 `c213e051034c5bd6d82bdd90a6aae7818e600f0c`。本節沒有假設已收到其他未固定版本或聊天決議；以下方向均不解除23項POLICY BLOCKED。

| 類別 | Case | Guardian | Consent |
|---|---|---|---|
| 既有來源／已確認限制 | 院所欄允許NULL；Revision為資料並發用途，不是資格世代；未知院所不可填目前登入院所 | 現有院所＋電話唯一鍵、有效主要聯絡人計算欄位及唯一鍵；不能推定作者或回填填表關係 | 現有Actor＋RequestId及Actor＋Organization＋DocumentVersion＋ScopeKey唯一鍵；不得刪舊列、取消撤銷或改Key以通過導入 |
| 候選技術方案，尚未核定 | 必填欄可討論分階段nullable後核實導入；Revision初始值另審，不直接採differ的空字串／0預設 | 核對共用電話、主要聯絡人及既有綁定來源後再固定導入方式；computed不是BindingGeneration | 再同意紀錄與歷史結構需共同設計；不能只換RequestId，也不能由Harness選擇新唯一鍵 |
| 老師／院方待決 | 個資必要性、舊資料缺漏處理及保存方式 | 共用電話／聯絡人等涉及業務資格的規則由11帶回確認；技術唯一鍵本身不是授權政策 | 同版本重新同意、撤銷／教師參與撤回後處理、正式本文／Scope與歷史保存政策 |

11先提供逐項確認及固定模型版本，16再更新候選來源與精確斷言；07確認Case消費契約、Scope與輪次／Workflow相依。即使新空庫沒有舊列，也不視為已決定未來升級及歷史保存政策。

## 乾淨工作樹重現條件

- 需要完整儲存庫checkout（含正式API、既有OfflineAdo及歷史來源），不是只複製此資料夾。所有專案引用均為相對路徑，無個人絕對路徑。
- 需要可建置net8.0的SDK、.NET／ASP.NET Core 8 Runtime及既有專案依賴。本輪未變更或安裝SDK。
- 本README的restore刻意限制本機來源：乾淨checkout配合已具備NuGet快取即可重現；**全新電腦沒有快取時會失敗**，須先依團隊核准的套件來源取得依賴，不能宣稱工具可憑空離線下載。
- Pinned11及固定JSON已隨測試來源保存；執行時不需要GitHub、fetch或11的Git物件。來源blob核對使用工作樹檔案，不依賴本機Git索引。
- 本輪以正常restore、重新編譯及DLL執行驗證；沒有建立全新機器或第二checkout，不宣稱跨機器環境已實測。
- 只提交原始碼、固定資料與說明；bin／obj不提交，原有誤產生的未追蹤檔案不在依賴或提交範圍。
