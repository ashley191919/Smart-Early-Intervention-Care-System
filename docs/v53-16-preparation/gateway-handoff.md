# 16-01／16-03／16-04：固定 Gateway 契約與交接

唯一程式依據 `bb74e72c1ed00f601646ba669c8ea73952359e98`。本目錄所在主 Worktree 不含全部核心；以下路徑均指該 Commit，不是承諾目前分支可直接建置。07／11最新交接SHA未提供／未核定，不推定已整合。

## 方法與 DTO

正式核心類別：`EarlyInterventionCare.Api.Services.Questionnaires.ParentQuestionnaireGateway`。五個方法均接可信 `ClaimsPrincipal principal, Guid taskId`，最後為可選 `CancellationToken ct=default`，回傳 `Task<T>`。

| 方法 | 額外輸入 | T |
|---|---|---|
| GetDraftAsync | 無 | QuestionnaireDraftResponse |
| SaveDraftAsync | SaveQuestionnaireDraftRequest input | QuestionnaireDraftResponse |
| SubmitAsync | Guid key, QuestionnaireSubmission input | ParentQuestionnaireReceipt |
| GetReceiptAsync | 無 | ParentQuestionnaireReceipt |
| GetStatusAsync | 無 | ParentTaskStatus |

核心存在不等於正式 HTTP API 已註冊。沒有列出已核定的專用路由或成功HTTP status；由11確認，不冒用Development Controller。

| DTO／來源檔 | 欄位（C#型別；JSON依既有camelCase設定） |
|---|---|
| QuestionnaireAnswer／DTOs/Questionnaires/QuestionnaireWorkflowContracts.cs | string? QuestionId, OptionValue |
| QuestionnaireSubmission／同上 | Guid QuestionnaireVersionId；string? RespondentName, FilledOn；QuestionnaireAnswer?[]? Answers；string? Observation |
| SaveQuestionnaireDraftRequest／同上 | 同提交欄位，另Required ulong? ExpectedRevision |
| QuestionnaireDraftResponse／同上 | Guid TaskId, QuestionnaireVersionId；ulong Revision；string? RespondentName, FilledOn；IReadOnlyList<QuestionnaireAnswer> Answers；string? Observation；DateTime? UpdatedAtUtc；string TaskStatus |
| ParentQuestionnaireReceipt／Services/Questionnaires/ParentQuestionnaireContracts.cs | Guid ResponseId, TaskId, QuestionnaireVersionId；DateTime SubmittedAtUtc；string TaskStatus；bool Replayed |
| ParentTaskStatus／同上 | Guid TaskId, QuestionnaireVersionId；string TaskStatus；DateTime? SubmittedAtUtc |
| ParentTaskAccess（內部證據）／同上 | Guid UserId, OrganizationId, CaseId, TaskId, GuardianId, ConsentRecordId；不可接受HTTP body提供 |

FilledOn文字格式為`yyyy-MM-dd`日期；後端依Asia/Taipei判定日期，儲存時間UTC。Revision為ulong，前端序列化不得失去大整數精度。無authorId輸入。家長receipt的TaskStatus不能錯用教師QuestionnaireReceipt的GrantStatus；兩者都沒有答案／作者／hash／key／Observation。

## Revision、去重、原子性

- 無Draft回revision0；第一次保存expectedRevision0，每次成功＋1；不符／overflow／EF concurrency回409，不自動覆蓋。Draft可缺題，提交必須完整payload，不能默默提交舊草稿。
- Controller解析非空UUID `Idempotency-Key`，核心以作者＋key查詢；相同Task／正規化payload回原responseId且Replayed=true，改Task／內容回409。不同key不能在同Task建立第二Response。原作者授權撤回後，replay／receipt也不可繞scope。
- 同交易：Adapter授權／鎖定→Task FOR UPDATE→作者／版本／狀態／一致性→Draft或Response＋Task更新→Adapter Revalidate→SaveChanges→Commit。成功commit才回傳。Response與Task的SUBMITTED／時間保持一致；不一致503且不自修。保留Draft不代表可在提交後閱讀／編輯它。
- Store要求乾淨、獨占scoped DbContext；Adapter不可另開context、替換／提交transaction；Dispose未commit回滾且清tracker。失敗重試需新scope／transaction。commit回覆未知採原key重讀，不自動換key。
- 不包含CaseStatus／History原子更新，不選有效輪次，不承諾稽核零遺失。若07需加入須先固定交易擁有者，不能另交易補寫後聲稱一起commit。

## 核心錯誤碼

| HTTP | code | 邊界 |
|---|---|---|
| 401 | AUTHENTICATION_REQUIRED | 非唯一可信已驗identity／userId |
| 400 | INVALID_INPUT、INVALID_ANSWERS | 空key、缺revision、非法／缺答案等 |
| 404 | TASK_NOT_FOUND | 不存在、取消、範圍／角色／作者不符；不洩漏存在性 |
| 404 | RESPONSE_NOT_FOUND | 本人尚無提交收據 |
| 409 | TASK_UNAVAILABLE | 未支援Task狀態；原交接錯誤表未列但Scope實作存在 |
| 409 | VERSION_UNAVAILABLE、VERSION_MISMATCH | 固定版本、角色或版本狀態不符／輸入版本不符 |
| 409 | DRAFT_CONFLICT | revision／overflow／EF concurrency |
| 409 | TASK_SUBMITTED | 不可重新寫Draft或提交 |
| 409 | IDEMPOTENCY_CONFLICT、WRITE_CONFLICT | key內容不符／EF唯一鍵1062競爭 |
| 503 | PARENT_ACCESS_NOT_INTEGRATED、DEFINITION_UNAVAILABLE、STORAGE_INCONSISTENT | 預設拒絕Adapter／壞snapshot／持久化不一致 |

DB故障與取消向上傳遞；11需安全error boundary，DB故障回安全503，不傳exception／SQL／連線字串；取消不偽裝成功。Permission查詢故障為AUTHORIZATION_UNAVAILABLE／503。框架binding、CSRF、429及錯誤envelope／traceId需11確認；現有QuestionnaireErrors只處理另一種operation exception，不能當成Parent workflow完整錯誤處理。

## 11 接入清單

1. 固定完整SHA與模型／Guardian／Consent適用用途、版本、撤回效果及多家長／重綁政策；提供真正IParentTaskAccessAdapter。缺少時DenyParentTaskAccessAdapter維持503。
2. LockAndAuthorizeAsync與RevalidateAsync都使用transaction.DbContext及CurrentTransaction；查ACTIVE院所／帳號、PARENT角色、有效綁定／適用同意及同Case Task。回傳所有非空ParentTaskAccess ID，UserId必須等於唯一可信principal userId。
3. 鎖順序候選：Organization→User→Case→Guardian/link→Consent→Task，多列按ID；撤銷與改綁writer須一致。最小Task查詢僅定位scope，最後須鎖下重查，不能靠HTTP先查一次。
4. 專用Controller負責認證scheme、CSRF／可信來源、大小限制、UUID key解析、模型binding、安全錯誤與no-store；不得從body取作者。草稿衝突保留使用者輸入後重讀，不能盲重試覆蓋。
5. 核定PARENT版本由伺服器ParentQuestionnaireOptions固定；整合審查、共同Migration及驗收後才註冊AddParentQuestionnaireCore並覆寫真Adapter。現Program未註冊，不在本批代接。
6. 家長查教師完成狀態必須專用最小DTO，不能回Entity再由前端隱藏。本人逐題答案專用讀取不在這五個Gateway方法中，另案確認。

## 07 接入清單

- `AddScopedCaseTaskReads()`＋真`ICaseTaskReadScope`，以`ScopedReadAuthorization.CanReadCaseAsync/CanReadTaskAsync`要求Permission及scope同時成立；沿用既有Handler與安全middleware，不覆寫同名Policy為只需登入。
- ParentTaskStatus是家長核心狀態DTO，不等於所有角色通用Task授權。醫療端沿用`CaseTaskResponse`（Task／Case／Questionnaire／Version IDs、title、versionNumber、role、round、required、status及UTC時間）前須核對scope；不得增加答案資料。
- 舊`CaseTasksCompletionResponse`包含CaseId與total／required／submitted／pending／inProgress計數及AllRequiredSubmitted，為跨輪次摘要，不能當作有效當輪結案依據。指定輪次重用QuestionnaireRoundCompletionEvaluator；有效輪次、取消必要／零必要任務及CaseStatus由07固定。
- Task指派DTO沿用QuestionnaireVersionId、RespondentRole、AssignmentRound、IsRequired；不重做核心。醫護Controller接入、Permission指派、正式scope與History共交易另待確認。

## 已有測試成果（歷史紀錄，這批未重跑）

來源為固定Commit的`docs/v53-16-verification.md`最新補強表；不是本批新的PASS，也不能相加當成獨立E2E案例數。

| Harness | 記錄結果 | 可證明／限制 |
|---|---|---|
| ParentQuestionnaire | 126 PASS | Gateway／Validator＋transactional Fake Store／Access；非真Guardian／Consent／鎖 |
| EfParentStore | 86 PASS | 正式EF Store／Gateway、Pomelo SQL轉譯、ADO合成rows／transaction；無socket、非MySQL engine |
| ScopedReadSafety | 44 PASS（38 DI/unit＋6 loopback HTTP） | Policy覆寫防護、Permission＋scope；非正式Host／DB |
| Permission／RBAC Offline／Policy／HTTP | 34／22／43／19 PASS | Handler／DI／ADO或合成HTTP，非正式三端整合 |
| Teacher Phase22 proposal | 158 PASS | 教師回歸，精確六項作者模型差異不等於部署核准 |
| Phase21／RoundCompletion | PASS exit0／37 PASS | 指派摘要離線／純計算，不決定有效輪次 |
| CoreDatabase／History offline | exit1／MODEL_SNAPSHOT拒絕 | 預期gate，不能算全綠 |

EF／ADO可驗WHERE／參數、revision predicate、1062及零affected rows映射、同transaction與Dispose／tracker；不能驗FK、唯一鍵真正執行、耐久、隔離、死鎖、撤銷競爭、重啟或備份還原。六項作者增量（兩欄、兩Restrict FK、一普通索引、一作者＋key唯一索引）仍待使用者統整Migration。既有Level1 health紀錄databaseChecked=false，不是業務DB驗收；API退出碼未取得不記為0。
