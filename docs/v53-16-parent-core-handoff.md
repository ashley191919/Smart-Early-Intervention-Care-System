# V5.3／16：家長儲存核心交接

本分支從 `533b6596925c5d6028aefa18b387472e33d1c708` 建立；包含既有 RBAC 及五份 Migration。
沒有 Fetch、Push、合併其他分支、實體 DB 連線或公開服務。`5078565` 的家長讀題核心尚未包含在此基準，請由 07 的 Git 整合流程保留該成果。

## 目前可交付與未完成

- 新增 `ParentQuestionnaireGateway` 與 EF transaction store；重用原 `QuestionnaireAnswerValidator`、Draft／Response／Task Entity。
- 不使用 TeacherGrant 作家長身分；沒有 Development 固定 Case 放行。
- 沒有家長專用 Controller／UI，也沒有真 Guardian／Consent adapter，沒有核定家長版本 seed。
- 預設 adapter 為拒絕，Program 沒有註冊家長 Gateway。**目前不是可部署的正式家長流程。**
- ParentAuthorUserId 模型為待審增量，Migration／Snapshot 不變。預設模型一致性檢查應拒絕；不可對既有 DB 啟動需要新作者欄位的儲存路徑。

## 11 的固定 Gateway

Namespace：`EarlyInterventionCare.Api.Services.Questionnaires`。

```csharp
Task<QuestionnaireDraftResponse> GetDraftAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct);
Task<QuestionnaireDraftResponse> SaveDraftAsync(ClaimsPrincipal principal, Guid taskId, SaveQuestionnaireDraftRequest input, CancellationToken ct);
Task<ParentQuestionnaireReceipt> SubmitAsync(ClaimsPrincipal principal, Guid taskId, Guid key, QuestionnaireSubmission input, CancellationToken ct);
Task<ParentQuestionnaireReceipt> GetReceiptAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct);
Task<ParentTaskStatus> GetStatusAsync(ClaimsPrincipal principal, Guid taskId, CancellationToken ct);
```

沿用 `SaveQuestionnaireDraftRequest`／`QuestionnaireSubmission`，沒有 authorId 欄位。
expectedRevision 首次 0，成功保存 +1；提交必須提供完整 payload，不默默套用草稿。
key 由專用 Controller 解析 `Idempotency-Key` 非空 UUID；同作者＋同 key＋同 task/payload 重送回同 responseId，內容不同 409。
姓名僅為填答資料，不是作者權限。唯一已驗證 identity 的唯一 `userId` 必須等於 adapter 查回作者。
已提交草稿不可讀寫；receipt／status 不回逐題答案、hash、作者 ID、去重 key。
日期沿用臺北日期與既有 Validator；正式結果不可覆寫。

## 11 的交易驗證 Adapter

實作 `IParentTaskAccessAdapter`：

1. `LockAndAuthorizeAsync(transaction, principal, taskId, ct)`：在 transaction.DbContext 及其 CurrentTransaction 中，查回並鎖定 ACTIVE 院所／帳號、Case、有效 Guardian 綁定及適用 Consent。
2. 由 DB 證明 PARENT 角色、同院所／Case／Task、綁定未撤銷、Consent 適用；不要從 body/JWT role/快取直接放行。
3. 回 `ParentTaskAccess(UserId, OrganizationId, CaseId, TaskId, GuardianId, ConsentRecordId)`；所有 ID 非空。這是內部證據，不是 HTTP DTO。
4. Gateway 再鎖 Task，核對 Case、PARENT、固定版本、狀態、既存作者。
5. `RevalidateAsync` 在同交易 commit 前再次檢查；讀草稿、查收據、去重回應也會呼叫。不得另開 DbContext、Commit 或釋放鎖。

共同鎖順序候選已固定為：Organization → User → Case → Guardian／link → Consent → Task。多筆依 ID 排序。
11 的撤銷／改綁／Consent 寫入也必須採相同協定，否則單次或再次查詢都不足以保證並行撤銷。
Adapter 可先讀最小 Task scope 尋找 Case，最終必須在鎖下再核對。若 11 最終 Entity 需要不同順序，先修訂契約與測試再接入，不在兩端各用一套。

真 Consent 的用途、版本、撤回效果由 11／院方定義；核心不創造政策。Fake 只驗介面與交易時點，沒有模擬成真正同意。

## Registration（整合審查及 Migration 完成後才啟用）

```csharp
services.AddParentQuestionnaireCore(new ParentQuestionnaireOptions(approvedParentVersionId));
services.AddScoped<IParentTaskAccessAdapter, ReviewedParentAccessAdapter>(); // 11
```

approvedParentVersionId 必須是後端設定的核定版本，不能由 request 選定。沒有核定版本就不要註冊；測試中的單題 snapshot 不是醫院問卷。
Gateway 使用 scoped、乾淨且獨占的 ApplicationDbContext；不能混入另一業務未保存的修改。EF store Dispose 時清 tracker；未 commit 由 transaction disposal rollback。
缺真 adapter 時維持 `DenyParentTaskAccessAdapter`，回 503。專用 Controller 負責認證 scheme、CSRF／來源檢查、payload 限制與安全錯誤邊界。
重試需要新 request scope／新 transaction，不在已失敗 transaction 裡重跑，也不在回應前自動重試可能已 commit 的操作。

## 錯誤契約

| code | HTTP | 意義 |
|---|---:|---|
| AUTHENTICATION_REQUIRED | 401 | 未登入或 identity／userId 歧義 |
| TASK_NOT_FOUND | 404 | 不存在、錯作者／範圍／院所、綁定／同意不可用；不洩漏存在性 |
| PARENT_ACCESS_NOT_INTEGRATED | 503 | 真 Adapter 尚未提供 |
| INVALID_INPUT / INVALID_ANSWERS | 400 | 沿用 Validator |
| VERSION_MISMATCH / VERSION_UNAVAILABLE | 409 | 任務固定版本不符或不可用 |
| DEFINITION_UNAVAILABLE | 503 | 伺服器快照毀損／不支援，不回原始定義 |
| DRAFT_CONFLICT | 409 | revision 過期／overflow／EF concurrency |
| TASK_SUBMITTED | 409 | 已提交不可覆寫 |
| IDEMPOTENCY_CONFLICT | 409 | 同 key 非相同提交 |
| WRITE_CONFLICT | 409 | 唯一鍵競爭；以原 key 重讀／重試 |
| RESPONSE_NOT_FOUND | 404 | 尚無本人收據 |

取消及 DB 故障向上傳遞；11 的 Controller error boundary 應以安全 503 回應 DB 故障，不回 exception、SQL 或連線資訊。

## 07：Task／完成度與最小讀取

- `ParentTaskStatus`：taskId、questionnaireVersionId、taskStatus、submittedAtUtc；不含答案。
- 原 `CaseTaskResponse`／`CaseTasksCompletionResponse` 不變。舊 CompletionAsync 跨輪次，不能當有效輪次。
- 指定輪次沿用 `QuestionnaireRoundCompletionEvaluator`；本批不決定有效輪次、不自動更新 CaseStatus。
- `services.AddScopedCaseTaskReads()` 重用 Permission／Handler，註冊 case.read 與 questionnaire.task.read，預設 `DenyCaseTaskReadScope`。
- 07 實作 `ICaseTaskReadScope`，檢查可信 principal、目前帳號／院所及 Task 實際隸屬 Case；不能只檢查 Permission。
- 使用 `ScopedReadAuthorization.CanReadCaseAsync/CanReadTaskAsync`；錯誤延續既有 PermissionLookupExceptionMiddleware。各端 Policy／Controller 接入仍由 07／11 負責。
- 核准虛構資料格式：`caseId, organizationId, taskId, questionnaireVersionId, respondentRole=PARENT, assignmentRound=1, isRequired=true, taskStatus, submittedAtUtc`。本批沒有自行建立 DB rows 或捏造個資／院所回填。

## 交易測試界線

`tests/ParentQuestionnaire/` 是 transactional Fake Store＋Fake Access Adapter，序列化測試資料，不開 DB／HTTP。
可證明服務流程、作者隔離、revision、提交不可覆寫、回滾、去重與再驗證呼叫；不能證明真 Guardian／Consent、MySQL 鎖、FK、重啟持久化或瀏覽器 Cookie。
EF metadata 使用禁止連線 factory。`ParentAuthorSchemaProposal` 只允許精確六項增量，不是 Migration 驗收。
教師 Phase22 可顯式 `--parent-author-schema-proposal` 做回歸，預設模型 gate 保持拒絕，不把新模式用於部署。

## 整合順序

11 固定模型／Adapter → 16 審 FK／鎖與六項作者增量 → 由指定統整者產生新增 Migration／Snapshot → 離線 SQL 審查 → 明確核准隔離 MySQL → 真 MySQL 驗收 → 11 註冊 Gateway／Controller → 07 管理 Git 整合。
原五份 Migration 不得改寫；本分支不自行 Merge／Push。
