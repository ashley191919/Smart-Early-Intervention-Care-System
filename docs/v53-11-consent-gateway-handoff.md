# K2／K3：11 Consent／可信作者與家長 Gateway

日期：2026-10-22。契約版本：`parent-v53-1`。本輪為固定可測試契約，需 16 以真 Adapter 跑相同測試後確認；不是已整合的寫入引擎。

## 所有權及檔案

`EarlyInterventionCare.Api/ParentModule/`：

- `ConsentModels.cs`：11 的 Entity／Configuration。
- `ConsentService.cs`：可信 actor、文件查核、同意保存及有效同意查詢。
- `ParentQuestionnaireContracts.cs`：Gateway、RequestContext、交易內檢查契約。
- `ParentControllers.cs`：Consent 及家長 Draft／Submit／Receipt 入口。
- `ParentModuleRegistration.cs`：DI、保護舊讀取路徑及安全錯誤。

11 不新增 Response 儲存、Task 更新或問卷交易 Service。預設注入 `UnavailableParentQuestionnaireGateway`，回 503 `PARENT_GATEWAY_UNAVAILABLE`，不降級保存。

## 預設拒絕與文件配置

`ParentModuleOptions` 預設 ConsentEnabled=false、DocumentVersionId=Empty、ScopeCode=UNCONFIRMED。

只有正式整合者確認下列全部條件後，才可配置：

1. 院方本文、版本、範圍與同意前身分驗證作業已確認。
2. 16 增量 Migration、回填與目標 DB 已驗收。
3. 明確選定 ACCOUNT 或 CASE scope（目前支援兩種模型，不代為決定）。
4. 所選 DocumentVersionId 的 DB 文件為 APPROVED，具核准參照、核准／生效時間、未退役、本文雜湊正確。

未提供院方文件、沒有正式 Seed、沒有修改 appsettings／Secrets 啟用。本輪測試 APPROVED 文件只在一次性 SQLite 測試程序內，本文標示非院方核准。

## 同意先行 API

- GET `/api/parent/consent/context`：Bearer；只回文件與同意目標／狀態，不回姓名、身分證、生日、地址、Profile、題目或答案。
- POST `/api/parent/consent`：Bearer；actor 從 JWT＋目前 DB 帳號取得。

```json
{
  "documentVersionId": "22222222-2222-4222-8222-222222222222",
  "contentHash": "伺服器提供的64字元SHA256",
  "caseId": null,
  "dataUseDecision": "AGREED",
  "teacherParticipation": "UNDECIDED",
  "requestId": "33333333-3333-4333-8333-333333333333"
}
```

ACCOUNT 的 caseId=null；CASE 必須是目前有效綁定的 CaseId。CASE bootstrap 僅回 opaque CaseId；正式呈現及具體 scope 仍待院方確認。

dataUseDecision=AGREED／DECLINED；teacherParticipation=UNDECIDED／AGREED／DECLINED。有效家長存取只看資料使用 AGREED，不把教師不同意或未決定當成家長問卷拒絕條件。

回傳 consentId、documentVersionId、caseId、兩個決定、recordedAtUtc、replayed。相同決定可重試；衝突或已撤銷拒絕，不提供未核定的改選／撤回 API。

前端先完成上述伺服器同意流程，才取得 `/api/parent/cases`、Profile、Task。以前的 sessionStorage 勾選只保留於舊展示模式，account 流程完全不讀它作為同意證明。

### 禁止條件

- 無登入／停用／Email 不可信：401 或既有 Email 403；Task 對外維持 404 隱藏語意。
- 無適用核定文件：503 CONSENT_NOT_CONFIGURED。
- 沒有效資料使用同意：403 CONSENT_REQUIRED。
- 範圍不明：CONSENT_SCOPE_REQUIRED。
- 指定 Case／Task 不在有效 Guardian scope：404。
- 文件版本／內容識別已變：409 CONSENT_VERSION_CHANGED。
- 相同請求／範圍出現不同決定：409 CONSENT_CONFLICT。

家長一般列表／Profile 在 CASE scope 下須具目前綁定範圍所需的同意；未綁定的新個案蒐集流程本期不開放。

## 固定 Gateway

`IParentQuestionnaireGateway`：GetDraftAsync／SaveDraftAsync／SubmitAsync／GetReceiptAsync。

參數使用後端建立的 `ParentRequestContext`，包含 ClaimsPrincipal、ActorUserId、OrganizationId、CaseId、ConsentId、DocumentVersionId；不得從 HTTP body 綁定該 Context。

HTTP：

| 方法 | 路徑 |
| --- | --- |
| GET／PUT | `/api/parent/tasks/{taskId}/draft` |
| POST | `/api/parent/tasks/{taskId}/submit` |
| GET | `/api/parent/tasks/{taskId}/receipt` |

PUT 範例：

```json
{
  "questionnaireVersionId": "22222222-2222-4222-8222-222222222222",
  "expectedRevision": "0",
  "respondentName": "虛構填表人",
  "filledOn": "2026-10-22",
  "respondentRelationship": "FATHER",
  "answers": [{"questionId":"Q1","optionValue":"0"}],
  "observation": ""
}
```

Submit 同上述欄位，另帶 `Idempotency-Key: <UUID>`。Header 無效回 400 INVALID_IDEMPOTENCY_KEY。

Draft 回 taskId、questionnaireVersionId、**revision 字串**、填表欄位、answers、updatedAtUtc、taskStatus。
Revision 使用字串避免 JS Number 丟失 ulong 精度；16 Adapter 需明確轉換，不另改既有教師 DTO。

回執：responseId、taskId、questionnaireVersionId、submittedAtUtc、taskStatus=SUBMITTED、replayed；不帶答案、作者或 GrantStatus。

本次填表關係是答案中繼資料，不是 Guardian 授權變更。作者一律用 Context 中的可信身分；原有 Draft／Response 尚未加入家長作者與本次關係，由 16 統整，不由 11 修改其 Entity。

## 16 必須提供的真實語意

1. 保存可信家長作者；別的照顧者不可接續／覆寫草稿或查本人回執。
2. Revision 原子比較與遞增；409 DRAFT_CONFLICT 不覆寫。
3. 同一作者＋Idempotency-Key 綁定同一 Task／版本／有效內容；同鍵同內容回原收據，不同內容 409 IDEMPOTENCY_CONFLICT。
4. 每 Task 最多一份正式 Response；不同 key 不能重新提交已完成 Task。
5. Response、Task=SUBMITTED、submittedAt 在同一交易成功或回復。
6. 重試已成功的原提交可回原回執，但仍須目前 actor／Guardian／Consent 有效。
7. 固定 Task 指派版本，不信任前端 VersionId；重用共用 Validator。
8. GetReceipt 驗作者，不因目前綁定就放行他人回執。

## 一致性檢查契約

`IParentQuestionnaireAccess.RequireAsync(...)` 是 Controller 預檢。

真引擎在自己的單一交易／scoped ApplicationDbContext 內，必須再呼叫：

```csharp
await parentAccess.RequireInTransactionAsync(
    request.Principal, taskId, writing: true, transactionContext: db, ct);
```

該方法拒絕不同 DbContext 或沒有 CurrentTransaction，重新檢查目前帳號／院所／PARENT／Email／Guardian／Task／Consent。

**有交易不等於已解決競態。** Case 協調鎖、Guardian／Consent 撤銷端與提交端的共同順序、隔離及 MySQL 並行測試仍須 11／16 在固定整合階段確認。本輪沒有自行更改 16 的教師 Grant→Task 鎖定流程，也未新增正式 Consent 撤回端點。

## 同一 Contract Test 交接

- `tests/ParentV53/ParentGatewayContractSuite.cs` 接受任意真／Fake Gateway 及等價隔離資料 fixture。
- 本輪 `FakeParentGateway` **只存在測試執行檔**；草稿／答案／回執保存在記憶體，僅更新測試 SQLite Task 以驗證 UI 刷新；不建立正式 Response。
- 16 的真 Adapter 必須執行同一 Suite，再補 MySQL transaction／rollback／並發／唯一鍵測試。
- DI 使用 TryAdd 預設拒絕實作；16 可在整合組態中 Replace／最後註冊真 Adapter，不能把 Fake 引入網站。

## Client 行為

- 寄信／登入沿用既有元件，不改 SMS，也未開放首次註冊。
- POST Consent 斷線保留相同 requestId，僅手動重試，不自動改選項。
- Draft 先 GET Revision；衝突保留輸入，明確重新載入後才可再存。
- Submit 結果不確定時保留原 key／內容，允許原提交重試或查詢回執；不儲存到 localStorage 冒充成功。
- 成功只使用伺服器回執及重新查回的 Task 狀態。
- 本輪僅本人回執入口，不提供未核定的答案修改、重填或完整歷史轉移。
