# Parent Submit／Idempotency 離線核心與就緒度

## Guarded Draft／Receipt 本輪增量

基準 HEAD：`80fed9f2b31935830b23ed52380676f13c6f84ef`。本輪 **143 PASS、0 FAIL、3 BLOCKED**，退出碼 **2**；其中原有 71 項仍通過，新增 72 項 guarded 純邏輯／Fake 檢查。主 Harness／API 編譯 0 警告、0 錯誤。下方舊批次數量與基準屬歷史紀錄。

僅新增 Gateway 的 internal `GetDraftCommandAsync`、`SaveDraftCommandAsync`、`GetReceiptCommandAsync`，重用原有 Draft／Receipt 私有核心、Store、Scope、Owner、Finish 與 CommandAccess，不另外建立儲存引擎。舊公開方法保留原簽章、DTO、檢查順序與 legacy Adapter 行為；新版 guarded 方法絕不退回 legacy Adapter。教師 Submit／Hash 未修改。

| 方法 | 本輪純邏輯驗證 | 正式整合狀態 |
|---|---|---|
| GetDraftCommandAsync | 同交易授權後作者遮蔽先於狀態／版本；無草稿 Revision 0；原草稿 NULL Relationship 不推定；最後重驗後才回傳 ParentDraftResult | BLOCKED：真 Adapter／Stamp／世代／鎖序未交付 |
| SaveDraftCommandAsync | nullable ulong ExpectedRevision 必填並精確比對，無草稿以 0；最大值拒絕遞增；合法／NULL Relationship 與正規化內容、Revision、Task 一起提交；失敗不發布記憶體副本 | BLOCKED：真正 MySQL 原子性、撤銷競態尚未驗收 |
| GetReceiptCommandAsync | 歷史查閱與成功重送分離；授權後允許 NULL／未知 HashVersion；不查 Idempotency-Key、不驗內容 Hash、不改舊資料，Replayed=false | BLOCKED：正式 HTTP／Bridge 尚未接線 |

`ParentSaveDraftCommand.Payload.ExpectedRevision` 已是 `ulong?`，不在 Gateway 再寫一套字串 parser。HTTP 的標準無前導零十進位字串必須先經既有 StrictParentRevisionCodec／Mapper；本輪不新增 HTTP 入口、不接受浮點數或轉換寬鬆別名。Relationship 只接受已固定代碼或草稿 NULL；不自動 Trim、推定或回填舊值。

三個方法在預設公開建構下皆於開 Store 前拒絕（`PARENT_ACCESS_NOT_INTEGRATED`／503），即使舊 Adapter 可放行亦無影響。internal 注入僅由本 Harness reflection 測試存取，未新增 friend assembly、公開注入方式、DI 或 Controller 引用。Fake 資格證據只存在測試內，不是正式 Stamp 算法。讀取的 `writing=false` 仍需授權與同交易最後重驗；不是資格旁路。每次操作使用獨立 shim，保留原 Principal 物件。

新增測試包括三方法各自跨作者 Draft／Response 搭配異常狀態、不可用／缺失版本、NULL／未知 HashVersion 的資訊遮蔽；無權限不讀 Task／內容；資格改變拒絕；最後重驗拒絕或 Commit 故障不回成功；新建及更新草稿的 Relationship／Revision；已提交不可再讀寫草稿；舊公開 Save 不覆寫新欄位。Fake Commit 採副本發布，**不是 EF／MySQL 回滾證據**。

本輪再次完整執行舊套件（使用文末捕捉入口）：教師在 `QuestionnairePhase22Harness.cs:367` 的 `no pending schema/model changes` **FAIL／exit 1**；舊家長在 `ParentQuestionnaire/Program.cs:198` 的 `only six additive author schema operations proposed` **FAIL／exit 1**。gate 後續檢查 BLOCKED；未修改、略過 gate 或使用 proposal 模式。兩專案建置均 0 錯誤，保留既有 NU1900 弱點資料來源警告。

其他本輪回歸：Canonical Hash 58 PASS、Adapter Contract 34 PASS、RevisionConversion 53 PASS、最新 Mapper 96 PASS／0 FAIL／2 BLOCKED、Bridge 骨架 38 PASS／0 FAIL／3 BLOCKED、EF Store 86 PASS（EF／Pomelo 搭配替身 ADO，不是真正 MySQL）。Bridge Harness 僅更新一行 BLOCKED 說明，以區分新增內部 guarded 核心與仍未實作的正式轉接；四方法骨架依舊全拒絕。含 BLOCKED 的 Harness 退出碼維持 2。

正式接線仍須 11 提供正式型別／Guardian／Consent Adapter、資格世代、Stamp 與全部資格寫入端協定；16 完成經核准的共同 Schema 與真實交易驗收；07 的 Task／Workflow 資格寫入端須遵循共同協定。未升級庫不得使用新版 EF 模型。Bridge 骨架仍全拒絕，未修改 Program／DI、Controller、Entity、Migration 或 Snapshot；未啟動服務或連接資料庫。

## 前批 Submit 核心紀錄

本輪修改基準：`fc99eb510917e414522e4ca368603ab188a03e0e`。
歷史 7 PASS／9 BLOCKED 記錄保留在該 Commit。本輪針對真正的 internal Command 路徑補行為驗證，不再以缺少方法簽章代替測試。

## 執行與結果

```powershell
dotnet run --project tests/ParentSubmitReadiness/ParentSubmitReadinessHarness.csproj --no-restore
dotnet build tests/ParentSubmitReadiness/ParentSubmitReadinessHarness.csproj --no-restore
```

如尚無 restore assets，只能使用本機快取與本地 source；缺少套件時停止，不下載新依賴：

```powershell
dotnet restore tests/ParentSubmitReadiness/ParentSubmitReadinessHarness.csproj --source tests/ParentSubmitReadiness -p:NuGetAudit=false
```

本輪遮蔽修正後：**71 PASS、0 FAIL、3 BLOCKED**（原 47 項＋24 項遮蔽／相容性情境）。71 項為實際執行的離線情境；3 項是未執行／未交付的整合前置條件，不算已通過。
程式退出碼：0 = 無阻塞；1 = 非預期失敗；2 = 無 FAIL 但仍有 BLOCKED。`dotnet run` 外層可能呈現 exit 1；須與 SUMMARY 一起判讀。建置為 0 警告、0 錯誤。所有測試皆不啟動 API、不建立 Socket 或資料庫連線、不讀私人設定。

## 接線範圍與預設拒絕

[ParentQuestionnaireGateway](../../EarlyInterventionCare.Api/Services/Questionnaires/ParentQuestionnaireGateway.cs) 保留公開舊 `SubmitAsync(ClaimsPrincipal, taskId, key, QuestionnaireSubmission, ct)`。
新增 **internal** `SubmitCommandAsync(ParentGatewayCallContext, taskId, key, ParentSubmitCommand, ct)`，兩者共用同一 SubmitCoreAsync、Store、Task／版本／作者／一致性檢查與收據產生，不建立第二套 Response 引擎。

- 公開建構子、既有 DI 不變；其 Command Adapter 固定預設拒絕，在開啟 Store 前拒絕新版提交。即使舊 IParentTaskAccessAdapter 放行，也不能啟用新版。
- 額外 Adapter 注入建構子為 internal，沒有正式呼叫點。Harness 只透過 reflection 注入合成 IParentTransactionAccessAdapter，未增加 friend assembly、公開注入入口或 DI。
- 私有 operation-local shim 只轉接既有介面，不實作 Guardian／Consent 授權。傳遞同一原始 ClaimsPrincipal、同一交易，提交前將原始資格證據交回同一 Adapter 重驗；由 Gateway Commit。
- Expectation／EligibilityStamp 只做必要欄位與全欄位精確一致性比對，**不是授權 Token**，不生成新 Stamp、不替換新資格、不自動重送。真正 Adapter 仍必須從同一 scoped DbContext／既有交易進行權威讀取。
- 沒有實作新列鎖或資格世代規則。共同鎖序、fresh read、撤綁重綁、Consent 撤銷的所有寫入端協定仍 BLOCKED；本輪不核定舊介面註解中的鎖序。

## 新版跨作者遮蔽順序

僅新版 Command 的 Scope 在以下位置新增作者檢查：原始身分／Adapter 授權成功 → Task 存在、Case／角色範圍符合（取消仍回 404）→ 讀取該 Task 的 Draft／Response 並檢查作者 → 狀態／版本檢查 → 既有共用提交核心。原本的核心作者檢查亦保留。

這些內部讀取僅在已取得存取證據、確認 Task 範圍後執行；只使用作者資訊決定是否回固定 `TASK_NOT_FOUND`／404，不回傳其他作者的內容或錯誤細節。無授權時不執行 Task／Draft／Response 查詢。沒有新增 FK、Schema、列鎖、Adapter 介面或資格算法。

新增 24 項情境：Draft／Response 跨作者各搭配異常 Task 狀態、版本不可用、版本不存在、NULL／未知 HashVersion、複合異常（12 項）；無授權搭配同六種異常（6 項）；自己的異常資料仍回原 409，以及舊公開 Submit 保留原順序／409（各 3 項）。Fake 查詢計數確認跨作者不查版本／Key，無授權不讀 Task 或內容，並且無寫入。舊路徑的異常組合遮蔽順序**未於本輪改變**，不將它宣稱為新版保證。

## 已驗證情境（純邏輯／Fake 邊界）

| 範圍 | 驗證 |
|---|---|
| 首次提交 | 無草稿必須 Revision 0；有草稿精確比對；stale 拒絕；ulong.MaxValue 可提交且不做草稿遞增 |
| 保存內容 | Validator 正規化後以既有 canonical encoder 建 Hash；Response 同時保存實際 Relationship、版本、Hash、作者；Task 狀態／時間一致 |
| 重送 | 作者、Task、Key、版本與正規化內容相同回原 ResponseId／時間；在重送完成前不比較 Draft Revision；不新增 Response、不重新寫答案 |
| 內容衝突 | 同 Key 的 Relationship、姓名、日期、Observation 或答案不同均 409，不覆寫 |
| 版本 | NULL、不支援、大小寫不同或尾端空白均 `IDEMPOTENCY_VERSION_UNSUPPORTED`／409；採 ordinal，不依賴資料庫 Collation，不嘗試舊 Hash |
| 關係 | 新提交與重送均須既有合法代碼；空白、別名、null 拒絕；舊草稿 NULL 不推定；v1 收據缺關係不能重送 |
| 安全邊界 | 跨作者 Task／Draft／Response 與無權限回 404；原始 principal 不由 Context ID 重建；缺資格欄位或 Adapter 返回不同 Task／資格即拒絕 |
| 資格變動 | 合成 Consent、文件、Guardian、Stamp 改變皆拒絕，不替換／重試；匹配重送也必須通過最後 Revalidate |
| 儲存失敗 | Fake Commit 失敗或最終資格重驗拒絕，不發布 staged Response／Task；保留原有 nullable 草稿與 Revision |
| 其他 | 不接受空 Key、不自動換 Key、不覆寫已提交 Task、不跳過 CANCELLED gate；壞 Hash bytes 不回成功收據 |
| 舊相容 | 舊公開 Submit 的 Hash、NULL 元資料、重送行為保留；授權歷史 GetReceipt 不等於新版重送核可 |

內部 INVALID_INPUT／400、DRAFT_CONFLICT／409、IDEMPOTENCY_CONFLICT／409 等由現有例外型別回報，尚非正式 HTTP Controller 映射驗收。
新版錯誤版本會安全拒絕；**舊公開 Submit 仍保留舊版本行為，不可誤當新版入口或舊資料遷移方案**。

## 仍 BLOCKED：不得作為正式上線證據

1. 11 的真正資格世代、Guardian／Consent Adapter、共同寫入端鎖序與撤銷競態。Fake 授權不是正式政策驗收。
2. 真正 EF／MySQL SaveChanges／Commit／Rollback、唯一鍵、兩個並行 Submit、死鎖與網路中斷後 Commit 結果。Fake 只在 Commit 時複製記憶體狀態，不具資料庫引擎交易語意。
3. 共同 Schema／增量 Migration／Snapshot 核准、HTTP／DI 接線與跨模組驗收。既有五份 Migration 全保留；未升級庫不可使用新版模型；禁止 MySQL80／3306。

07 Workflow／完成輪次未接入，本輪只更新問卷 Task，不觸發 CaseStatus 或 History。院方 Consent 本文、Scope 與啟用政策未自行決定。

## 回歸與重用

重用 [ParentCanonicalHash](../ParentCanonicalHash/Program.cs)、[ParentIntegrationContract](../ParentIntegrationContract/Program.cs)、[ParentBridgeMapper](../ParentBridgeMapper/Program.cs) 的既有元件／契約與測試，不另寫 Revision parsing、Mapper 或 encoder。教師核心與共用 SubmissionHash 原始碼未修改。
[ParentQuestionnaire](../ParentQuestionnaire/Program.cs) 與 [Phase22](../QuestionnairePhase22Harness.cs) 的舊 schema gate 本輪不修改、不略過，實際失敗結果如下。既有 ADO Harness 亦不代表本輪真正 MySQL 驗收。

| 回歸 | 本輪結果 |
|---|---|
| ParentCanonicalHash | 58 PASS、0 FAIL |
| ParentIntegrationContract | 34 PASS、0 FAIL |
| ParentBridgeMapper | 67 PASS、0 FAIL |
| EfParentStore | 86 PASS；EF／Pomelo＋替身 ADO，非 MySQL |
| QuestionnairePhase22Harness | FAIL，原檔第 367 行 `no pending schema/model changes`；gate 後續檢查 BLOCKED |
| ParentQuestionnaireHarness | FAIL，原檔第 198 行 `only six additive author schema operations proposed`；gate 後續檢查 BLOCKED |

後兩項各回 exit 1；模型新增欄位尚未納入 Migration／Snapshot，且舊六項 author schema 預期不含 Relationship／HashVersion，不可當作已驗收或因方便而移除 gate。部分既有專案建置帶 NU1900 套件弱點來源警告；建置皆 0 錯誤，未下載套件。主 Harness 建置 0 警告。

## 安全執行完整舊回歸（捕捉例外，避免 JIT debugger）

不直接啟動會以未處理例外結束的舊測試。先建置，再由本 Harness 的固定白名單入口呼叫完整測試 EntryPoint（無測試篩選、不傳 proposal／skip 參數）：

```powershell
dotnet build tests/ParentSubmitReadiness/ParentSubmitReadinessHarness.csproj --no-restore
dotnet build tests/QuestionnairePhase22Harness.csproj --no-restore
dotnet tests/ParentSubmitReadiness/bin/Debug/net8.0/ParentSubmitReadinessHarness.dll --regression teacher
dotnet build tests/ParentQuestionnaire/ParentQuestionnaireHarness.csproj --no-restore
dotnet tests/ParentSubmitReadiness/bin/Debug/net8.0/ParentSubmitReadinessHarness.dll --regression parent
```

捕捉入口只允許上述兩個已建置測試 assembly，不接受任意路徑，不載入 API 的 EntryPoint。例外保留型別、訊息及堆疊，輸出 REGRESSION_FAIL 並以 exit 1 結束，不重試、不繼續同套件後續測試、不改系統偵錯設定。建置失敗時停止該套件。此 runner 只在獨立 Harness 內，不增加正式服務可達路徑。
