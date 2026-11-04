# V5.3 問卷完成度／Workflow 整合候選設計

本機盤點基準：`codex/v53-parent-core`，`3463e37cfb3e81bf81e0450cd322a0895e16f567`。本文件是三人審查提案，不是實作完成、正式政策、Migration 或部署執行授權。本輪僅靜態閱讀，沒有執行測試、啟動服務或連接資料庫。

11／07 尚未提供下一個可整合固定版本；不 Fetch、不檢查其他 Worktree 或未提交成果。先前固定版本與未交付清單參考 [三人計畫](v53-three-way-integration-plan-20261030.md)，不把其中歷史紀錄當作今日新增交付。

標籤：**現有程式**表示此 HEAD 可見；**候選**表示本文件建議、未實作；**待 11／07**表示需共同契約；**待老師／院方**表示業務政策未核定。以下所有新增型別、欄位及方法簽章均為候選。

## 1. 現有程式與模型清單

下列相對路徑均以本文件所在 docs 為基準，可沿固定 HEAD 重現閱讀。

| 範圍 | 現有程式與實際能力 | 不能當成已完成 |
|---|---|---|
| 家長 Submit | [ParentQuestionnaireGateway](../EarlyInterventionCare.Api/Services/Questionnaires/ParentQuestionnaireGateway.cs)：舊公開 Submit 與 internal SubmitCommandAsync 共用 SubmitCoreAsync；新版預設 Deny，先授權／作者遮蔽，再狀態／版本／內容檢查 | 沒有正式 Guardian／Consent Adapter、Stamp 或 Workflow 呼叫 |
| 家長首次／重送 | 新 Command 首次比較 draft?.Revision ?? 0；保存 Relationship、canonical v1 HashVersion／PayloadHash；重送先核對同作者／Task／Key／版本／內容，再回原 ResponseId，不比較更新後 Draft Revision | 舊公開 Submit 仍用共用 SubmissionHash，不等於新版安全入口；未知 HashVersion 不能成功重送 |
| 家長 Draft／Receipt | 同檔 internal guarded 方法共用 Store；歷史收據查詢獨立於重送，可在作者／資格核可後讀 NULL HashVersion 的舊收據 | 作者未知仍拒絕；不是「NULL HashVersion 就可跨作者讀取」 |
| 家長交易 | [EfParentQuestionnaireStore](../EarlyInterventionCare.Api/Services/Questionnaires/EfParentQuestionnaireStore.cs)：乾淨獨佔 scoped Context、Begin；Task FOR UPDATE；CommitAsync 合併 SaveChanges 與 Commit；Dispose 清追蹤；Gateway Finish 先 Revalidate 再 Commit | 沒有 save-only 階段、Case 鎖或全輪次快照；AsNoTracking 不保證 current read |
| 教師 Submit | [TeacherQuestionnaireWorkflowService](../EarlyInterventionCare.Api/Services/Questionnaires/TeacherQuestionnaireWorkflowService.cs)：既有 Grant／Session 範圍、Validator、共用 SubmissionHash；同 Grant／Key／Task／Hash 重送回原收據；首次保存 Response＋Task | 名稱雖含 Workflow，程式明確不更新 CaseStatus；仍限制 Development Grant／指定虛構 Case |
| 教師 Grant 完成 | 該 Grant 所有指定 Task SUBMITTED 才改 USED、撤銷 Session 並設定收據窗口；同交易提交 | Grant 任務集合不是整個 Case 的有效輪次集合，不能當作 Case 必填完成度 |
| 教師交易 | [EfTeacherQuestionnaireStore](../EarlyInterventionCare.Api/Services/Questionnaires/EfTeacherQuestionnaireStore.cs)、[交易介面](../EarlyInterventionCare.Api/Services/Questionnaires/ITeacherQuestionnaireStore.cs)：Begin，Grant 鎖，再依 TaskId 排序鎖 Grant 任務；SaveChanges＋Commit 合併 | 介面沒有 DbContext／save-only 能力；沒有 Case 優先鎖或家長資格協調 |
| Task | [QuestionnaireTask](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireTask.cs)：CaseId、問卷／版本、角色、uint AssignmentRound、IsRequired、TaskStatus、提交／取消時間 | 沒有有效輪次指標、輪次封存列或持久政策確認旗標 |
| Draft／Response | [Draft](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireDraft.cs)、[Response](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireResponse.cs)：nullable 家長作者／教師 Grant／Relationship；Draft Revision；Response Key／Hash／nullable HashVersion | 不代表欄位都已進 Migration／實際資料庫；不推定舊作者、關係、Hash 演算法 |
| Case／History | [CaseRecord](../EarlyInterventionCare.Api/Data/Entities/CaseRecord.cs) 有 CaseStatus／時間；[CaseStatusHistory](../EarlyInterventionCare.Api/Data/Entities/CaseStatusHistory.cs) 有狀態前後值、來源、nullable ChangedByUserId／Reason | 此 HEAD 的 CaseRecord 無 OrganizationId、Revision 或有效輪次欄位；不能假設 11 模型已合入。History 無 Response／輪次事件去重欄位 |
| 原始完成度 API | [QuestionnaireService.CompletionAsync](../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireService.cs)、[現有 DTO](../EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireContracts.cs)：全 Case Task 必填計數，非零且全 SUBMITTED 才 AllRequiredSubmitted | 未選有效輪次、未封存、未驗 Response 一致性；歷史輪次／CANCELLED 仍影響集合，不可直接觸發 Workflow |
| 兩套純 evaluator | [RoundCompletionEvaluator](../EarlyInterventionCare.Api/Services/Workflow/RoundCompletionEvaluator.cs) 與 [QuestionnaireRoundCompletionEvaluator](../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireRoundCompletionEvaluator.cs) 均要求明確 Case／正整數輪次，拒絕混合範圍／重複 TaskId／未知狀態 | 只計算呼叫者提供的 Task 狀態；不查 DB、不選有效輪次、不判權限、不證明 Response 已保存 |
| Workflow 純政策 | [CaseQuestionnaireWorkflowPolicy](../EarlyInterventionCare.Api/Services/Workflow/CaseQuestionnaireWorkflowPolicy.cs)：只容許 WAITING_FORM → FORM_COMPLETED；未確認有效輪次／未封存／取消政策待定時 PendingIntegration；已 FORM_COMPLETED 為 NoAction | confirmation bool 必須來自可信持久來源，不能傳 true 代替模型；NoRequiredTasks 目前不會轉狀態 |
| Workflow 介面／DTO | [ICaseStatusWorkflowService](../EarlyInterventionCare.Api/Services/Workflow/ICaseStatusWorkflowService.cs)、[SubmissionContext](../EarlyInterventionCare.Api/DTOs/Workflow/QuestionnaireSubmissionContext.cs)、[WorkflowResult](../EarlyInterventionCare.Api/DTOs/Workflow/CaseStatusWorkflowResult.cs) 已描述共享交易、寫入錯誤傳遞、去重及 PendingIntegration | 此 HEAD 沒有實作該介面的正式 Service／Store／DI。07 未提交的 Workflow 不可見、未交付，不能假設方法／鎖已完成 |
| 人工狀態更新 | [CaseStatusService](../EarlyInterventionCare.Api/Services/Cases/CaseStatusService.cs)、[EfCaseStatusStore](../EarlyInterventionCare.Api/Services/Cases/EfCaseStatusStore.cs)：自行 Begin、鎖 Case、保存 Case＋History、Commit | 不能在 Submit 交易中直接呼叫 ChangeManuallyAsync，否則有巢狀／獨立交易問題 |

模型約束依據：[CoreModelConfiguration](../EarlyInterventionCare.Api/Data/CoreModelConfiguration.cs)、[Parent Mapping](../EarlyInterventionCare.Api/Data/ParentQuestionnaireModelConfiguration.cs)、[History Mapping](../EarlyInterventionCare.Api/Data/CaseStatusHistoryConfiguration.cs)。保留同 Case／Questionnaire／角色／輪次的 Task 唯一鍵、每 Task 單一 Response、作者／Grant＋Key 唯一鍵、Task／版本及 Grant Scope FK。不同 TaskId 但同指派語意也不能任意算作兩份必要問卷。

## 2. 共用完成度 DTO 候選

**候選、未實作**：不修改現有 HTTP DTO，以新內部結果先承載可信全輪次評估，再由 07 的受保護查閱 API 投影。沒有答案、分數、姓名、Observation、作者 ID 或 Stamp。

```csharp
// 候選；型別／方法均尚未實作，狀態值待三人核定。
public sealed record CandidateRoundCompletionDto(
    Guid CaseId, uint? EffectiveRound, string? RoundRevision,
    int? RequiredCount, int? SubmittedRequiredCount,
    int? PendingRequiredCount, int? InProgressRequiredCount,
    int? CancelledRequiredCount, int? OptionalCount,
    bool? AllRequiredSubmitted, string Decision,
    IReadOnlyList<string> PendingReasons, string Source,
    DateTime ObservedAtUtc, bool IsCommitted);
```

- 未取得權威輪次／完整集合時，EffectiveRound、計數及 AllRequiredSubmitted 保留 null，不造 0／false。明確歷史輪次查閱另用 RequestedRound，不能冒稱它是 EffectiveRound。
- Decision 候選：Complete、Incomplete、PendingIntegration；Reason 區分有效輪次未知、未封存、空輪次、零必要、取消政策待定。Complete 須有必要問卷且全部完成，還需獨立的 Case 狀態政策檢查。
- Source 候選：Unknown、LegacyCrossRoundSnapshot、TransactionalRoundSnapshot。現有 Completion API 只能標示原始跨輪次來源，不能重新命名成有效輪次結果。
- RoundRevision 若日後對外使用，採標準十進位字串，避免 JavaScript Number 精度問題；來源尚未建立時為 null。
- IsCommitted=false 表示交易內評估，不是已保存成功；只有最終 Commit 成功後才可以對外宣告提交成功。時間戳、Source 名稱與 DTO 本身都不是授權或交易證據。
- 重用兩個既有 evaluator，先補語意映射契約測試，不建立第三套計數引擎。Workflow 版把空集合與零必填合併為 NoRequiredTasks，Questionnaires 版另區分 EmptyRound，且後者提供可選／取消計數；映射時保留待決策原因，不把 nullable IsComplete 強轉 false 或 true。

## 3. 有效輪次與封存的最小候選模型

**候選、非 Schema 白名單**：保留 Task.AssignmentRound 作歷史編號，不以 MAX(AssignmentRound)、最新 UpdatedAt、Task SUBMITTED 數量猜目前輪次。不新增第二套 Task／Response 或搬移答案。

| 候選資源 | 最小欄位／約束建議 | 未決事項 |
|---|---|---|
| CaseQuestionnaireRound | (CaseId, AssignmentRound) 複合 PK，正整數輪次；State 候選 DRAFT／SEALED／ARCHIVED；Revision；SealedAtUtc／SealedByUserId、ArchivedAtUtc／ArchivedByUserId 可空 | 狀態名稱、封存／封題意義與誰能操作，均待 07／16 及院方確認 |
| CaseQuestionnaireRoundState | CaseId PK；EffectiveRound nullable；Revision；(CaseId, EffectiveRound) 指向上述輪次 | 明確指標保證每 Case 最多一個有效輪次；NULL 是未確認，不自動選最大值；Case 院所關聯須先整合 |
| Task 與輪次 | 保留原 CaseId／AssignmentRound／唯一鍵；共同核准後才評估新增複合 FK | 舊 Task 可能沒有權威輪次列，須先盤點，不自動建立 SEALED／有效輪次資料 |

SEALED 候選表示必填集合已固定；ARCHIVED 候選表示保留歷史且不可再作目前輪次，不刪 Task／Draft／Response／Hash／History。EffectiveRound 指標只可引用經政策核可的 SEALED 輪次；封存有效輪次時須同交易清除／切換指標，不能留下指向封存列。此跨列規則並非僅靠一般 FK 就能強制，尚需服務與交易設計。

既有輪次來源不明時不自動初始化為第 1 輪、SEALED 或有效。新資源 Revision 如何起始／遞增與資格 ABA 世代是否關聯需另定，不把它冒充 EligibilityStamp 的 TaskGeneration。封題後是否允許增減任務、取消、變更 IsRequired／版本與重開，均不能自行選擇。若允許，須同 Case 協調鎖更新集合版本，令舊完成度失效。

## 4. 判定表及政策分界

| 情況 | 候選完成度／Workflow 行為 | 決策責任 |
|---|---|---|
| 有效輪次未知／未封題／已封存 | PendingIntegration，不更新 CaseStatus／History | 07／16 固定權威來源；老師／院方核定操作政策 |
| 同輪次部分必要 Task PENDING／IN_PROGRESS | Incomplete；保持狀態，保存本次合法提交 | 既有純規則可重用，整合待驗收 |
| 有效且封題，至少一份必要且全部 SUBMITTED | 先驗 Task／Response 一致性；僅 WAITING_FORM 可考慮轉 FORM_COMPLETED | 07 Workflow、16 交易協調 |
| 必要 Task CANCELLED | PendingIntegration，不擅自排除分母、不當已提交 | 老師／院方確認取消後需求是否仍成立 |
| 空輪次／零份必要問卷 | 不以 0=0 判 Complete；候選 PendingIntegration | 老師／院方；現有政策不會自動轉換 |
| 可選問卷未完成／取消 | 純計數與必要集合分開，不單靠可選問卷阻擋必要完成；是否有附加醫療規則待確認 | 07／16；院方核定附加規則 |
| 舊／非有效輪次提交 | 不完成目前輪次；是否仍可寫該輪次另待政策，不能用 Workflow NoAction 代替提交授權 | 11／07／16，老師／院方 |
| 同語意重複指派 | 保留既有唯一鍵／409，不因取消而硬刪重建或忽略衝突 | 07 指派、16 約束審查 |
| SUBMITTED 無 Response、版本／時間關聯不一致 | 儲存錯誤，整筆失敗；不轉 PendingIntegration 或修復為成功 | 16 |
| 已 FORM_COMPLETED／其他非 WAITING_FORM 狀態 | 不再新增相同完成 History；不自動結案、回退或重開 | 現有純政策；輪次重開業務待核定 |

封存與資料刪除是不同操作。保存期限、撤回後答案保留、誰可看封存輪次、重新評估是否重新進 WAITING_FORM、取消必要問卷及零必要問卷語意均需老師／院方核定。不得從合成測試資料推出正式醫療規則。

## 5. 共用完成度協調介面候選

已有 ICaseStatusWorkflowService.EvaluateAndApplyAsync(QuestionnaireSubmissionContext, CancellationToken)；優先實作其共同交易契約，不另建提交引擎或另一套 History 寫入管線。

```csharp
// 候選，未實作：僅完成度查詢／計算，不提交交易或自行授權。
public interface IQuestionnaireCompletionCoordinatorCandidate
{
    Task<CandidateRoundCompletionDto> EvaluateInTransactionAsync(
        ApplicationDbContext db,
        QuestionnaireSubmissionContext submission,
        CancellationToken ct);
}
```

方法需拒絕沒有現存交易、錯誤 Context／關聯或不完整快照；驗證 Case、Task、Response、版本與輪次，查詢整個 Case／明確輪次的 PARENT＋TEACHER 必要集合，不使用單一 Grant 的 Tasks 或客戶端完成度。07 Workflow 實作可呼叫此 16 協調器，再沿現有 CaseQuestionnaireWorkflowPolicy 決定 Case／History。

介面本身不會強制同 DbContext、鎖或授權；未來實作與呼叫端必須驗證。讀取 DTO／snapshot 不取代 Guardian／Consent、教師 Grant／Session 或醫療 Scope。

必要交易能力候選：Parent／Teacher transaction 增加 SavePendingChangesAsync(ct)（只 SaveChanges、不 Commit），Teacher transaction 提供同一 DbContext 存取。這是未來需共同審查的介面變更，現有介面尚無此方法。是否改為共用 UoW 由 16／07 確認，不在本輪決定或實作。

## 6. Submit → 完成度 → Workflow 的交易時序候選

1. 以原可信 Principal 或教師 Session 開始必要預檢；未交付正式資格 Adapter 時新版家長入口仍在開 Store 前拒絕。預檢不是授權收據。
2. 單一 Submit coordinator 擁有 scoped DbContext、Begin、SaveChanges、Commit、Rollback／Dispose。先按核定資格鎖序授權，再依共同 Case／Task 鎖序取得資源，不能在已鎖 Task 後臨時反向鎖 Case。
3. 驗 Task Scope、作者遮蔽、版本、內容與 Key。成功重送走第 7 節的立即回原收據分支，不呼叫完成度／Workflow。
4. 首次提交按既有家長／教師各自契約驗證；家長比較 ExpectedRevision／Relationship／canonical v1，教師不改共用 Hash。Stage Response＋Task；教師既有 Grant USED／Session 變更一併保留在本交易。
5. 在同交易 SaveChanges（不 Commit），使資料庫重新查詢看見本次 Response＋SUBMITTED Task。這個階段現有 CommitAsync 無法分離，故正式接入 BLOCKED；不得先 Commit 再補 Workflow。
6. 重新讀取鎖保護下的 Case、權威有效輪次／封題、整輪 Task／Response 一致性，使用 evaluator。自己先前的 Save 在同交易可見，不代表其他資格／Task 查詢沒有舊快照問題；必須核定隔離與 current-read 策略。
7. 07 Workflow：政策未定時回 PendingIntegration，不寫 Case／History；條件核可且 Case 仍 WAITING_FORM 才 Stage FORM_COMPLETED 與一筆 History。TransitionApplied 僅表示 staged，不是已 Commit。自動來源使用既有 QUESTIONNAIRE_COMPLETION；ChangedByUserId 的系統／實際使用者歸屬由 07／16 確認，不偽造醫護作者。
8. 同交易最後重新授權與比對資格；再 SaveChanges 保存 Case＋History，最後一次 Commit 全部提交，才回成功收據。若最後 Save／Commit 前仍會取得新鎖，須納入共同鎖序；資格鎖須持有到交易結束。
9. 任何授權失效、資料庫／約束／並發錯誤、取消或 History 保存失敗：整筆回滾，清除未提交追蹤，不回成功。不得 catch 為 NoAction／PendingIntegration。Commit 結果未知時不得宣稱已回滾，保留安全證據、以原 Key 在授權後查證，不自動換 Key 重提。

預期的政策 PendingIntegration 是「不做 Case 狀態副作用」，不是把真正儲存錯誤降級。若必要依賴／交易能力缺失，正式整合保持關閉；不能以假的成功 Workflow 服務代替。人工 CaseStatusService 自行擁有交易，不可在此直接呼叫；Workflow 應以同 Context stage History。

現有教師 Submit 的 audit 是 Commit 後 best-effort；它不是交易 History，也不能當作成功去重依據。日後若需可靠通知／外部事件，另審查 transactional outbox，不在提交中先送外部副作用。

## 7. 成功重送與 History 去重

- 家長既有成功重送先驗同作者、Task、Key、授權、HashVersion、canonical 內容／Relationship，再回原 ResponseId／SubmittedAtUtc；不比較新 Draft Revision、不產生另一筆 Response。
- 教師既有同 Grant／Key／Task／Hash 重送保留原教師 Hash 與收據窗口，不額外執行 Grant 關閉／Session 修改或 Workflow；不能因整合需求改成家長 HashVersion 規則。
- 未來 hook 只放「首次提交」分支，且在最終 Commit 前；重送分支即使需要授權重驗及結束讀取交易，也不做完成度／History 副作用。歷史 GetReceipt 更不觸發 Workflow。
- 不因重送時規則已固定就偷偷補做上次 PendingIntegration 的 Workflow。若需追補，另設有授權、去重與操作證據的顯式重算流程，待核准。
- Case 行鎖內重新讀狀態，只有實際 WAITING_FORM → FORM_COMPLETED 寫一筆 History，可防並行最後兩份提交重複轉換；成立前提是所有相關寫入端都遵守 Case 協調鎖。
- 現有 History 沒有 Submission／輪次事件唯一鍵。初版可否只靠 Case 鎖／狀態條件足夠，或需 (CaseId, Round, WorkflowEventType) 去重資源，必須由 07／16 依「重開／多輪」政策決定。不可把 CorrelationId 當去重鍵，也不先新增唯一鍵。

## 8. 局部鎖序與資格協定的協調

候選最低局部偏序為：**Case 協調資源先於該 Case 的 Task；多個 Task 依固定 TaskId 排序；最後才按核定順序處理 Draft／Response／History。** 有效輪次指標、指派、取消、封題／封存、手動狀態寫入、兩種 Submit 都必須參與同一 Case 協調。

目前家長是 Adapter → Task；教師是 Grant → 排序 Task；人工狀態是 Case。若直接在教師或家長已持 Task 鎖後再呼叫 Workflow 鎖 Case，可能與 Case → Task 的寫入端形成反向等待。必須調整整個開交易／取鎖邊界，不能只在 Submit 尾端加一個 Case 查詢。

11 的文件協調資源、院所、User、Case、Guardian／綁定、Consent／Invitation，與教師 Grant／Session、輪次列的相對順序尚未全域核定。院所協調鎖是互斥資源，不等於全院資格 Epoch；單一撤綁不得讓同院無關家長預檢全部失效。

允許以未加鎖查詢定位資源作 hint，但必須在取得核定鎖後重新確認所有關聯，hint 不授權。Case 協調列／輪次不存在時不能略過鎖；缺列建立、唯一鍵競態、文件換版跨院所鎖排序與 MySQL 隔離／範圍鎖策略均待共同方案，不在文件中假裝已有 SQL。

在核准隔離 MySQL 驗證前，不宣稱已防死鎖或 ABA。死鎖後重試必須整筆重新授權，不能沿用舊 Stamp；是否及如何自動重試另行固定，前端資格變更 409 不自動重送。

## 9. 醫療端讀取／指派 Permission 與 Scope

現有依據：[QuestionnairesController](../EarlyInterventionCare.Api/Controllers/QuestionnairesController.cs) 仍僅 ADMIN／CASE_MANAGER，加 Development 虛構 Case gate；不因 MEDICAL_STAFF 需要讀取就移除限制。[ScopedReadAuthorization](../EarlyInterventionCare.Api/Services/Authorization/ScopedReadAuthorization.cs) 提供 case.read／questionnaire.task.read 加 Scope，預設 Deny；宣告／註冊不等於此 Controller 已接入。[CaseStatus Authorizer](../EarlyInterventionCare.Api/Services/Cases/ICaseStatusAccessAuthorizer.cs) 亦有預設拒絕。

| 操作 | 所需可信資訊／候選位置 |
|---|---|
| Case／完成度讀取 | 原 Principal、Active User／院所、權限、正式 CaseAccess 指派／照護關係；Controller／入口先驗 Scope，查詢再套 Case／院所界線 |
| Task 讀取 | questionnaire.task.read 加 Case Scope、Task 實際屬於該 Case／院所；不回答案等敏感欄位 |
| Task 指派／取消／封題封存 | 獨立寫入 Permission（名稱待 07／16）、Case 寫入 Scope、輪次政策與狀態、原作者／操作證據；不得以 read 權限代替 |
| 自動 Workflow | 已授權 Submit、持久 Case／輪次、同交易與伺服器政策；不冒用醫護 Permission 或把家長升權為醫療角色 |

不開放同院全部 Case。此 HEAD 缺 Case 院所模型及正式醫療 Scope，須與 11／07 固定交付統整。完成度不能取代 Guardian／Consent 或 Grant 授權；家長可見教師完成狀態不代表可見教師答案／草稿／分數。

## 10. 分工、依賴與最小後續順序

以下為建議分工，尚需三人確認：

| 負責者 | 交付及邊界 |
|---|---|
| 11 | 正式 Context／Guardian／Consent／作者 wrapper、Stamp 與資格寫入端；明確加入撤綁／重綁與文件政策。不負責第二套 Response 引擎 |
| 16 | 共用完成度來源契約、evaluator 映射測試、Submit save-only／同交易協調候選、Schema／Migration 統整與隔離驗收；不代替 07 決定醫療流程 |
| 07 | 固定提交 Workflow Service／Store／醫療 Scope、輪次與指派需求、CaseStatus／History 的 stage-only 實作；不自行 Commit Submit 的交易 |
| 三人 | 鎖偏序與所有寫入端、DTO／錯誤、有效輪次權威、去重、固定 Commit、跨模組回歸；涉及醫療規則送老師／院方 |

1. **可獨立準備，另批核准後實作**：兩套 evaluator 結果映射／空輪次差異契約測試；完成度 DTO 的 nullable／來源／staged 語意測試；交易時序與失敗注入案例。沿既有元件，不重做核心或偽造 roundConfirmed=true。
2. **待 07／11 介面確認**：定義同 Context、save-only、授權重驗與 Workflow hook；列出所有需 Case → Task 協調的寫入端。未提交 Workflow 先列 BLOCKED。
3. **待共同模型與政策**：核定輪次／封題／封存模型、取消／零必要、歷史輪次寫入、指派 Permission、History 去重與作者歸屬；未核定項目不進 Schema 白名單。
4. **固定版本整合後**：16 修改交易能力，07 實作 stage-only Workflow，11 實作正式資格協定；先離線驗證成功重送不呼叫 Workflow、錯誤必回滾、未定政策無 Case 副作用。Fake 僅證明純邏輯。
5. **另行核准**：共同增量 Migration／Snapshot、隔離 MySQL 寫入／並行／回復驗收，之後才審查 DI／Controller 接線。不得使用 MySQL80／3306。

## 11. 未來驗收與回復檢核（均未執行）

| 案例 | 候選預期 |
|---|---|
| 家長／教師同輪次各完成一部分 | 只有目前有效輪次的完整必要集合影響判定；單一 Grant 完成不代表 Case 完成 |
| 最後一份必要問卷完成 | 同交易保存 Response／Task／教師相關狀態／Case＋一筆 History；Commit 後才回成功 |
| 兩個不同 Task 並行最後提交 | 按 Case 協調序列化，後者讀到前者提交；最多一次狀態轉換，不因雙方看到舊快照而漏轉 |
| 相同 Key／內容成功重送、歷史收據 | 授權後回原收據，Workflow 呼叫次數不增加，History 不增加 |
| 同 Key 不同內容／未知家長 HashVersion | 按既有家長／教師各自契約拒絕，無 Workflow 副作用 |
| 取消必要、空輪次、未知有效輪次／未封題 | PendingIntegration，不自動變更 Case／History，不將其當成儲存失敗的替代回傳 |
| 封存／取消／指派與 Submit 並行 | 同協調資源下取得一致集合／政策，未核定前保持 BLOCKED |
| Guardian／Consent 撤銷與 Submit 並行 | 資格失效安全拒絕；完成度不能挽救授權，Response／Task／Case／History 整筆不提交 |
| 第二次 Save 或 History FK／唯一鍵失敗 | 回滾第一次 save-only 所寫 Response／Task，保留原資料與 Hash bytes，不回成功 |
| 人工狀態變更競態／死鎖／Commit 結果未知 | 保留真實錯誤與追蹤資訊，依核准復原流程處理；不強制清庫、換 Key 或臆測成功 |

任何未核准輪次、資格來源、Scope 或鎖策略都不得為展示而設預設放行。既有 [Schema 審查清單](v53-common-schema-migration-review.md) 繼續有效；五份歷史 Migration 全保留、不重排／覆寫：

- 20261005152359_InitialSharedCore
- 20261005180101_AddAuditLogs
- 20261005184434_AddSubmissionReceiptWindow
- 20261006074424_IntegrateIdentityAndAuthorization
- 20261019131342_AddCaseStatusHistories

套用前須核定固定版本、增量白名單、合成資料、備份與獨立還原庫；舊作者／Relationship／HashVersion 不推定、不回填。回復優先回滾尚未提交交易；已提交後的版本回退須先審查 Schema 相容性，禁止盲目 Down 刪除答案或既有 History。

本輪未重跑任何 Harness。既有 [Submit 測試紀錄](../tests/ParentSubmitReadiness/README.md) 與 [Evidence 契約紀錄](../tests/ParentEligibilityEvidence/README.md) 說明 Fake／純契約與真正 MySQL 的界線；教師 `no pending schema/model changes`、舊家長 `only six additive author schema operations proposed` 的既有 Schema gate 失敗不能視為已修復，也不得以本文件或純規則測試取代。
