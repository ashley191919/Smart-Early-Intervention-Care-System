# Parent Submit／Idempotency 就緒度（離線缺口 Harness）

固定基準 `7170c475edf31af315a499fa98ed41cf9fc1dfe3`。只新增此測試目錄，不修改正式 Gateway、DI、Controller、模型、教師 Hash 或 Migration。

## 執行

從 Worktree 根目錄使用本機快取還原，明確指定本地 source，不向 NuGet 網路來源要求套件：

```powershell
dotnet restore tests/ParentSubmitReadiness/ParentSubmitReadinessHarness.csproj --source tests/ParentSubmitReadiness -p:NuGetAudit=false
dotnet run --project tests/ParentSubmitReadiness/ParentSubmitReadinessHarness.csproj --no-restore
```

缺少快取套件時停止，不下載或安裝新依賴。ProjectReference 只編譯／載入 API 型別，啟動的是 Harness 的 Main，沒有執行 API 的 Program。所有交易介面注入記憶體 Fake，`DbContext` 為 null，沒有建立 Connection、EF Provider、HTTP Host 或 Socket。

程式設定退出碼：0 = 無 FAIL／BLOCKED；1 = 非預期斷言失敗；2 = 無 FAIL 但仍有 BLOCKED。`dotnet run` 外層可能將非零退出碼呈現為 1；以 SUMMARY 區分原因。**目前預期非零，不是完整 Submit 驗收通過。**

## 本次執行與狀態

**7 PASS、0 FAIL、9 BLOCKED**。共 14 個實際探測（含一個方法簽章探測），另列 2 個尚未執行的整合阻塞；不能說成 16 項行為測試全部通過。

| 情境 | 結果 | 實際證據與界線 |
|---|---|---|
| NULL HashVersion 重送 | BLOCKED | 真正舊 Gateway 回成功收據，尚未實作要求的 `IDEMPOTENCY_VERSION_UNSUPPORTED`／409 |
| 不支援版本重送 | BLOCKED | 同上，不猜測或重算版本 |
| 版本大小寫差異 | BLOCKED | 同上，未落實精確版本辨識 |
| 版本尾端空白 | BLOCKED | 同上，不能當成受支援版本 |
| 已授權歷史收據讀取 | PASS | 實際 GetReceipt 不等於 Submit 重送核可；不修改 NULL 元資料 |
| 作者＋Task＋Key 相同且 canonical v1 內容正規化相同 | BLOCKED | 合成 v1 Response 交給真正 Gateway，卻得到舊 Hash 的 `IDEMPOTENCY_CONFLICT`／409 |
| Command 改變 ExpectedRevision、內容經正規化相同 | PASS（純組合） | 真正 Command → Validator → encoder bytes 相同；沒有實作重送判定 |
| 跨作者且版本未知 | PASS（Fake scope） | GetReceipt 與 Submit 均 404，不洩漏版本錯誤 |
| 無授權且版本未知 | PASS（Fake scope） | 404，未查詢 Key、未 Commit |
| 未提供正式 Adapter | PASS | 使用真正 DenyParentTaskAccessAdapter，讀取與重送均拒絕／503 |
| 儲存失敗且已有 NULL Relationship 草稿 | PASS（Fake only） | 呼叫真正 Gateway，失敗保留草稿 Revision／關係與 Task，無 Response |
| 最終 revalidation 拒絕 | PASS（Fake only） | 同上，無 Commit；不代表撤銷並行安全 |
| 首次 Revision 與成功重送優先順序 | BLOCKED | 真正 Submit 尚無 ParentSubmitCommand／ExpectedRevision 參數；未寫替身 Submit 假裝驗證 |
| 新提交 Relationship／canonical v1／HashVersion 保存 | BLOCKED | 真正舊 Submit 仍存 NULL 關係／版本與舊 Hash；不得啟用該路徑作為新版入口 |
| 真正 Guardian／Consent 資格世代與撤銷競態 | BLOCKED（未執行） | Adapter／共同寫入鎖協定未交付 |
| EF／MySQL 原子性、唯一鍵、併發、死鎖 | BLOCKED（未執行） | Fake copy-on-commit 不具引擎級交易語意 |

BLOCKED 行為探測會核對觀察到的已知舊行為；若既有程式變成另一種結果則 FAIL，不能靜默略過。版本拒絕若未來實作正確會成為 PASS；僅增加 Command 簽章時會 FAIL，要求補真正 Revision／順序行為測試，不會因方法存在就通過。

## 重用既有測試，不重做演算法

- [ParentQuestionnaire](../ParentQuestionnaire/Program.cs)：已有舊流程首次提交、同 Key 重送、不同內容 409、另一 Task Key 衝突、作者／授權、Fake Commit 失敗與 revalidation。**本輪未重跑，不能當成新版 Submit 已通過。**其「六個 author schema 操作」舊模型斷言亦不涵蓋後增的 Relationship／HashVersion，未擅自調整該 gate。
- [ParentCanonicalHash](../ParentCanonicalHash/Program.cs)：既有 canonical bytes golden vector、Relationship、排序、Unicode／Culture／邊界測試，本輪不複製。
- [RevisionConversion](../RevisionConversion/Program.cs)、[ParentBridgeMapper](../ParentBridgeMapper/Program.cs)：重用現有嚴格 Revision／DTO 元件，不另寫 parsing／Mapper。
- [ParentIntegrationContract](../ParentIntegrationContract/Program.cs)：固定介面及預設拒絕；本輪不把該新介面假設成舊 Gateway 已採用。
- [EfParentStore](../EfParentStore/Program.cs)：EF＋替身 ADO 的歷史覆蓋，不是真正資料庫；本輪未重跑。

「同 Key 不同內容」的舊流程測試已存在，不複製；**canonical v1 的正式衝突判定仍 BLOCKED**，因相同內容的基線尚不能成功重送。不得用舊 Gateway 的任意 409 當作新版內容比較正確。

## 最小後續修正建議（本輪未實作）

1. 另行核准家長 Command 入口／coordinator；保留教師 SubmissionHash。先真正授權與作者遮蔽，再核對 Task／Key／受支援版本與 canonical 內容。NULL／不支援版本拒絕 409，不回填或猜測。
2. 只有首次提交才比較 `draft?.Revision ?? 0`；成功重送先通過作者、Task、Key、相同 canonical 內容及資格檢查，不因 ExpectedRevision 改變失敗。
3. 首次提交保存明確 Relationship、現有 `earlycare.parent-submit.canonical/v1` 與對應 Hash；Relationship 不得由 Guardian 推定。重送也必須核對合法且相同的內容關係。
4. 未完成正式授權 Adapter、資格變更偵測與共同鎖序前保持預設拒絕。不要將歷史收據讀取的資格與成功重送的 Hash 條件合併。
5. 模型增量 Migration／Snapshot、唯一鍵／隔離 MySQL 與回滾／併發驗收需另行核准；保留五份 History Migration，禁止連未升級舊庫或使用 MySQL80／3306。

內部錯誤碼／Status 是直接觀察 WorkflowException，並非 Controller HTTP 整合驗收。Fake Adapter 沒有驗證真實身分儲存、Consent、Scope 或鎖定；Fake Commit 只在最後複製狀態，不證明 EF SaveChanges 失敗回滾或網路中斷後 Commit 結果。
