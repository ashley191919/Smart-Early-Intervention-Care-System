# Gateway Bridge DTO／錯誤碼契約測試準備

狀態：格式層離線準備；不是正式 Bridge，也不是共同核定的錯誤映射。16 固定 `370e319499da794b8d82fda9e9d888aa721ceec1`，11 固定 `e2affb548bb427cd02d001d5bfd5761d7ecb4648`。未修改正式碼或 11 Contract Suite。

## DTO 欄位對照（原始碼已確認）

| 11 HTTP DTO | 16 核心 DTO | 可安全確認／未決項目 |
|---|---|---|
| ParentDraftRequest.QuestionnaireVersionId: Guid | SaveQuestionnaireDraftRequest 同名 Guid | UUID 可原值傳遞；Guid 型別不保證非 Empty、存在或可讀，留給既有業務驗證 |
| ExpectedRevision: string | ExpectedRevision: ulong?，有 Required | 數值可無損轉換；11 接受前導零，新轉換元件拒絕，正式接線待確認；缺值不得默認 0 |
| RespondentName／FilledOn／Observation: string? | 同名 string? | 可保存 null；不能在格式層擅自 trim 或補值；FilledOn 核心 Validator 使用 yyyy-MM-dd，草稿容許未填 |
| Answers: QuestionnaireAnswer[] | QuestionnaireAnswer?[]? | 元素均為 QuestionId／OptionValue string?；11 宣告非 null 不代表 HTTP binding／元素已驗證；核心提交必填／內容驗證仍由既有 Validator 處理 |
| RespondentRelationship: string | 無 | 不可丟棄後宣稱完整 Bridge；保存位置、必填、Hash 待決策 |
| ParentSubmitRequest.ExpectedRevision: string | QuestionnaireSubmission 無對應欄位 | Submit 是否依草稿 Revision、重送判定順序待共同決策 |
| ParentSubmitRequest.RespondentName／FilledOn: string | QuestionnaireSubmission: string? | 不能由 C# 非 nullable 宣告推定已完成驗證；核心 complete=true 才驗完整提交 |
| ParentSubmitRequest 其餘 payload | QuestionnaireSubmission 的版本、Answers、Observation | 同名資料可描述，尚不可據此制定 canonical payload／Hash |
| ParentDraftResponse.Revision: string | QuestionnaireDraftResponse.Revision: ulong | 回傳字串可無損格式化；不可把核心 JSON number 直接當 HTTP 回應 |
| ParentDraftResponse：TaskId、版本、metadata、Answers、UpdatedAtUtc、TaskStatus | 核心同名欄位 | UpdatedAtUtc 為 DateTime?；UTC 由核心輸出處理；11 額外 Relationship 尚缺 |
| ParentSubmissionReceipt | ParentQuestionnaireReceipt | 六個同名同型欄位：ResponseId／TaskId／QuestionnaireVersionId Guid、SubmittedAtUtc DateTime、TaskStatus string、Replayed bool；須未來實際橋接驗證 |
| ParentRequestContext | ClaimsPrincipal ＋交易內 ParentTaskAccess | 不是 DTO 格式轉換；不能把 Actor／Consent ID 當已驗權限，不在本測試偽造資格 |

16 的 QuestionnaireReceipt 是教師用 GrantStatus DTO，不是 ParentQuestionnaireReceipt，不能因名稱相似接錯。ParentTaskStatus 為 16 額外方法；11 Gateway 尚無同名狀態方法。

來源：11 `ParentModule/ParentQuestionnaireContracts.cs`、`ParentControllers.cs`；16 `DTOs/Questionnaires/QuestionnaireWorkflowContracts.cs`、`Services/Questionnaires/ParentQuestionnaireContracts.cs`、`ParentQuestionnaireGateway.cs`、`QuestionnaireAnswerValidator.cs`。

## 目前錯誤碼盤點（不是新映射表）

| 來源 | code / HTTP | 備註 |
|---|---|---|
| 16 Gateway | AUTHENTICATION_REQUIRED / 401；TASK_NOT_FOUND / 404；TASK_UNAVAILABLE / 409 | ownership 不符亦遮罩為 TASK_NOT_FOUND；不可擅改為 409 |
| 16 Gateway／Validator | INVALID_INPUT、INVALID_ANSWERS / 400；VERSION_MISMATCH、VERSION_UNAVAILABLE / 409 | 空 key 在核心是 INVALID_INPUT / 400 |
| 16 Gateway／Store | DRAFT_CONFLICT、TASK_SUBMITTED、IDEMPOTENCY_CONFLICT、WRITE_CONFLICT / 409 | Revision、提交後編輯、重送／DB 衝突各不同；不自動重試／改 key |
| 16 Gateway／預設 Adapter | RESPONSE_NOT_FOUND / 404；PARENT_ACCESS_NOT_INTEGRATED、DEFINITION_UNAVAILABLE、STORAGE_INCONSISTENT / 503 | 依原碼分類，未完成 HTTP Bridge 錯誤翻譯 |
| 11 Controller | INVALID_INPUT、INVALID_IDEMPOTENCY_KEY / 400 | Header 用 Guid.TryParse 並拒絕 Guid.Empty，未限定僅 D 格式；不自行改成嚴格 UUID D |
| 11 預設 Gateway／Access | PARENT_GATEWAY_UNAVAILABLE、PARENT_TRANSACTION_REQUIRED / 503；TASK_NOT_FOUND / 404；TASK_NOT_EDITABLE / 409 | 不是 16 的同名錯誤全集 |
| 11 Actor／Consent | UNAUTHENTICATED / 401；CASE_NOT_FOUND / 404；CONSENT_NOT_CONFIGURED / 503；CONSENT_REQUIRED / 403 | 不在格式測試決定有效 Consent |
| 11 Consent | INVALID_INPUT / 400；CONSENT_VERSION_CHANGED、CONSENT_CONFLICT / 409；CONSENT_SCOPE_REQUIRED / 400 或 403 | 同 code 不同條件有不同 status，不能按 code 單獨硬編碼映射 |
| 11 Fake Contract Suite | DRAFT_OWNERSHIP_CONFLICT / 409；TASK_NOT_EDITABLE / 409；RESPONSE_NOT_FOUND / 404 | Fake 實作觀察，不是共同政策；16 ownership 遮罩不同 |
| 11 ParentModuleErrors | CaseAccessException 保留 code／status；DB／Timeout → SERVICE_UNAVAILABLE / 503 | envelope 為 code、message、traceId，設定 no-store；不處理所有 16 Workflow exception |

16 現有 QuestionnaireErrors 處理 QuestionnaireOperationException 與 DB 故障，不能視為已接妥 ParentQuestionnaireGateway 的 QuestionnaireWorkflowException。正式 Bridge 應於共同確認後處理例外型別／安全訊息／traceId，不能讓內部例外／SQL 外洩。401 challenge、binding、自動驗證、CSRF、限流的 HTTP envelope 本次未啟動 Host 驗收。

## 11 Contract Suite 可重用範圍

固定來源：`tests/ParentV53/ParentGatewayContractSuite.cs`、`FakeParentGateway.cs`、`Program.cs`。本輪未複製、修改或執行它們。

- 可保留案例設計：初始字串 Revision=0、儲存增量、草稿重讀、過期 Revision 衝突、同 key 同 payload 重送同 ResponseId、不同 payload 衝突、收據重讀。
- 需先調整契約再套用：Relationship 保存、跨作者 DRAFT_OWNERSHIP_CONFLICT / 409、提交後 TASK_NOT_EDITABLE、新提交的 ExpectedRevision、Fake 的 Hash 輸入。
- Suite 透過注入 gateway／context／fixture 可在未來重用，但例外捕捉固定 CaseAccessException，須由 Bridge 正式轉譯後驗證。Suite 的 Error helper 只核對 code，未核對 HTTP status／envelope，不能當 HTTP 驗收。
- Fake 使用 EF fixture 查詢／ExecuteUpdate，且以記憶體字典保存答案與收據，不符合本輪「無資料庫」測試邊界；不在此另造第二個 Fake 引擎。
- 已有 RevisionConversion 53 項、ParentQuestionnaire／EfParentStore 等既有測試不重寫；本次只補跨邊界 JSON shape／型別風險，沒有重測交易或授權。

## 新增離線測試的範圍與執行

```powershell
dotnet run --project tests/GatewayBridgeContract/GatewayBridgeContractHarness.csproj
```

僅 .NET 8 BCL；直接編譯兩份既有原碼（核心 DTO、RevisionStringConverter），無 ProjectReference／PackageReference、EF、Host、網路、資料庫、Guardian／Consent。NuGet 來源限定測試目錄並停 audit；需要已安裝 targeting pack。建置只產生忽略的 bin／obj。

測試驗證實際核心 DTO JSON、字串／UInt64 型別區別、合成 scalar fixture 的無損值、日期詞法、Nullable／Required 宣告與明確 DataAnnotations 驗證、UTC 字串及 DTO 欄位集合。使用 JsonSerializerDefaults.Web 是顯式測試設定，不是啟動正式 MVC；C# Nullable／Required／JSON binding／業務有效性分開描述。

11-shaped fixture 是依固定原碼人工建立的純合成 JSON，只含三個 scalar，不是 11 型別編譯驗證、完整 HTTP request 或 Bridge；它不提供權限或醫療政策。測試不斷言尚未決策的錯誤映射，不以複製錯誤表的自我比較充當測試。

## 正式 Bridge 前必須確認

1. 前導零格式是否收緊，以及格式錯誤的 code／status。
2. 404／409 ownership 遮罩、TASK_SUBMITTED／TASK_NOT_EDITABLE、空 key 的 HTTP code；Guid 可接受詞法與多值 Header 行為。
3. Submit ExpectedRevision 與成功重送判定順序、Relationship 保存／Hash、canonical payload／相容性。
4. 錯誤 envelope／例外轉譯與 transaction 內可信 Actor／Guardian／Consent 重驗協定。

結論：可以繼續格式層準備及共同契約審查；尚不適合啟用正式 Bridge。所有 PASS 僅代表此離線格式測試，不代表真 Controller、HTTP 狀態、提交交易、院方核准或 MySQL 驗收。
