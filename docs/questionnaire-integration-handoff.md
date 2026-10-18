# 共用問卷後端介接規格書

提供對象：11 家長／教師端開發。文件依據為程式碼，不是未完成 API 的實作承諾。

- Branch：`feature/shared-rbac-core`
- Commit：`1a87aaba71a49de531f2b118ab5531fc7c9de03b`
- 狀態用語：**已實作**表示指定版本有程式；**尚未整合**表示尚未完成正式身分、資源授權或跨模組接入；**待確認**表示尚須共同核准的規格。
- **正式家長／教師 API 尚未完成。Development API 不可直接當正式入口。**
- 本文件的 07／11／16 責任均為**建議分工，待三方確認**。

## 1. 版本與交接範圍

### 已實作與版本確認

文件建立前確認 Worktree 為 `Smart-Early-Intervention-Care-System-rbac`，Branch／HEAD 符合上述基準，工作目錄乾淨。這是固定 Commit 的交接文件，不保證之後遠端分支仍停在相同版本。

| 功能 | 指定版本狀態 |
| --- | --- |
| 問卷種類／發布版本摘要、任務指派及列表 | 已實作共用核心；正式 Case 授權尚未整合 |
| 教師 Draft、Submit、Receipt | 已實作 EF 流程；限定 Development |
| 定義結構／答案驗證、revision、提交去重 | 已實作 |
| TeacherGrant／Session 撤銷 | 已實作 Development 流程 |
| 指定輪次完成度 | 已實作純計算；尚未整合 API／Workflow |
| 四個 RBAC Policy | 已註冊；尚未套用問卷業務 API |
| 正式家長填答、本人答案查詢 | 尚未實作 |
| 正式教師授權／同意、Task 取消 API | 尚未完成 |
| 提交後 CaseStatus／History 共用交易 | 尚未整合 |

| 範圍 | 元件 | 交接定位 |
| --- | --- | --- |
| 共用核心 | QuestionnaireService、Store、Validator、輪次計算 | 正式專用 API 可重用的基礎，不等於完整正式流程 |
| Development 流程 | TeacherQuestionnaireWorkflowService、TeacherWorkspaceService、DevelopmentTeacherWorkspaceController | 有 EF 保存，但內含固定虛構 Case／Grant 限制 |
| 舊記憶體測試 | ITeacherGrantService、InMemoryTeacherGrantService、TeacherTestFormController | 兩題測試，不作正式串接基礎 |

### 尚未整合

Guardian／家長帳號／作者／同意、正式教師授權，以及醫療資料範圍、CaseStatus／History。現有核心使用的 Case、Questionnaire、Version、Task、Grant、Session、Draft、Response Entity 已在本分支；缺口是正式關係契約與流程，而非必須引用未合併 Entity 才能編譯。

### 待確認

正式專用 API 路由、作者與綁定政策、同意撤銷、跨模組交易及責任分界。

### 相關程式碼位置

- [ApplicationDbContext.cs](../EarlyInterventionCare.Api/Data/ApplicationDbContext.cs)
- [Program.cs](../EarlyInterventionCare.Api/Program.cs)
- [既有共用契約](shared-contract-v1.md)

## 2. 共用 Service 方法契約

以下六個主要元件的 Namespace 均為 `EarlyInterventionCare.Api.Services.Questionnaires`。方法簽章保留 CancellationToken 及預設值；Interface 成員未另加實作中的 async 修飾詞。

### 2.1 QuestionnaireService

來源：[QuestionnaireService.cs](../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireService.cs)。類別為 `public sealed class QuestionnaireService`，依賴 `IQuestionnaireStore`、`TimeProvider`，沒有對應業務服務 Interface。

```csharp
public Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(
    CancellationToken ct = default);
public async Task<IReadOnlyList<CaseTaskResponse>> ListTasksAsync(
    Guid caseId, CancellationToken ct = default);
public async Task<CaseTasksCompletionResponse> CompletionAsync(
    Guid caseId, CancellationToken ct = default);
public async Task<Guid> AssignAsync(
    Guid caseId, AssignQuestionnaireRequest input,
    CancellationToken ct = default);
```

| 方法 | 輸入用途／回傳 | 身分／授權 | 交易／正式介接條件 |
| --- | --- | --- | --- |
| ListAsync | 問卷及 PUBLISHED 版本摘要，無完整題目 | 不取得登入者、不檢查權限 | 無明確 Transaction；API 須決定讀取資格 |
| ListTasksAsync | caseId 的全部輪次任務 | 只檢查 Case 存在 | 無明確 Transaction；上層須驗證 Case scope 並最小化回應 |
| CompletionAsync | 舊版跨輪次摘要 | 同上 | 不可視為有效當輪完成度 |
| AssignAsync | input 指定版本／角色／輪次／必要性，回新 TaskId | 有版本、角色及重複驗證，沒有操作人授權 | Store 單次 SaveChanges；須先確認功能權限及 Case 範圍 |

已實作：CompletionAsync 對所有輪次的 IsRequired 任務計數，取消必要任務仍在總數，零必要任務回 AllRequiredSubmitted=false；不更新 CaseStatus。

尚未整合：正式資源授權、Task 取消、指定有效輪次 API。待確認：誰可指派／閱讀，以及家長專用投影。

### 2.2 TeacherQuestionnaireWorkflowService

來源：[TeacherQuestionnaireWorkflowService.cs](../EarlyInterventionCare.Api/Services/Questionnaires/TeacherQuestionnaireWorkflowService.cs)。類別為 `public sealed class TeacherQuestionnaireWorkflowService`，依賴 `ITeacherQuestionnaireStore`、`QuestionnaireAnswerValidator`、`TimeProvider`、`IAuditLogService`。

```csharp
public async Task<QuestionnaireDraftResponse> GetDraftAsync(
    string? token, Guid taskId, CancellationToken ct = default);
public async Task<QuestionnaireDraftResponse> SaveDraftAsync(
    string? token, Guid taskId, SaveQuestionnaireDraftRequest input,
    string correlationId, CancellationToken ct = default);
public async Task<QuestionnaireReceipt> SubmitAsync(
    string? token, Guid taskId, Guid key, QuestionnaireSubmission input,
    string correlationId, CancellationToken ct = default);
public async Task<QuestionnaireReceipt> GetReceiptAsync(
    string? token, Guid taskId, CancellationToken ct = default);
```

| 參數 | 用途 |
| --- | --- |
| token | 64 字元十六進位教師 Session 憑證，不是 JWT／userId |
| taskId | 必須在 Grant 指定 Task 集合內 |
| input | 草稿或提交內容 |
| key | 對應 Idempotency-Key 的非空 Guid |
| correlationId | 診斷／稽核追蹤，不是授權或去重依據 |
| ct | 非同步取消 |

已實作：每次核對 Session、Grant、指定 Task、Case／TEACHER 角色與任務狀態；草稿限未提交任務；Receipt 只回收據，不回原始答案。四個方法經 Store 開啟交易，寫入與去重回應會 Commit。

正式介接狀態：**不可直接作為正式家長／教師流程**，服務內仍要求 IsDevelopment=true 與固定虛構 Case，不只是 Controller 限制。

尚未整合：Parent JWT／Guardian／作者、正式 Grant／同意、CaseStatus／History。待確認：正式 scope adapter 與交易責任。

### 2.3 IQuestionnaireStore

來源：[IQuestionnaireStore.cs](../EarlyInterventionCare.Api/Services/Questionnaires/IQuestionnaireStore.cs)，EF 實作：[EfQuestionnaireStore.cs](../EarlyInterventionCare.Api/Services/Questionnaires/EfQuestionnaireStore.cs)。

```csharp
Task<IReadOnlyList<QuestionnaireSummary>> ListAsync(CancellationToken ct);
Task<bool> CaseExistsAsync(Guid caseId, CancellationToken ct);
Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct);
Task<bool> AssignmentExistsAsync(Guid caseId, Guid questionnaireId,
    string role, uint round, CancellationToken ct);
Task<bool> InsertAsync(QuestionnaireTask task, CancellationToken ct);
Task<IReadOnlyList<CaseTaskResponse>> ListTasksAsync(Guid caseId, CancellationToken ct);
```

依序負責摘要、Case 存在、內部版本 Entity、同 Case／問卷／角色／輪次重複檢查、新增 Task、全部輪次任務列表。InsertAsync 對指定唯一鍵的競爭衝突回 false。

身分／授權：無。交易：EF 實作不手寫 BeginTransaction，Insert 使用 SaveChanges。可作內部資料層；不能直接對外公開 Entity 或把存在檢查視為授權。待確認：正式 Parent／Teacher 專用查詢投影。

### 2.4 ITeacherQuestionnaireStore／交易介面

來源：[ITeacherQuestionnaireStore.cs](../EarlyInterventionCare.Api/Services/Questionnaires/ITeacherQuestionnaireStore.cs)，實作：[EfTeacherQuestionnaireStore.cs](../EarlyInterventionCare.Api/Services/Questionnaires/EfTeacherQuestionnaireStore.cs)。

```csharp
public interface ITeacherQuestionnaireStore
{
    Task<ITeacherQuestionnaireTransaction?> OpenAsync(
        byte[] sessionHash, CancellationToken ct);
}

public sealed record TeacherTaskEntry(TeacherGrantTask Link, QuestionnaireTask? Task);

public interface ITeacherQuestionnaireTransaction : IAsyncDisposable
{
    TeacherGrant? Grant { get; }
    TeacherSession? Session { get; }
    IReadOnlyList<TeacherTaskEntry> Tasks { get; }
    Task<QuestionnaireVersion?> FindVersionAsync(Guid versionId, CancellationToken ct);
    Task<QuestionnaireDraft?> FindDraftAsync(Guid taskId, CancellationToken ct);
    Task<bool> HasResponseAsync(Guid taskId, CancellationToken ct);
    Task<QuestionnaireResponse?> FindByKeyAsync(Guid grantId, Guid key, CancellationToken ct);
    Task<QuestionnaireReceipt?> FindReceiptAsync(Guid grantId, Guid taskId, CancellationToken ct);
    void AddDraft(QuestionnaireDraft draft);
    void AddResponse(QuestionnaireResponse response);
    Task RevokeSessionsAsync(DateTime now, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
```

已實作：OpenAsync 以 Session 的 SHA-256 hash 找 GrantId，找不到回 null；BeginTransaction 後鎖 Grant、排序鎖 Tasks。Find 系列提供內部讀取／去重，Add 系列追蹤新增，RevokeSessionsAsync 更新 Session 狀態，CommitAsync 先 SaveChanges 再 Commit。DisposeAsync 釋放交易，未提交時清除 tracker。

授權仍由 Workflow 檢查，Store 不取代業務 scope。沒有公開 RollbackAsync；繼承 IAsyncDisposable 的 DisposeAsync 結束交易。

尚未整合：家長交易入口。不能把 parent JWT 當 token、偽造 Grant 或只移除 Development if。待確認：Parent 身分、作者與撤銷競爭模型。

### 2.5 QuestionnaireAnswerValidator

來源：[QuestionnaireAnswerValidator.cs](../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireAnswerValidator.cs)。

```csharp
public ValidatedQuestionnaireContent Validate(
    string snapshot, QuestionnaireSubmission input, DateOnly today, bool complete);

public sealed record ValidatedQuestionnaireContent(
    string? RespondentName, DateOnly? FilledOn,
    QuestionnaireAnswer[] Answers, string? Observation);

// ValidatedQuestionnaireContent 的公開衍生成員：
public string AnswersJson { get; }
public byte[] SubmissionHash(Guid taskId, Guid versionId);
```

snapshot 應來自可信版本，input 是答案，today 是後端日期基準，complete=false 為草稿、true 為提交。現有 Workflow 使用臺北日期。SubmissionHash 用於已完整驗證的提交，實作要求 FilledOn 有值，不應對不完整草稿呼叫。

已實作：題號／選項／重複／完整題數驗證、姓名日期觀察限制、答案排序與 trim、定義結構／重複題號與選項拒絕。明確 type 只接受 singleChoice；舊 snapshot 可省略 type／required，required 有值時須為 boolean。required=false 尚不放寬全部題目提交規則。

身分／交易：無。可共用 Validator，但不代表所有量表已核定。尚未整合：其他題型與真正可選題語意。待確認：家長專用版本與醫院內容。

### 2.6 QuestionnaireRoundCompletionEvaluator

來源：[QuestionnaireRoundCompletionEvaluator.cs](../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireRoundCompletionEvaluator.cs)。

```csharp
public static QuestionnaireRoundCompletion Evaluate(
    Guid caseId, uint assignmentRound, IEnumerable<QuestionnaireRoundTask> tasks);

public sealed record QuestionnaireRoundTask(Guid CaseId, uint AssignmentRound,
    Guid TaskId, bool IsRequired, string? TaskStatus);
public sealed record QuestionnaireRoundCounts(int Submitted, int Pending,
    int InProgress, int Cancelled);
public sealed record QuestionnaireRoundCompletion(Guid CaseId, uint AssignmentRound,
    QuestionnaireRoundCounts Required, QuestionnaireRoundCounts Optional,
    QuestionnaireRoundDecision Decision);
```

Counts 提供 Total，Completion 提供 TotalTaskCount 與 bool? IsComplete。Complete→true、Incomplete→false、CancellationPolicyRequired／NoRequiredTasks／EmptyRound→null。

已實作：要求非空 CaseId、正整數輪次與完整單輪快照，拒絕混合 Case／輪次、空 TaskId、重複、null、非法狀態。身分／交易：無；上層負責可信 scope 與完整任務。

尚未整合：API／CaseStatus。待確認：有效輪次、封存、取消必要任務與零必要任務的業務規則。不自行選最大輪次，也不得把 null 當已完成。

### 2.7 Development 適配與舊測試

`EarlyInterventionCare.Api.Development.TeacherWorkspaceService` 提供 CreateAsync／VerifyAsync／GetTaskAsync／RevokeAsync／LogoutAsync，並在 partial 類別中適配 Draft／Submit／Receipt。它管理現有畫面格式、虛構授權及 Cookie，不是正式家長授權服務。

`ITeacherGrantService`／`InMemoryTeacherGrantService` 是 `/api/dev/teacher-grants`、`/teacher/test-form` 的兩題記憶體測試，不能拿來保存正式問卷。

相關位置：[TeacherWorkspaceService.cs](../EarlyInterventionCare.Api/Development/TeacherWorkspaceService.cs)、[TeacherWorkspaceSubmission.cs](../EarlyInterventionCare.Api/Development/TeacherWorkspaceSubmission.cs)、[ITeacherGrantService.cs](../EarlyInterventionCare.Api/Services/ITeacherGrantService.cs)。

## 3. DTO 與資料結構契約

### 已實作：標示原則

Namespace：`EarlyInterventionCare.Api.DTOs.Questionnaires`。JSON 依目前 ASP.NET Core 預設 camelCase，這兩份 DTO 無自訂欄位名稱註記。非 nullable C# 型別不直接等於已定義完整 HTTP 缺欄位業務規則。

- 「註記必填」表示有 Required。
- 「服務必填」表示 Validator／Workflow 會檢查。
- 回應 DTO 是輸出欄位，無 Required；作為輸入時的必填／省略規則**未明確定義**。

### 3.1 QuestionnaireContracts.cs

來源：[QuestionnaireContracts.cs](../EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireContracts.cs)。

#### AssignQuestionnaireRequest：AssignAsync 輸入

| C# 欄位 | 型別 | JSON | 必填／驗證 |
| --- | --- | --- | --- |
| QuestionnaireVersionId | Guid? | questionnaireVersionId | Required；服務拒絕 null／Guid.Empty |
| RespondentRole | string | respondentRole | Required；PARENT 或 TEACHER |
| AssignmentRound | int? | assignmentRound | Required；1～int.MaxValue |
| IsRequired | bool? | isRequired | Required；false 有效，null 無效 |

#### QuestionnaireVersionSummary：發布版本摘要

| C# 欄位 | 型別 | JSON |
| --- | --- | --- |
| QuestionnaireVersionId | Guid | questionnaireVersionId |
| RespondentRole | string | respondentRole |
| VersionNumber | string | versionNumber |
| VersionStatus | string | versionStatus |

均為非 nullable 輸出；無 DTO 值域／長度驗證。List Store 目前選取 PUBLISHED。沒有完整題目。

#### QuestionnaireSummary：問卷清單

| C# 欄位 | 型別 | JSON |
| --- | --- | --- |
| QuestionnaireId | Guid | questionnaireId |
| QuestionnaireCode | string | questionnaireCode |
| Title | string | title |
| Versions | IReadOnlyList&lt;QuestionnaireVersionSummary&gt; | versions |

均為非 nullable 輸出；Versions 可為空集合；無輸入必填註記。

#### CaseTaskResponse：個案任務列表

| C# 欄位 | 型別 | JSON |
| --- | --- | --- |
| TaskId | Guid | taskId |
| CaseId | Guid | caseId |
| QuestionnaireId | Guid | questionnaireId |
| QuestionnaireVersionId | Guid | questionnaireVersionId |
| QuestionnaireTitle | string | questionnaireTitle |
| VersionNumber | string | versionNumber |
| RespondentRole | string | respondentRole |
| AssignmentRound | uint | assignmentRound |
| IsRequired | bool | isRequired |
| TaskStatus | string | taskStatus |
| CreatedAtUtc | DateTime | createdAtUtc |
| UpdatedAtUtc | DateTime | updatedAtUtc |
| SubmittedAtUtc | DateTime? | submittedAtUtc |

均為輸出欄位；SubmittedAtUtc 可空。EF Store 將時間標為 UTC。不是正式家長專用 DTO，沒有作者檢查，也不含 CancelledAtUtc。

#### CaseTasksCompletionResponse：舊版跨輪次摘要

| C# 欄位 | 型別 | JSON |
| --- | --- | --- |
| CaseId | Guid | caseId |
| TotalTaskCount | int | totalTaskCount |
| RequiredTaskCount | int | requiredTaskCount |
| RequiredSubmittedCount | int | requiredSubmittedCount |
| RequiredPendingCount | int | requiredPendingCount |
| RequiredInProgressCount | int | requiredInProgressCount |
| AllRequiredSubmitted | bool | allRequiredSubmitted |

均為輸出欄位，無輸入驗證。不能表達指定輪次、取消數、待決策或封存狀態。

### 3.2 QuestionnaireWorkflowContracts.cs

來源：[QuestionnaireWorkflowContracts.cs](../EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireWorkflowContracts.cs)。

#### QuestionnaireAnswer：Draft／Submit 的答案項目

| C# 欄位 | 型別 | JSON | 驗證 |
| --- | --- | --- | --- |
| QuestionId | string? | questionId | DTO 可空；已提供答案中必須非 null 且存在於 snapshot |
| OptionValue | string? | optionValue | DTO 可空；已提供答案中必須非 null 且屬該題合法選項 |

答案題號不得重複；沒有 Required 註記，規則由 Validator 執行。

#### QuestionnaireSubmission：正式提交內容

| C# 欄位 | 型別 | JSON | 必填／驗證 |
| --- | --- | --- | --- |
| QuestionnaireVersionId | Guid | questionnaireVersionId | 無 Required；Workflow 要求等於 Task 版本 |
| RespondentName | string? | respondentName | 服務必填；trim 非空、最多 50 字元、不含控制字元 |
| FilledOn | string? | filledOn | 服務必填；yyyy-MM-dd，不晚於後端 today |
| Answers | QuestionnaireAnswer?[]? | answers | 服務必填；全部題目各一合法答案，不含 null |
| Observation | string? | observation | 選填；trim 後最多 1000 字元，空白正規化 null |

均無 DTO Required；實際完整提交規則由 Validator／Workflow 執行。

#### SaveQuestionnaireDraftRequest：保存草稿

| C# 欄位 | 型別 | JSON | 必填／驗證 |
| --- | --- | --- | --- |
| QuestionnaireVersionId | Guid | questionnaireVersionId | Workflow 要求等於 Task 版本 |
| ExpectedRevision | ulong? | expectedRevision | Required；服務須匹配目前 revision，拒絕 null／ulong.MaxValue |
| RespondentName | string? | respondentName | 可空；有值適用姓名限制 |
| FilledOn | string? | filledOn | 可空；有值適用格式／日期限制 |
| Answers | QuestionnaireAnswer?[]? | answers | 可空／缺題；已提供答案必須合法 |
| Observation | string? | observation | 選填；最多 1000 字元 |

首次保存 ExpectedRevision=0。required=false 尚不放寬正式提交全部題目的規則。

#### QuestionnaireDraftResponse：讀取／保存草稿回應

| C# 欄位 | 型別 | JSON | 回傳行為 |
| --- | --- | --- | --- |
| TaskId | Guid | taskId | 目標 Task |
| QuestionnaireVersionId | Guid | questionnaireVersionId | 固定版本 |
| Revision | ulong | revision | 無草稿為 0 |
| RespondentName | string? | respondentName | 可空 |
| FilledOn | string? | filledOn | yyyy-MM-dd 或 null |
| Answers | IReadOnlyList&lt;QuestionnaireAnswer&gt; | answers | 無草稿為空集合 |
| Observation | string? | observation | 可空 |
| UpdatedAtUtc | DateTime? | updatedAtUtc | 無草稿為 null |
| TaskStatus | string | taskStatus | 目前狀態 |

輸出沒有 Required；前端須保留完整 ulong 精度。

#### QuestionnaireReceipt：提交／重送／收據查詢回應

| C# 欄位 | 型別 | JSON |
| --- | --- | --- |
| ResponseId | Guid | responseId |
| TaskId | Guid | taskId |
| QuestionnaireVersionId | Guid | questionnaireVersionId |
| SubmittedAtUtc | DateTime | submittedAtUtc |
| GrantStatus | string | grantStatus |
| Replayed | bool | replayed |

均為非 nullable 輸出，無輸入 Required。刻意不回答案、姓名、Observation、Key 或 hash，不能代替家長本人結果 DTO。

### 尚未整合／待確認

Draft 只有 LastSavedByGrantId，Response 只有 SubmittedByGrantId，沒有可信家長作者 User ID。尚無家長結果閱讀 DTO，也不能用姓名、現有 Guardian 或 null Grant 推定作者。家長去重範圍、多家長接續草稿、重新綁定後歷史閱讀均待確認。

相關位置：[QuestionnaireDraft.cs](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireDraft.cs)、[QuestionnaireResponse.cs](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireResponse.cs)。

## 4. 錯誤代碼與 HTTP Response

### 已實作：入口與格式

`GET /api/questionnaires`、`GET/POST /api/cases/{caseId}/tasks`、`GET /api/cases/{caseId}/tasks/completion` 是既有 Controller 路徑；目前限 ACTIVE ADMIN／CASE_MANAGER，Case 路徑另限 Development 虛構個案。教師流程位於 `/api/dev/teacher-workspace/*`，Production 404。這些不是新核准的正式 Parent／Teacher 路由。

QuestionnaireErrorsAttribute（F1）：

```json
{"code":"ASSIGNMENT_EXISTS","message":"同個案、問卷、角色與輪次的任務已存在。"}
```

WorkspaceErrorsAttribute（F2）：

```json
{"code":"DRAFT_CONFLICT","message":"草稿版本已變更，請重新讀取。","traceId":"伺服器追蹤值"}
```

框架回應（F3）：Unauthorized／Forbid／NotFound、模型繫結／驗證未統一成 F1／F2，可能為空回應、ProblemDetails、ValidationProblemDetails。

| HTTP | Code | 條件／來源 | 格式 |
| --- | --- | --- | --- |
| 400 | INVALID_INPUT | QuestionnaireService 無效 Case／指派參數 | F1 |
| 400 | ROLE_MISMATCH | AssignAsync 角色不符 | F1 |
| 400 | INVALID_INPUT | Workflow／Validator：revision、Key、姓名／日期／觀察不符 | F2 |
| 400 | INVALID_ANSWERS | Validator：缺題、重複、未知題、非法選項 | F2 |
| 400 | INVALID_INPUT | 教師 Controller：Idempotency-Key 非有效非空 UUID | code、message |
| 401 | SESSION_REQUIRED | Workflow.Open：token 無效／無 Session；Controller.Task 無工作台 | F2 或含 traceId 的直接 JSON |
| 401 | GRANT_UNAVAILABLE | Workflow scope／USED 新提交；Controller.Verify 失敗 | F2 或含 traceId 的直接 JSON |
| 401 | 無自訂 Code | Authentication／CheckUser：JWT、userId、帳號無效 | F3 |
| 403 | CASE_ACCESS_NOT_INTEGRATED | QuestionnairesController：非允許 Development Case | F1 類型 |
| 403 | INVALID_ORIGIN | 教師 Controller 草稿／提交來源檢查 | code、message |
| 403 | 無自訂 Code | CheckUser.Forbid：角色不允許 | F3 |
| 404 | CASE_NOT_FOUND | QuestionnaireService：Case 不存在 | F1 |
| 404 | VERSION_NOT_FOUND | AssignAsync：版本不存在 | F1 |
| 404 | TASK_NOT_FOUND | Workflow.RequireScope：Task 不在授權範圍或關係不符 | F2 |
| 404 | RESPONSE_NOT_FOUND | GetReceiptAsync：未提交／沒有該 Grant 收據 | F2 |
| 404 | 無自訂 Code | 非 Development／撤銷目標不存在 | F3 |
| 409 | VERSION_NOT_PUBLISHED | AssignAsync：不能新指派的版本 | F1 |
| 409 | ASSIGNMENT_EXISTS | AssignAsync：重複或唯一鍵競爭 | F1 |
| 409 | DRAFT_CONFLICT | SaveDraft／Store：revision／EF concurrency | F2 |
| 409 | IDEMPOTENCY_CONFLICT | SubmitAsync：同 Grant＋Key 不同 Task／內容 | F2 |
| 409 | TASK_SUBMITTED | RequireEditable：已提交、取消或不可編輯 | F2 |
| 409 | VERSION_MISMATCH | RequireVersion：版本或其關係不符 | F2 |
| 409 | TASK_UNAVAILABLE | TeacherWorkspaceService：Development Task／版本／Map 不可用 | F2 |
| 409 | GRANT_CONFLICT | CreateAsync：測試入口不能替換正式 Grant | F2 |
| 409 | GRANT_USED | RevokeAsync：USED 不可撤銷 | F2 |
| 429 | 無自訂 Code | teacher-workspace-code RateLimiter | 未定義統一 JSON |
| 503 | SERVICE_UNAVAILABLE | QuestionnaireErrors：DB／更新／逾時 | F1 |
| 503 | SAVE_UNAVAILABLE | WorkspaceErrors：DB／更新／JSON／InvalidOperationException | F2 |
| 503 | AUTHORIZATION_UNAVAILABLE | PermissionLookupExceptionMiddleware：權限查詢失敗 | code、message |

Validator 解析到損壞 snapshot 時拋固定 InvalidOperationException，既有 WorkspaceErrors 回 503；非法使用者答案則是 INVALID_ANSWERS／400。部分填答欄位先驗證，因此不宣稱每次請求都先檢查 snapshot。

### 尚未整合

401／403／404／409 已使用，但格式及部分語意未統一；取消 Task 也使用 TASK_SUBMITTED。RequestSizeLimit、框架驗證、RateLimiter 未統一業務 JSON。舊兩題測試頁另有文字／HTML，不是正式契約。

### 待確認：建議統一格式（尚未實作）

```json
{"code":"DRAFT_CONFLICT","message":"草稿版本已變更，請重新讀取。","traceId":"伺服器追蹤值"}
```

另須決定模型驗證 errors、429 重試資訊、越權回 403／404 的隱私策略、取消 Task 錯誤碼。本文件不變更現有回應。

### 相關程式碼位置

- [QuestionnairesController.cs](../EarlyInterventionCare.Api/Controllers/QuestionnairesController.cs)：QuestionnaireErrorsAttribute／CheckUser／CaseAllowed
- [DevelopmentTeacherWorkspaceController.cs](../EarlyInterventionCare.Api/Controllers/DevelopmentTeacherWorkspaceController.cs)：WorkspaceErrorsAttribute／各 Action
- [PermissionLookupExceptionMiddleware.cs](../EarlyInterventionCare.Api/Services/Authorization/PermissionLookupExceptionMiddleware.cs)

## 5. Transaction／並發／安全規格

### 5.1 已實作：交易生命週期

| 操作 | Begin／Commit | Rollback／清理 |
| --- | --- | --- |
| 教師 Draft／Submit／Receipt | Store.OpenAsync Begin；Workflow 呼叫交易 CommitAsync | 未提交交易 Dispose，清除 tracker |
| Grant 建立／驗證／撤銷／登出 | TeacherWorkspaceService 各方法 Begin／SaveChanges／Commit | await using Dispose；Verify 拒絕另明確 Rollback |
| 工作台查詢 | GetTaskAsync Begin；更新 LastSeen 後 Commit | 失敗 Dispose |
| 指派 Task | Store.InsertAsync 單次 SaveChanges，無手寫 Begin | 指定唯一鍵衝突轉 false |
| Validator／輪次計算 | 無 DB | 無 |

教師 Store 順序：Session hash 查 GrantId → BeginTransaction → Grant FOR UPDATE → 依 TaskId 排序鎖 Task → 驗證／操作 → SaveChanges → Commit。唯讀 Draft／Receipt 亦開交易，通常以 Dispose 結束。

### 5.2 Draft Revision

- 每 Task 最多一份 Draft；無草稿回 Revision=0。
- 首次 ExpectedRevision=0，後續用前次成功 revision，成功＋1。
- 不符或上限回 DRAFT_CONFLICT／409；Revision 為 EF concurrency token。
- Task 須 PENDING／IN_PROGRESS 且沒有 Response；取消／提交後不能再寫。
- 前端遇 409 應保留輸入並重讀，不自動換 revision 覆寫。ulong 不能失去整數精度。

### 5.3 Idempotency-Key 與一致性

提交順序：驗證 Session／Grant／Task → 版本與完整答案 → 正規化 hash → 查 Grant＋Key → 同請求回原收據 → 新請求確認可編輯 → 新增 Response → Task SUBMITTED → 全部指定 Tasks 提交時 Grant USED 並撤銷 Sessions → Commit。

已存在 Mapping：`uq_responses_task`、`uq_responses_grant_key`、`uq_tasks_assignment`。同 Key 不同 Task／內容回 409。不同 Key 競爭同 Task 不應建立兩份答案。

Response／Task／Grant／Session 效果在同一次 Commit。沒有原始答案更新／刪除 API，但也沒有 trigger 保證任何 raw SQL 都不可改寫。Audit 是 Commit 後 best-effort，不是零遺失同交易稽核。

### 5.4 Grant／Session 與收據

- Verify 成功建立 8 小時 Session，並撤銷該 Grant 原 ACTIVE Sessions。
- Grant 全部指定 Tasks 提交後 USED；Sessions 撤銷，但提交會話保留最多 15 分鐘收據窗口，且不超過原 Session 到期。
- Receipt 不回答案，也不重新開放題目／草稿；USED 不允許新 Key 建答案。
- Logout 鎖 Grant，撤銷 Session 並清除 ReceiptExpiresAtUtc。
- Grant Revoke 鎖同一 Grant，改 REVOKED 並撤銷 ACTIVE Sessions。
- Revoke 先完成，Submit 拒絕；最後一份 Submit 先完成，Revoke 回 GRANT_USED／409。部分提交後 Revoke 保留已提交答案並阻擋剩餘填寫。

### 5.5 RBAC／JWT／Cookie／Origin

已註冊四個 Policy：

```text
Permission:case.status.update
Permission:case.status_history.read
Permission:case.read
Permission:questionnaire.task.read
```

RBAC Handler 取唯一已驗證 userId，PermissionService 查目前帳號狀態／角色配置，不以 JWT 舊 role／permission 放行，ADMIN 無隱含全權。QuestionnairesController 尚未套上述 Policy，仍使用角色與 Development Case 檢查；Teacher Workflow 使用 Grant scope，不是 JWT RBAC。

JWT：JwtBearer／HS256，檢查 key、issuer、audience、lifetime，要求簽章及 expiration，ClockSkew=30 秒，MapInboundClaims=false、SaveToken=false。JwtOptions 本階段要求 30 分鐘期限。沒有 SessionVersion／SecurityStamp 即時撤銷。

| Development 教師 Cookie | 實際值 |
| --- | --- |
| Name | EarlyCare.DevTeacher |
| Path | /api/dev/teacher-workspace |
| HttpOnly／SameSite | true／Strict |
| Secure | Request.IsHttps |
| Cookie Expires | 未設定 |
| DB Session | 8 小時；DB 存 SHA-256，Cookie 存高熵憑證 |

Draft 要求 X-Teacher-Draft: 1，Submit 要求 X-Teacher-Submission: 1；Sec-Fetch-Site 不得 cross-site，Origin 存在時須同來源，但允許缺 Origin。不是 ASP.NET Antiforgery Token。其他教師寫入路徑未套同一 IsSameOriginWrite，不得宣稱完整 Production CSRF 已完成。

**07 尚未合入的醫療 Cookie／可信 Origin／Antiforgery，不屬於本分支已完成內容。**

### 5.6 測試證據與限制

| 紀錄 | 證明範圍 | 不代表 |
| --- | --- | --- |
| RBAC 118 項 | 核心、ADO 攔截、替身與隔離 HTTP | 真實 MySQL 全整合 |
| Phase 2-2 158 項 | Fake Store 的 Draft／提交／撤銷／rollback／並行 | 真實 DB 鎖定及隔離 |
| 輪次 37 項 | 純計算與待決策狀態 | 有效輪次或 CaseStatus 已整合 |
| 2026-10-06 提交驗收文件 | 初期 Development 在獨立 MySQL 的故障、六筆同 Key、提交／撤銷競爭、重啟收據紀錄 | 最新家長／教師正式跨模組流程驗收 |

本次文件整理未重跑測試。不能把舊 MySQL 紀錄延伸成最新正式整合已完成，也不能把 Fake Store 的 Commit 故障當真實 DB 故障。

### 尚未整合／待確認

Guardian 綁定撤銷、同意撤回不在現有教師交易鏈；Controller 先查一次 scope 不足以避免競爭。CaseStatus／History 亦未接入；不能在 Submit 已 Commit 後再以獨立交易聲稱原子完成。待確認跨模組鎖順序、Session 撤銷策略、真正 MySQL 驗收基準。

### 相關程式碼位置

- EfTeacherQuestionnaireStore.OpenAsync／CommitAsync／DisposeAsync
- TeacherQuestionnaireWorkflowService.SaveDraftAsync／SubmitAsync
- TeacherWorkspaceService.VerifyAsync／RevokeAsync／LogoutAsync
- [CoreModelConfiguration.cs](../EarlyInterventionCare.Api/Data/CoreModelConfiguration.cs)
- [PermissionAuthorizationExtensions.cs](../EarlyInterventionCare.Api/Services/Authorization/PermissionAuthorizationExtensions.cs)
- [PermissionService.cs](../EarlyInterventionCare.Api/Services/Authorization/PermissionService.cs)
- [JwtOptions.cs](../EarlyInterventionCare.Api/Options/JwtOptions.cs)、Program.cs
- [teacher-submission-testing.md](teacher-submission-testing.md)、[questionnaire-round-completion-core.md](questionnaire-round-completion-core.md)

## 6. 家長／教師 API 介接指南

本章 Adapter／Service 均是**建議新增的責任邊界，不是本版本已存在的方法或類別**。正式路由另行確認。所有分工均為**建議分工，待三方確認**。

### 6.1 家長問卷任務清單

- 已實作可重用：QuestionnaireService.ListTasksAsync 及任務投影概念。
- 尚未整合：Parent 專用授權／查詢 Adapter；可信 userId → 有效 Guardian／Case → PARENT Tasks，不能公開醫療清單原始結果。
- 建議分工：11 專用 API／scope，16 共用查詢；待三方確認。
- 依賴／待確認：User、Guardian／Case、Task／Version；取消／歷史輪次及欄位政策。
- 測試：跨家長、跨 Case、未綁定、撤銷拒絕；不回教師答案或他人資料。
- 依據：QuestionnaireService.ListTasksAsync／EfQuestionnaireStore.ListTasksAsync。

### 6.2 家長取得草稿

- 已實作可重用：QuestionnaireDraftResponse、revision／答案模型。
- 尚未整合：Parent 交易入口與作者檢查。不能直接呼叫要求教師 Grant 的 GetDraftAsync。
- 建議分工：11 API／身分，16 儲存及作者模型；待三方確認。
- 依賴／待確認：有效綁定、PARENT Task、可信作者；同 Task 多家長與重綁政策。
- 測試：同 Case 不同作者隔離、撤銷拒絕、無草稿 revision 0。
- 依據：TeacherQuestionnaireWorkflowService.GetDraftAsync／RequireScope、QuestionnaireDraft。

### 6.3 家長儲存草稿

- 已實作可重用：SaveQuestionnaireDraftRequest、Validator、revision。
- 尚未整合：交易內重查／鎖定綁定及作者的 Parent 寫入流程。
- 建議分工：11 Adapter，16 共用交易／Schema；待三方確認。
- 依賴／待確認：可信 User、有效綁定、Task、Draft 作者與鎖順序。
- 測試：首次並行保存、舊 revision 409、撤銷競爭、提交後拒絕。
- 依據：SaveDraftAsync、EfTeacherQuestionnaireStore、QuestionnaireDraft。

### 6.4 家長正式提交

- 已實作可重用：QuestionnaireSubmission、Validator、hash、一 Task 一 Response。
- 尚未整合：Parent 提交協調器、作者欄位與家長去重範圍。不能直接依賴 null Grant＋Key 作家長去重保證。
- 建議分工：16 共用提交／模型，11 API／身分接入；待三方確認。
- 依賴／待確認：作者、綁定、Task／Version、Key 範圍及 Workflow 交易。
- 測試：同 Key 重送／不同內容、不同 Key 並行、撤銷競爭、失敗全 rollback。
- 依據：SubmitAsync、QuestionnaireResponse、CoreModelConfiguration。

### 6.5 家長查詢本人結果

- 已實作可重用：Response Entity 作內部資料源，不直接回 Entity。
- 尚未整合：專用結果 DTO、作者＋有效綁定閱讀 Service；Receipt 不含答案，不能代替。
- 建議分工：11 API／展示，16 作者及查詢模型；待三方確認。
- 依賴／待確認：可信提交者 User ID（目前缺欄位）、題目版本及重綁歷史權限。
- 測試：同 Case 其他家長／教師答案拒絕、撤銷拒絕、答案不可修改。
- 依據：QuestionnaireResponse、QuestionnaireReceipt、GetReceiptAsync。

### 6.6 教師取得指定任務

- 已實作可重用：GrantTask scope、Session hash、版本 snapshot；GetTaskAsync 可參考。
- 尚未整合：正式 Grant／Session 適配及最小任務 DTO；不能沿用固定虛構 Case、Notice 或兩題記憶體入口。
- 建議分工：11 教師 API，16 共用授權／儲存；待三方確認。
- 依賴／待確認：同意、Grant、TEACHER Tasks、Session、Cookie／CSRF、最小個案欄位。
- 測試：跨 Grant／Case／PARENT Task 拒絕；撤銷／到期／取消不得填。
- 依據：TeacherWorkspaceService.GetTaskAsync／Map、Workflow.RequireScope。

### 6.7 教師草稿及提交

- 已實作可重用：Workflow 演算法、DTO、Validator、EF Store。
- 尚未整合：正式 scope adapter／Controller；不能只刪除 Development if。
- 建議分工：16 核心調整，11 正式 API／前端；待三方確認。
- 依賴／待確認：正式 Grant／Session／同意、交易、收據期限與錯誤格式。
- 測試：保留 Phase 2-2 情境，再加正式 HTTP／Cookie／隔離 MySQL。
- 依據：TeacherQuestionnaireWorkflowService、DevelopmentTeacherWorkspaceController。

### 6.8 教師授權撤銷

- 已實作可重用：RevokeAsync 的 Grant 鎖、Session 撤銷、USED 拒絕邏輯。
- 尚未整合：可信家長操作人、有效綁定、Grant 建立者及同意檢查。
- 建議分工：11 發起 API／關係及同意事件，16 共用撤銷交易；待三方確認。
- 依賴／待確認：IssuedByUserId、ConsentReferenceId、Guardian；解除綁定／撤回同意範圍與通知方式。
- 測試：非本人／未綁定／跨 Case 拒絕、Revoke／Submit 競爭、重複撤銷、USED 不可改 REVOKED。
- 依據：TeacherWorkspaceService.RevokeAsync／LogoutAsync、TeacherGrant、TeacherSession。

## 7. 待確認事項與三人分工

### 已實作基礎／尚未整合

現有 DTO、Validator、教師 EF 流程與輪次核心可交接；正式身分、作者、資源範圍、同意、跨模組交易及 API 尚未整合。測試通過不構成正式可用宣告。

### 建議分工，待三方確認

| 項目 | 建議主要負責 | 協作 |
| --- | --- | --- |
| DTO／Validator／revision／去重 | 16 | 11 確認使用方式 |
| 共用 Entity／Migration／Snapshot 統整 | 16 | 07／11 提供模型 |
| 家長／教師專用 API／前端 | 11 | 16 提供核心 |
| Guardian／身分／同意契約 | 11 | 16 資料及交易整合 |
| 醫療 Authentication／資料範圍 | 07 | 16 RBAC |
| CaseStatusWorkflow／History | 07 | 16 提交協調器 |
| 輪次 evaluator 統一 | 07＋16 | 11 消費結果 |
| 正式 Cookie／CSRF／Session | 07＋11＋16 | 共同契約 |

### 待確認

1. 家長 Draft／Response 作者與歷史處理。
2. Guardian 撤銷及同意撤回與 Submit 的交易保護。
3. 正式 Grant、Session、API 路由與錯誤格式。
4. 有效輪次、封存、取消必要任務、零必要任務／空輪次。
5. 07 Workflow 的交易擁有者、鎖順序、History 去重。
6. 家長／教師正式版本、題型及量表內容。
7. JWT／Cookie 停用、改密碼後的撤銷策略。
8. Permission Seed、角色矩陣及資源範圍。
9. 最新整合版的實際 MySQL 驗收。

應保留的契約：家長只能看教師完成狀態，不讀教師答案；原始答案提交後不提供修改；撤銷不是完成；Grant 全部 Tasks 與 Case 當輪必要 Tasks 是不同判斷；輪次 Complete 不直接授權 Case 狀態轉移；不以客戶端 userId／角色／院所作可信身分。

### 相關程式碼位置

TeacherQuestionnaireWorkflowService 的 TODO／RequireScope、ITeacherQuestionnaireStore 的 Parent adapter TODO、QuestionnaireDraft／Response、Program.cs，以及 [輪次契約](questionnaire-round-completion-core.md)。

另有已知文字落差：TeacherWorkspaceService.Notice 仍稱「草稿僅暫存本頁」，不符目前 EF 草稿；應另案修正，本文件未修改程式。

## 附錄 A. 給 11 的交接摘要（可直接貼 LINE）

> 共用問卷版本：feature/shared-rbac-core，Commit 1a87aaba71a49de531f2b118ab5531fc7c9de03b。
>
> 已有問卷／任務 DTO、定義與答案驗證、Draft revision、教師提交去重／收據、EF 交易及指定輪次計算。請沿用 Task／Draft／Response，不另建第二套答案表。
>
> 正式家長／教師 API 尚未完成。教師 Workflow 仍限定 Development 虛構 Case＋TeacherGrant Session，不能直接改路由使用。家長 Draft／Submit／本人答案查詢還缺可信作者、Guardian 交易內檢查及家長去重範圍。
>
> Receipt 只有提交證明、沒有答案；家長不能讀教師答案。正式 Grant 還要接綁定、同意及撤銷。RBAC 四個 Policy 已註冊，但問卷 Controller 尚未接入。
>
> 指定輪次核心不自選有效輪次；取消必要任務回待決策。測試紀錄 RBAC 118、Phase 2-2 158、輪次 37 通過，多數離線／Fake Store，不等於正式跨模組 MySQL 驗收。
>
> 請先確認作者、綁定／同意、錯誤格式與交易責任，再開始正式 API；07／11／16 分工為建議，待三方確認。

## 附錄 B. 11 開始串接前必須確認的問題清單

1. 是否以完整 SHA 1a87aab… 為基準？
2. 可信家長 userId、帳號／院所狀態由誰檢查？
3. 有效 Guardian 關係如何在交易內重查？
4. 多家長草稿／作者及重綁歷史如何處理？
5. 家長 Idempotency-Key 的唯一範圍？
6. 正式 Grant 建立者、同意、撤回與解除綁定如何協調？
7. 正式教師 Cookie／CSRF／Session 規格？
8. 家長版本、題型與可選題是否核定？
9. 11 API 與 16 核心調整責任是否經三方確認？
10. 錯誤格式及 403／404 隱私政策？
11. 有效輪次／封存及 Workflow 交易如何接入？
12. 隔離 MySQL＋真實 HTTP 驗收時機與範圍？

## 附錄 C. 建議下一階段實作順序

1. 固定 SHA、確認建議分工及正式 API 契約。
2. 定作者、Guardian 交易內 scope、同意／撤銷。
3. 16 統整必要模型與 Migration 計畫，另行核准後執行。
4. 家長任務清單與授權隔離測試。
5. 家長 Draft／Submit／本人答案查詢。
6. 正式教師 Grant／Session／撤銷與專用 API。
7. 統一錯誤格式及 Cookie／CSRF。
8. 與 07 接有效輪次、Workflow／History 共用交易。
9. 隔離 MySQL、HTTP、撤銷／提交競爭與回歸驗收。

## 附錄 D. 目前不應宣稱已完成的功能

- 正式 Production 家長／教師 API。
- 家長可信作者、本人答案閱讀授權。
- Guardian／同意撤銷與提交的原子保護。
- 正式家長建立教師 Grant、Task 取消服務。
- 有效輪次／封存、CaseStatus 自動更新、History 同交易整合。
- 07 醫療 Cookie／CSRF 已合入本分支。
- 全部錯誤均為統一 JSON。
- 全部量表、版本、題型已核定。
- 最新正式跨模組 MySQL 全驗收。
- 資料庫層全面答案不可變、稽核零遺失。
