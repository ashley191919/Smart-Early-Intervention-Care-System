# V5.3／11＋16 Parent Adapter 固定介面基礎

狀態：`parent-bridge-contract-1`，供 11 實作的未接線契約。本文及新增型別尚未 Commit；交付後必須由使用者提供實際完整 SHA，不能以分支 tip 或本文假定的 SHA 開發。

基準：16 `3d98360216761f928ae1e2c84412783e80d82ec4`；11 `e2affb548bb427cd02d001d5bfd5761d7ecb4648`。本批只有新增契約／純 Revision wrapper／無條件拒絕實作／離線測試；沒有 Bridge、Controller、DI 或提交流程接線，沒有 Entity／Migration／Snapshot 變更。

## 1. 已由使用者轉達 11 確認的語意

- HTTP Revision 僅 ASCII `0` 或無前導零正整數，範圍 UInt64；缺草稿的 ExpectedRevision 是 0，不是缺少輸入就默認 0。
- 草稿未填／舊資料的 RespondentRelationship 可以 null；正式提交必填；舊資料不推定。
- 首次 Submit 驗 Draft Revision；成功重送先依同作者、Task、Idempotency-Key、相同 canonical 提交內容辨識原收據，不受目前 Draft Revision 阻擋。
- canonical 提交 Hash 包含 Relationship，不包含 ExpectedRevision；作者及 Key 由可信身分／獨立參數處理。
- 跨作者 Task／Draft 安全遮罩為 404。
- Consent／文件／綁定在預檢後變更必須安全拒絕，不替換成另一個有效同意、不自動重送。

上述語意已確認，不表示基準正式程式已完成。基準 11 Controller 仍接受前導零／非 nullable Relationship；基準 16 Submit 仍無 ExpectedRevision／Relationship。本批不修改它們或教師共用 Validator／SubmissionHash。

## 2. 16 新增檔案與型別

命名空間：`EarlyInterventionCare.Api.Services.Questionnaires`。

來源：`EarlyInterventionCare.Api/Services/Questionnaires/ParentGatewayIntegrationContracts.cs`。

| 型別 | 固定欄位／責任 |
|---|---|
| ParentSaveDraftCommand | 既有 SaveQuestionnaireDraftRequest Payload、string? RespondentRelationship；沿用 Payload.ExpectedRevision 的 UInt64?／Required 宣告，不在此賦予寫入能力 |
| ParentSubmitCommand | 既有 QuestionnaireSubmission Payload、ulong ExpectedRevision、string RespondentRelationship；C# non-null 不是執行期完整性驗證 |
| ParentDraftResult | 既有 QuestionnaireDraftResponse Draft、string? RespondentRelationship；舊值保持 null |
| ParentAccessExpectation | ActorUserId、OrganizationId、CaseId、GuardianId、ConsentId、DocumentVersionId 均 Guid，EligibilityStamp 為 string；全部為伺服器預檢比對資料，不是權限 |
| ParentGatewayCallContext | 原本可信 ClaimsPrincipal Principal、ParentAccessExpectation ExpectedAccess；不由 ID 建立或補造 Principal |
| ParentTransactionAccess | Guid TaskId、ParentAccessExpectation Snapshot；僅由未來交易 Adapter 新鮮驗證後回傳，仍須提交前重驗 |
| ParentCanonicalSubmission | TaskId、QuestionnaireVersionId、RespondentName、DateOnly FilledOn、Answers、Observation、RespondentRelationship；沒有 Revision、Key 或前端作者 |
| IParentCanonicalSubmissionEncoder | `byte[] Encode(ParentCanonicalSubmission content)`；只固定純編碼邊界，沒有實作或啟用 |
| IParentRevisionCodec | `bool TryParse(string? text, out ulong revision)`、`string Format(ulong revision)`；StrictParentRevisionCodec 委派既有嚴格轉換元件，未註冊 DI |

Record／非 nullable 宣告只描述資料形狀，不進行資格查詢或保證資料有效；可被程式手動建立，不能當授權 token。未來正式 caller 必須防禦 null／Empty／缺標記及可變陣列，經核心 Validator／Adapter 驗證後才使用。

## 3. 11 要實作的精確介面

新介面是 `IParentTransactionAccessAdapter`，不是目前正式核心使用的 `IParentTaskAccessAdapter`；兩者刻意不連接，舊核心及其拒絕預設保持原狀。

```csharp
Task<ParentTransactionAccess> LockAndAuthorizeAsync(
    IParentQuestionnaireTransaction transaction,
    ClaimsPrincipal principal,
    Guid taskId,
    ParentAccessExpectation expectedAccess,
    bool writing,
    CancellationToken ct = default);

Task RevalidateAsync(
    IParentQuestionnaireTransaction transaction,
    ClaimsPrincipal principal,
    ParentTransactionAccess access,
    CancellationToken ct = default);
```

`IParentQuestionnaireTransaction` 是已存在的真實介面，不另造交易引擎。11 必須使用 `transaction.DbContext` 的同一 scoped ApplicationDbContext 及其既有 CurrentTransaction；正式實作不接受 null DbContext、不同 context 或缺少交易，不自行 Begin／SaveChanges／Commit／Dispose、不建立新 scope、不呼叫另一個 HTTP Service 授權、不寫無關追蹤變更。

LockAndAuthorize 必須由傳入 principal 解析唯一已驗證 userId，再比對 authoritative User／院所／PARENT 角色／Case／Task／Guardian／Consent。所有 expected ID／snapshot 均與鎖內結果比對；返回值不得直接把輸入 expectation 原封當作已授權。

writing=false 也要完整授權；SUBMITTED 允許進入原收據／相同重送核對，不能用它放行新的寫入。Task／Draft 跨作者統一遮罩。Response 實體作者、Revision、Key、Task／Response 一致性由 16 核心負責，11 不寫第二套答案儲存。

Revalidate 必須在同一交易、提交前（包含收據／重送／讀取完成）重新核對原快照及資格，不能只重用 tracked entity 或 Repeatable Read 舊快照。不得吞取消、拒絕或儲存錯誤。鎖由 coordinator 的交易生命週期持有及釋放。

精確列鎖／隔離層級／writer 協調尚未核定，因此新增 `DenyParentTransactionAccessAdapter` 無條件回 PARENT_ACCESS_NOT_INTEGRATED／503；它不接觸 transaction，即使提供看似完整的 principal、context 或 evidence 也不放行，且沒有 DI 註冊。缺少具體協定時 11 可先編譯對應拒絕骨架，不能宣稱已完成正式並發安全。

## 4. ParentRequestContext 對應及尚缺資料

11 基準 `ParentModule/ParentQuestionnaireContracts.cs` 的 ParentRequestContext 已有 Principal、ActorUserId、OrganizationId、CaseId、ConsentId、DocumentVersionId；未來由 11 自己維護此檔及 DTO。

對應方式：Principal 保留原物件；其餘 ID 只傳入 expectation 作交易內比對。11 還須從伺服器預檢結果提供 GuardianId 與 EligibilityStamp（可擴充其 context 或提供配套伺服器型別）；不得從 HTTP body 補值。此次 16 沒有修改或假設 11 現有 context 已具備這兩欄。

EligibilityStamp 是新增的內部「資格快照標記」提案，沒有演算法實作，不是新醫療政策、授權 token 或 submission Hash。只比 Consent／文件／Guardian ID 不足以偵測同 ID 修改／撤銷再綁定，因此固定非空標記欄位供將來協調。其版本化、輸入、比較與所有 writer 的更新／鎖協定仍待 11＋16 確認；不能填常數、任意 Guid 或以空值略過。

待共同固定的資格變更涵蓋範圍至少包括：可信帳號／院所／角色與有效狀態、Guardian 關聯與綁定代次、Consent 紀錄及文件內容／版本／核准／適用範圍。如何偵測撤銷後恢復的 ABA 變更、是否需要持久 revision／歷程，由模型與 writer 審查決定；單靠欄位 Hash 不保證偵測所有曾發生的變更。未定前保持拒絕，不新增猜測的 Schema。

## 5. canonical／Revision／Idempotency 契約

| 項目 | 已確認 | 尚待固定的實作細節 |
|---|---|---|
| Revision | strict decimal string ↔ ulong，InvariantCulture，不經浮點數；無草稿=0 | HTTP binding／格式錯誤訊息由 11 接線，本批 codec 不回 HTTP |
| Relationship | 草稿 null；正式提交必填；保存至 Draft／Response；入 Hash | 候選仍為 11 既有七個代碼，代碼擴充／大小寫需書面固定；舊 response Hash 相容／欄位 Mapping 尚未做 |
| Idempotency | 同作者＋Key；成功重送核對 Task／相同 canonical 內容，回同 ResponseId；不重比 Draft Revision | Header 可接受 Guid 詞法、多值與錯誤碼由 11 核對；不得換 key 自動重送 |
| Hash | Relationship 入內容，ExpectedRevision 不入；SHA-256 bytes 存既有 binary(32) 候選 | UTF-8 canonical 欄位次序、字串／null 規則、答案 ordinal 排序、版本 domain／舊資料相容尚待共同固定，encoder 無實作 |

ParentCanonicalSubmission 應從既有答案 Validator 正規化後的內容建立，不直接對原始 HTTP JSON 做 Hash。不得改教師共用 canonical 規則。ParentSubmitCommand 的 Relationship 是字串宣告，實際非空／合法代碼驗證留給未來家長核心；本批不新增 Submit 方法。

## 6. 建議錯誤碼交接（不代表已核定 HTTP 映射）

| 情境／已確認語意 | 現有來源／建議 | 待 11 核對 |
|---|---|---|
| 跨作者 Task／Draft | 16 TASK_NOT_FOUND／404；404 語意已確認 | 對外 code 是否維持 TASK_NOT_FOUND；更新舊 Fake ownership 409 |
| 角色／資格預檢後變更，安全拒絕 | 建議 ACCESS_PRECHECK_CHANGED；不替換、不自動重送 | 403／404／409 選擇、訊息及遮罩須共同確認，尚無程式 |
| 初次 Revision 不符 | 16 DRAFT_CONFLICT／409 | 保留 code／envelope 候選；正式 HTTP 尚待確認 |
| 已成功 key、不同 canonical 內容 | 16 IDEMPOTENCY_CONFLICT／409 | 不將失敗當重送成功；HTTP envelope 尚待確認 |
| 已提交、使用新 key | 16 TASK_SUBMITTED／409；11 TASK_NOT_EDITABLE／409 | 兩種 code 的正式選用尚待核對 |
| Revision／關係無效 | 11／16 INVALID_INPUT／400 候選 | binding／nullable／代碼的實際回應 |
| 空 key | 11 INVALID_IDEMPOTENCY_KEY／400；16 INVALID_INPUT／400 | Bridge 翻譯與 Header parsing |
| Adapter 未整合 | 新／舊 deny 都是 PARENT_ACCESS_NOT_INTEGRATED／503 | 11 原 PARENT_GATEWAY_UNAVAILABLE／503 是否保留外觀 |
| 身分／交易／儲存失敗 | 16 AUTHENTICATION_REQUIRED／401、STORAGE_INCONSISTENT／503；11 UNAUTHENTICATED／401、PARENT_TRANSACTION_REQUIRED／503 | 對外型別／code／traceId／no-store；不得洩露 SQL／Token |

新 deny 的 503 只是既有核心錯誤慣例，本批沒有 HTTP Host，沒有新增 exception filter 或宣稱真實路由回傳該 status。院方正式 Consent 本文、Scope／啟用／撤回政策仍待核准，本介面不代定它們。

## 7. 所有權與後續交付

- 16：以上新契約與 deny／codec、未來 Bridge／parent hash／共用儲存／Submit／Schema；不修改 11 模組原檔。舊 IParentTaskAccessAdapter 與新介面的正式整合方式另案審查。
- 11：新增自己的 `ParentModule/ParentTransactionAccessAdapter.cs`（建議路徑），實作本文件介面；自己的 Context／HTTP DTO／Controller／前端／Guardian／Consent writer／Contract Suite。此新實作未核准前不得註冊或啟用。
- 共同：資格 stamp、完整鎖序、所有綁定／撤銷／換版 writer、canonical bytes／Hash 舊資料及錯誤映射。舊契約列出的候選鎖序不是新協定已核定的證據。
- 07：未來 Program 最終接線；本批沒有 DI／Program 修改。
- 先交付實際固定 Commit 及測試紀錄，再讓 11 依介面開發。此次沒有 Commit，不能把基準 SHA 當成包含新契約。

## 8. 離線測試與證據界線

新增專案：`tests/ParentIntegrationContract/ParentIntegrationContractHarness.csproj`。參考真 API assembly 以檢查實際 transaction 型別／簽章；編譯 API 不會執行其 Program，也不啟動 Host／載入 User Secrets。測試只建資料與 trap transaction，任何 transaction 成員被使用即失敗；不建立 DbContext 或 DbConnection。

為避免 restore 連線，使用本機來源與既有 NuGet cache；需要本機 SDK／targeting pack 及已快取的 API 套件，缺少時停止，不下載或接資料庫：

```powershell
dotnet restore tests/ParentIntegrationContract/ParentIntegrationContractHarness.csproj --source tests/ParentIntegrationContract -p:NuGetAudit=false
dotnet run --project tests/ParentIntegrationContract/ParentIntegrationContractHarness.csproj --no-restore
```

範圍：型別／簽章、原 principal 物件傳遞、Revision wrapper 邊界、舊 null 關係、canonical shape 排除 Revision、default deny 的讀寫／重驗、核心沒有新 Command overload。不是實際拒絕跨作者／撤銷競態／canonical Hash／Submit 順序的行為驗收；這些尚未接入。所有新介面保持未註冊，沒有新公開 API。

保留既有五筆 Migration／Designer，不用 MySQL80／3306，不連任何 DB，不修改 DbContext／Snapshot／Program／教師引擎。本批測試 PASS 不能視為 11 Adapter、安全並發或正式跨模組整合完成。
