# Parent Gateway Bridge 安全骨架（僅獨立 Contract Harness）

16 基準：`7045c776402dd5a7ea7baff3f49ac5ed96c299db`。
11 固定契約：`26e8b7fb09fdaa24292c17a4f58d7ea4ce536491`。

**此骨架沒有正式接線，四方法一律拒絕。不是已能使用的 Bridge，也不是 Guardian／Consent 或資料庫驗收。**

## 為何採工具骨架

16 正式 API 尚無 11 的 ParentRequestContext／IParentQuestionnaireGateway 型別。不得把測試型別複製為假正式 DTO，因此只在 [獨立測試專案](OfflineContract/ParentGatewayBridgeContractHarness.csproj) 連結既有 [pinned 定義](../ParentBridgeMapper/OfflineLatest/Pinned11Dtos.cs)、[原 PayloadMapping](../ParentBridgeMapper/OfflineLatest/Pinned11PayloadMapping.cs) 與 [Mapper](../ParentBridgeMapper/ParentBridgeDtoMapper.cs)。固定來源與 blob 見 [Mapper 文件](../ParentBridgeMapper/README.md)。沒有再新增一份 DTO，也沒有引入 11 未提交的 Adapter。

`PARENT_DTO_26E8B7F` 只在本獨立 Harness 選擇最新版 Mapper 語意；不是正式 API 的啟用開關。ProjectReference 僅編譯／載入 16 型別，不執行 API Program。骨架與 pinned 宣告沒有進入正式 API 編譯。

## 四個方法目前的行為

[ParentGatewayBridgeSkeleton.cs](ParentGatewayBridgeSkeleton.cs) 是 internal sealed 類別，只編譯於測試 assembly。四個方法保留 11 的參數、結果型別與 CancellationToken，全部以 faulted Task 回 `QuestionnaireWorkflowException(PARENT_ACCESS_NOT_INTEGRATED,503,固定訊息)`。

| 方法 | 本輪可驗證 | 尚未實作 |
|---|---|---|
| GetDraftAsync | 簽章、缺資格時拒絕；獨立 DraftResult 映射 | guarded 交易內讀取、保存關係的回傳來源 |
| SaveDraftAsync | 簽章、缺資格時拒絕；純 DTO → ParentSaveDraftCommand | guarded 交易、Relationship／Revision 儲存 |
| SubmitAsync | 簽章、缺資格時拒絕；純 DTO → ParentSubmitCommand | 真正資格證據及呼叫既有 SubmitCommandAsync |
| GetReceiptAsync | 簽章、缺資格時拒絕；純 receipt 欄位映射 | guarded 歷史查詢，且與成功重送判定分開 |

沒有 Store、Adapter、委派或資格旗標建構參數，沒有可開啟的分支；不會呼叫舊公開 GetDraft／SaveDraft／Submit／GetReceipt 來繞過缺少新版資格證據的情況。所有輸入（包含匿名、其他作者、null 或 cancelled token）均維持 unavailable／503，未讀資料；**不能將這項拒絕測試稱為作者驗證或 404／409 業務判定已通過**。

測試不偽造 EligibilityStamp、AccessExpectation 或交易證據，不建立 Fake Store／Fake Adapter。合成 ClaimsPrincipal 只供拒絕／原參考保留檢查，從未用它放行儲存。

## 未來真正轉接的最小計畫（本輪未執行）

1. 11 正式型別進入另行核准的整合版本後，Bridge 改用真正型別，不能發布 pinned Fixture。四個 HTTP Gateway 方法不改名、不另建正式 DTO。
2. 11 以原始 Principal 產生真正預檢證據，包含 GuardianId 及經共同確認的 Stamp 承載；缺少證據即拒絕，不由 Context ID 合成身分、不以空字串／常數補 Stamp。
3. 16 補 guarded Draft／Save／Receipt 核心。使用既有 IParentQuestionnaireStore／IParentQuestionnaireTransaction，同一 scoped DbContext／既有交易；11 Adapter 不 Begin／Commit。讀取也必須驗證資格，不能以 writing=false 當旁路。
4. Save 使用 ParentSaveDraftCommand 保存 nullable Relationship；GetDraft 的 ParentDraftResult 必須從已授權交易取得關係，不在 Commit 後另查 Entity 拼資料，也不由目前 Guardian 關係推定。
5. Submit 將既有 Mapper 產生的完整 ParentSubmitCommand（含 ExpectedRevision／Relationship）及原 Key，傳給實際存在的 internal ParentQuestionnaireGateway.SubmitCommandAsync。不可只傳 Payload 到舊公開 Submit。
6. 由既有核心負責首次 Revision、canonical v1／HashVersion、原子 Response＋Task，以及同作者／Task／Key／內容重送。Bridge 不計算另一套 Hash、不自動換 Key；不採用 Fake Gateway 的 JSON Hash。
7. 歷史收據只走已授權 receipt 核心；不能因未知 HashVersion 製造成功提交回執，也不應套用成功重送的內容比較來替代歷史查閱。
8. 11 預檢與 16 核心均須保留跨作者遮蔽，作者／存取驗證早於狀態、版本及 HashVersion 細節。資格改變、未知版本及新 Key 的錯誤依已確認契約處理，前端不自動重送。
9. 先固定全部資格寫入端、世代／共同鎖序／fresh reads 與共同 Schema，再另行核准隔離 MySQL／HTTP 驗收及 DI。不得因骨架測試通過就註冊正式服務或解除 DenyAll／Development gate。

上述是後續工作分界，不假設 guarded Draft／Receipt 已存在；本輪既有核心、模型、DI、Controller、Program 與 Migration 全未修改。

## 離線執行

從 Worktree 根目錄執行；restore 只用本地 source／套件快取，缺套件時停止，不下載新依賴：

```powershell
dotnet restore tools/ParentGatewayBridge/OfflineContract/ParentGatewayBridgeContractHarness.csproj --source tools/ParentGatewayBridge/OfflineContract -p:NuGetAudit=false
dotnet build tools/ParentGatewayBridge/OfflineContract/ParentGatewayBridgeContractHarness.csproj --no-restore
dotnet tools/ParentGatewayBridge/OfflineContract/bin/Debug/net8.0/ParentGatewayBridgeContractHarness.dll
```

本輪 **38 PASS、0 FAIL、3 BLOCKED**；建置 0 警告／0 錯誤。程式無斷言失敗但尚有 BLOCKED 時退出碼 2（外層可能轉為其他非零）；真正斷言失敗為 1。不能當成整合成功。

覆蓋四方法精確簽章、全拒絕、變更作者／Key／內容與取消不產生旁路、原 Principal／Guardian 保留、DTO 映射、NULL 草稿關係、收據不含答案／作者／HashVersion、正式 API 無此型別、沒有第二套 Store／授權／提交引擎。

Bridge 骨架不查閱任何 Response，所以不能宣稱它已驗證跨作者 404、未知版本 409、不同內容衝突或歷史收據授權；這些必須由未來真 Bridge 接入既有核心後驗收。既有 [ParentSubmitReadiness](../../tests/ParentSubmitReadiness/README.md) 的 Fake 核心測試可作分層回歸，不能混算 Bridge／MySQL 驗收。

## BLOCKED 與責任

| 阻塞 | 主責與前置 |
|---|---|
| 真正 Context／Stamp／世代／Guardian／Consent Adapter | 11；16 共審。原七欄 Context 沒有 Stamp；資格鎖序與撤銷寫入端尚未落地 |
| guarded Draft／Receipt、真正 Bridge forwarding | 16；沿用既有 Store／核心，不退回舊 Adapter。共同契約確認後另批實作 |
| Schema／HTTP／MySQL／跨模組資格、原子性及重送驗收 | 三人；必要增量／目標實例／執行另行核准。07 Task／Workflow 寫入端須遵守共同協定 |

院方 Consent／撤銷／重新同意政策仍待確認。骨架不決定可見範圍、不自動更新 CaseStatus／History，也不修改教師 Hash。全程未啟動 API／Docker、未讀私人設定或連接資料庫。
