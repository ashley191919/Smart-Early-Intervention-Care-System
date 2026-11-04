# ParentBridgeMapper：最新 pinned DTO 對齊（未正式接線）

16 基準：`3a67edb573b521d402cd75c631562fa54a817cd3`。
11 最新已驗證固定 Commit：`26e8b7fb09fdaa24292c17a4f58d7ea4ce536491`。
本批僅工具／離線測試，不是正式 Bridge、Controller、Adapter 或 DI。Context／GuardianId 是伺服器資料，不是授權；沒有 Stamp、世代或交易實作。

## 本批範圍與兩個明確版本

只允許改本目錄，因此沒有修改 `tests/ParentBridgeMapper/` 的歷史 pinned DTO／專案。新 pinned 定義與 Harness 放在 [OfflineLatest](OfflineLatest/ParentBridgeLatestHarness.csproj)。

- **最新對齊**：OfflineLatest 專案明確定義 `PARENT_DTO_26E8B7F`，編譯同一份 [Mapper](ParentBridgeDtoMapper.cs) 的新版分支；MapDraft／MapSubmit 直接呼叫 11 pinned `ParentCorePayloadMapping.PrepareDraft／PrepareSubmit`，Revision 只解析一次。
- **歷史相容回歸**：原 `tests/ParentBridgeMapper` 沒有上述符號，沿用 e2affb5 的六欄 Context、舊例外型別與 null Answers 轉換行為。它的 67 PASS **不是最新 11 契約驗收**。這個相容分支只為避免越界修改既有測試，不是正式 fallback。
- 這是測試編譯選擇，不是 API Runtime 設定。兩條分支都沒有被正式 API 專案納入；不得因定義符號就聲稱完成安全接線。若未來要移除歷史分支／更新舊測試，需另批授權包含該目錄。

## 固定來源

使用 GitHub 唯讀工具取得固定 Commit，沒有 Fetch、複製其他 Worktree 或讀取未提交原始碼。

| Fixture | 原始碼／Git blob | 擷取範圍 |
|---|---|---|
| [Pinned11Dtos.cs](OfflineLatest/Pinned11Dtos.cs) | `ParentModule/ParentQuestionnaireContracts.cs`；`ed5df3eebd68f08c892a6c93ceeff2e470fed4ae` | ParentGatewayContract、五個 DTO／Context record、四方法 IParentQuestionnaireGateway 原宣告；不含存取服務或 Gateway 實作 |
| [Pinned11PayloadMapping.cs](OfflineLatest/Pinned11PayloadMapping.cs) | `ParentModule/ParentCorePayloadMapping.cs`；`30bfe65d72f56a9eb88a2b18358fc50183fd849e` | 加 metadata header 後完整保留原內容；測試驗證去 header 後的 Git blob 雜湊 |
| [Pinned11CaseAccessException.cs](OfflineLatest/Pinned11CaseAccessException.cs) | `Services/Cases/CaseAccessService.cs`；`6e3122f03682f9588e423418e8f29f2655f6a449` | 只擷取原例外型別宣告，不包含授權實作 |

路徑皆相對 11 固定 Commit 的 `EarlyInterventionCare.Api/`；Fixture 僅限測試，不能發布為正式 DTO／授權模組。Git blob 校驗使用 SHA-1 僅為來源識別，與正式提交 SHA-256 canonical Hash 無關。

最新 Context 精確為七欄：Principal、ActorUserId、OrganizationId、CaseId、ConsentId、DocumentVersionId、GuardianId；沒有 EligibilityStamp。Draft request／response 的 Relationship 為 `string?`；Submit 仍必填。Revision 在 HTTP 是字串，核心是 ulong。

## 純資料方法與界線

- `MapDraft(ParentDraftRequest)` → `ParentSaveDraftCommand`；`MapSubmit(ParentSubmitRequest)` → `ParentSubmitCommand`。沿用 11 準備語意：嚴格 Revision、原七種關係代碼、草稿可 null、提交必填、Answers 不得為 null，複製答案陣列一次；不擅自排序或正規化姓名、日期、Observation／答案。
- `PreserveContext` 回原 Context 物件，包含 GuardianId；`GetOriginalPrincipal` 回原 Principal，不用 Context ID 建 Claims。匿名仍匿名；這兩個函式不執行身分／Scope 授權，Context 不得序列化回瀏覽器。
- 新版 `MapDraftResult(ParentDraftResult, taskId, versionId)` 重用 pinned RestoreDraft，Relationship 必須由未來已授權持久化結果提供。NULL 原樣保留，不從 Guardian／瀏覽器輸入推定，也不另查 Entity。
- 新版 `MapReceipt(ParentQuestionnaireReceipt, taskId, versionId)` 映射六個收據欄位；檢查非空 ResponseId、預期 Task／Version、SUBMITTED、UTC DateTime。異常使用既有 `PARENT_CORE_RESPONSE_INVALID`／503 語意，訊息不包含輸入細節。回傳收據不表示本工具已驗證權限。
- 最新格式錯誤沿用 11 `CaseAccessException(INVALID_INPUT,400,固定訊息)`；null 函式參數使用 ArgumentNullException。例外／測試失敗報告不輸出原始 DTO、Claims 或敏感標記。
- 未提供 ParentGatewayCallContext／AccessExpectation 轉換：缺少真正 Stamp 時明確 BLOCKED，不能補空字串、常數或用 Context ID 當 Stamp。
- 沒有開 Socket、開交易、授權放行、Store、SQL、HTTP 呼叫或 canonical Hash。尤其不引用 Fake Gateway 的 JSON Hash。

## 離線使用

從 Worktree 根目錄執行，restore 僅使用本機 source／快取；缺套件就停止，不安裝第三方套件或讀私人設定：

```powershell
dotnet restore tools/ParentBridgeMapper/OfflineLatest/ParentBridgeLatestHarness.csproj --source tools/ParentBridgeMapper/OfflineLatest -p:NuGetAudit=false
dotnet build tools/ParentBridgeMapper/OfflineLatest/ParentBridgeLatestHarness.csproj --no-restore
dotnet tools/ParentBridgeMapper/OfflineLatest/bin/Debug/net8.0/ParentBridgeLatestHarness.dll
```

最新結果：**96 PASS、0 FAIL、2 BLOCKED**；建置 0 警告／0 錯誤。程式成功完成離線斷言但存在整合阻塞時 exit 2，非全部就緒；失敗 exit 1。部分 dotnet／shell 外層可能呈現為其他非零，請連同 RESULT 判讀。

測試涵蓋七欄／nullable metadata、原 Principal 與 GuardianId、嚴格 Revision 邊界（含 2^53±邊界及 ulong.MaxValue）、JSON 字串型別、缺關係／缺答案、複製陣列、Draft／Receipt 欄位／UTC／隱私、Culture 與不回顯合成敏感標記。也確認 Mapper 不在正式 API assembly、工具沒有 Gateway／AccessAdapter 實作。只讀取本測試目錄的公開 pinned 原始碼作雜湊校驗，不讀環境變數、appsettings、User Secrets 或連線字串。

歷史回歸（未修改其檔案）：

```powershell
dotnet run --project tests/ParentBridgeMapper/ParentBridgeMapperHarness.csproj --no-restore
```

結果 **67 PASS、0 FAIL**，僅驗證歷史相容分支，不能與最新契約測試混稱正式跨模組驗收。ProjectReference 只編譯／載入 API 型別，不執行 API Program；只有被 Git 忽略的 bin／obj 建置產物。

## 尚未完成

1. 11 的真正 EligibilityStamp／世代／交易 Adapter 未交付；七欄 Context 本身不足以建立可信 AccessExpectation。正式 Context 承載方案仍待共同固定。
2. 真實 Bridge、guarded Draft／Receipt、共同 Schema／Migration、資格鎖序、HTTP／資料庫保存／並行均未整合驗收。原始碼 fixture／純轉換 PASS 不能代替 Controller、Guardian／Consent 或 MySQL 證據。

未新增正式服務入口、反射注入或 DI，不改家長 Submit、教師 Hash、既有模型／Migration，也不解除 DenyAll／Development 限制。本批完成後停止，等待審查。
