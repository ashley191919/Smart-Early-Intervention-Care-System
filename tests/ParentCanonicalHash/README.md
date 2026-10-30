# 家長 canonical bytes／Hash v1（獨立、未接線）

基準：`b1444ef2a1477fa7b94399a641d0fbb6b01536c7`。實作來源：`EarlyInterventionCare.Api/Services/Questionnaires/ParentCanonicalSubmissionEncoder.cs`，實作既有 IParentCanonicalSubmissionEncoder；沒有 DI／Gateway／Controller 呼叫，沒有 Schema 變更。本格式是本批供審查的確定規格，不表示已部署或完成舊資料相容。

## 輸入與正規化

優先呼叫 `Encode(taskId, versionId, ValidatedQuestionnaireContent, relationship)` 或同參數 ComputeHash；內容必須先由既有 QuestionnaireAnswerValidator 以 complete=true 驗證。另一個 Encode(ParentCanonicalSubmission) 是既有介面的實作，要求相同的已驗證內容；它不能查問卷定義，也不證明答案合法／完整或具權限。

沿用既有 Validator：姓名 Trim、空姓名變 null（完整提交拒絕）；Observation Trim、空／純空白變 null；日期解析為 DateOnly；答案依 QuestionId 的 StringComparer.Ordinal 排序，重複題號拒絕。Encoder 不再次 trim 或容忍未正規化欄位；手動輸入空 Observation 字串會拒絕，raw null／空字串經 Validator 後都成 null，因此 Hash 相同。

Encoder 防禦性檢查非 Empty Task／Version、完整姓名／日期、基本答案形狀、唯一題號及合法關係，但不是 Validator 的替代品。關係只能是 FATHER、MOTHER、GRANDPARENT、RELATIVE、GUARDIAN、CAREGIVER、OTHER，不修正大小寫／空白、不替舊資料推定值；null 關係不能計算新的正式提交 Hash。舊草稿／Response 仍維持原有 null，未被讀寫。

Unicode 以嚴格 UTF-8 編碼（無 BOM、無 replacement fallback），不增加 NFC／NFD、大小寫或其他正規化。組合與分解字串維持不同；合法 emoji／中文保留。未配對 UTF-16 surrogate 拋 EncoderFallbackException，不替換成同一字元而造成編碼歧義。這是獨立 encoder 的輸入拒絕，不修改原 Validator 或 HTTP 錯誤處理。

## Canonical bytes 精確規格

版本標識：`earlycare.parent-submit.canonical/v1`。

`U32(n)`：4 bytes unsigned big-endian。

`Text(s)`：`U32(UTF8(s).Length)` 接嚴格 UTF-8 bytes。長度為 byte 數，不是 UTF-16 char 數；不使用系統 endian、分隔符串接或 Culture 預設格式。

固定順序：

1. Text(版本標識)。
2. Text(TaskId 的小寫 D UUID，36 ASCII chars)。
3. Text(QuestionnaireVersionId 的小寫 D UUID)。
4. Text(正規化姓名)。
5. Text(FilledOn，以 InvariantCulture 的 yyyy-MM-dd)。
6. U32(答案數量)。
7. 每個答案按 QuestionId ordinal 排序：Text(QuestionId)、Text(OptionValue)。在編碼內複製陣列後排序，不改 caller 陣列；不 Trim 問題／選項代碼。
8. Observation：null 寫單一 byte `00`；非 null 寫 `01` 後接 Text(正規化內容)。非 null 空字串不屬有效正規化輸入。
9. Text(RespondentRelationship)。

結果：`SHA256(canonical bytes)`，32 bytes；ComputeHash 不輸入 ExpectedRevision、Idempotency-Key、ClaimsPrincipal 或作者 ID。作者＋Key＋Task 的查詢、ownership 與原收據比對由未來 Submit coordinator 處理，Hash 相同不代表獲授權或成功重送。格式版本已包含在 Hash 輸入中；將來編碼規則改變須使用新版本。

本規格使用固定 schema／順序、答案計數、文字長度與 null tag，避免 `ab+c`／`a+bc` 等欄位邊界歧義；這不代表 SHA-256 在數學上不可能碰撞。

## 固定測試向量

Task=`11111111-1111-4111-8111-111111111111`、Version=`22222222-2222-4222-8222-222222222222`、姓名 Alice、日期 2026-10-30、單題 Q1=0、Observation=null、關係 FATHER。

預期 SHA-256 hex：`91E05E5302578620234CD4994D3728FCAAA861D65E088233E6CA0C11F1D4C452`。

Program.cs 固定完整 golden bytes；向量以獨立 Node 內建 Buffer.writeUInt32BE／crypto 離線計算，不呼叫 C# encoder 來建立預期值。測試涵蓋穩定性、所有內容欄位、七個關係、答案順序、欄位界定、null／空值、Unicode／surrogate、Culture 與共享舊 Hash 未變。

## 執行與安全邊界

```powershell
dotnet restore tests/ParentCanonicalHash/ParentCanonicalHashHarness.csproj --source tests/ParentCanonicalHash -p:NuGetAudit=false
dotnet run --project tests/ParentCanonicalHash/ParentCanonicalHashHarness.csproj --no-restore
```

測試專案參考 API assembly 以使用真 Validator／型別；只編譯、不執行 API Program，不建立 Host／DbContext／DbConnection、Socket 或交易。Restore 只用本機來源與快取；缺少 SDK／targeting pack／套件時停止。建置會更新忽略的 bin／obj。Encoder 只有 BCL 記憶體 stream／編碼／SHA256 依賴。

測試 PASS 僅代表純 canonical 元件；沒有驗收 Guardian／Consent、撤銷競態、Submit Revision、Key 重送、跨作者 404 或 MySQL。教師原有 ValidatedQuestionnaireContent.SubmissionHash() 未修改，新元件不改變其 JSON Hash 路徑。

## 舊 Hash 相容及正式接線仍未完成

- 舊家長 Hash 沒有 Relationship／版本 domain；本批不重算、不回填、不將 null 視為特定關係，不以嘗試兩種 Hash 的 fallback 放行重送。
- v1 前綴在 Hash 之前的 bytes 中，不等於現有 Response 有可讀的版本欄位；如何辨識舊／新列、是否保存 HashVersion，仍須共同決策。
- 舊作者＋Key 與新 canonical 的相容策略、資料遷移／升級／回復尚未設計或驗收。不能聲稱已完成資料遷移。
- Draft／Response 的 Relationship 儲存、正式家長 Command 接入、Guardian／Consent Adapter／資格快照與共同鎖序、HTTP 錯誤轉譯均為後續前置條件。維持預設拒絕，不啟用 Submit／DI。
