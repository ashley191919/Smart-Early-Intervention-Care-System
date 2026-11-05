# V5.3 家長作者 Entity／Mapping／DbContext 最小聯集審查

## 1. 基準、狀態與證據

16：`codex/v53-parent-core`／`159c27f7790a77eca1ab2d746aac9fd7202b6688`；開始時工作區乾淨。11：固定 `b5573b5311641d9b108930cbdf5e461dbc1bcea0`，透過 GitHub 唯讀讀取程式與目錄，沒有 Fetch。

**本文件是候選聯集白名單，需 11／16 共同審查；不是修改模型、Migration、DI 或資料庫的授權。** 已存在指固定版本原始碼存在，不等於已進 Snapshot、實際建表或正式驗收。候選指待核准的未來整合；未完成包括來源聯集、增量 Migration、正式 Reader 替換、資格協定與隔離 MySQL。

主要來源（11 連結均固定上述 SHA）：

| 範圍 | 來源／核對證據 |
|---|---|
| 共用核心 | [11 CoreModelConfiguration](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/CoreModelConfiguration.cs) 與 [16 Core](../EarlyInterventionCare.Api/Data/CoreModelConfiguration.cs) blob 均 `77a1d891d3e851c6c103dabae357dc399de75803` |
| 作者擴充 | [16 Parent Mapping](../EarlyInterventionCare.Api/Data/ParentQuestionnaireModelConfiguration.cs)，blob `0c5e87cc82f96189165d46220cb69568cb009eb5`；11 Data 目錄無此檔 |
| Draft／Response | [11 Draft](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/Entities/QuestionnaireDraft.cs)、[11 Response](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/Entities/QuestionnaireResponse.cs)；[16 Draft](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireDraft.cs)、[16 Response](../EarlyInterventionCare.Api/Data/Entities/QuestionnaireResponse.cs) |
| 11 Case／綁定 | [CaseRecord](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/Entities/CaseRecord.cs)、[GuardianContact.cs 三個 Entity](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/Entities/GuardianContact.cs)、[Guardian Mapping](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/GuardianModelConfiguration.cs)，Mapping blob `79f2a2b769e4d59285d4258b2939bee7df8e6a1d` |
| Consent | [11 ConsentModels](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/ParentModule/ConsentModels.cs)，blob `7cf821fe6d6c0b9ffb5c8e64c91224ca6b4eb8bb` |
| DbContext | [11](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/Data/ApplicationDbContext.cs)，blob `7670ffa054e9117cd29045555023d6fa8a505a00`；[16](../EarlyInterventionCare.Api/Data/ApplicationDbContext.cs) |
| History | [16 Entity](../EarlyInterventionCare.Api/Data/Entities/CaseStatusHistory.cs)、[Mapping](../EarlyInterventionCare.Api/Data/CaseStatusHistoryConfiguration.cs)；11 固定 Entities／Data 目錄無此 Entity／Configuration |
| Reader／wrapper | [16 契約](../EarlyInterventionCare.Api/Services/Questionnaires/ParentAuthorMetadataContracts.cs)、[EF Reader](../EarlyInterventionCare.Api/Services/Questionnaires/EfParentAuthorMetadataReader.cs)、[11 wrapper](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/ParentModule/ParentAuthorVisibility.cs)、[11 註冊](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/b5573b5311641d9b108930cbdf5e461dbc1bcea0/EarlyInterventionCare.Api/ParentModule/ParentModuleRegistration.cs) |

既有 [共同 Schema 審查](v53-common-schema-migration-review.md)、[Workflow 候選文件](v53-questionnaire-workflow-integration-proposal.md) 繼續有效；本文件補充 b5573b5 的最新作者聯集，不把先前參考版本當成最新交付。

## 2. 逐欄白名單：Draft／Response

下文 N=允許 NULL，NN=不允許 NULL；UUID 表示 `char(36)`／ascii／ascii_bin。來源「共」=11、16 均有且共用 Core Mapping 相同；「16」=僅 16 現有模型，候選納入聯集。候選必須保留表內原始資料；無自動回填、修復、作者推定或 default 值。

### questionnaire_drafts

| CLR 欄位 | CLR 型別／NULL | DB 欄名／型別 | 來源與必要性 |
|---|---|---|---|
| TaskId | Guid／NN | task_id／UUID | 共；PK，範圍查詢鍵 |
| QuestionnaireVersionId | Guid／NN | questionnaire_version_id／UUID | 共；Task／版本 FK |
| RespondentName | string?／N | respondent_name／varchar(100) | 共；既有草稿內容，不供 metadata Reader 查詢 |
| RespondentRelationship | string?／N | respondent_relationship／varchar(32) | 16；Submit 後續關係保存，不是 Reader 必要欄位 |
| FilledOn | DateOnly?／N | filled_on／date | 共 |
| AnswersJson | string／NN | answers_json／json | 共；不得被 metadata Reader 載入 |
| Observation | string?／N | observation／text | 共；不得被 metadata Reader 載入 |
| Revision | ulong／NN | revision／bigint unsigned | 共；concurrency token，保留既有比對 |
| LastSavedByGrantId | Guid?／N | last_saved_by_grant_id／UUID | 共；教師來源，Reader 只查 IS NOT NULL |
| ParentAuthorUserId | Guid?／N | parent_author_user_id／UUID | 16；**Reader 最小必要擴充**，不得推定舊作者 |
| UpdatedAtUtc | DateTime／NN | updated_at_utc／datetime(6) | 共；不是資格世代 |

### questionnaire_responses

| CLR 欄位 | CLR 型別／NULL | DB 欄名／型別 | 來源與必要性 |
|---|---|---|---|
| ResponseId | Guid／NN | response_id／UUID | 共；PK |
| TaskId | Guid／NN | task_id／UUID | 共；唯一、Task 範圍 |
| QuestionnaireVersionId | Guid／NN | questionnaire_version_id／UUID | 共；Task／版本 FK |
| RespondentName | string／NN | respondent_name／varchar(100) | 共；不供 metadata Reader 查詢 |
| RespondentRelationship | string?／N | respondent_relationship／varchar(32) | 16；新版家長 Submit 必填檢查另由核心負責，舊／教師列仍 N |
| FilledOn | DateOnly／NN | filled_on／date | 共 |
| AnswersJson | string／NN | answers_json／json | 共；不供 metadata Reader 查詢 |
| Observation | string?／N | observation／text | 共；不供 metadata Reader 查詢 |
| SubmittedByGrantId | Guid?／N | submitted_by_grant_id／UUID | 共；教師來源，Reader 只查 IS NOT NULL |
| ParentAuthorUserId | Guid?／N | parent_author_user_id／UUID | 16；**Reader 最小必要擴充** |
| IdempotencyKey | Guid／NN | idempotency_key／UUID | 共；去重語意不變 |
| PayloadHash | byte[]／NN | payload_hash／binary(32) | 共；不重算舊 bytes |
| HashVersion | string?／N | hash_version／varchar(64) | 16；新版家長重送版本判定，不是 Reader 必要欄位 |
| SubmittedAtUtc | DateTime／NN | submitted_at_utc／datetime(6) | 共 |

**候選分批界線**：Reader-only 最少是兩個 nullable 作者欄位及其模型；完整沿用 16 Parent Mapping 則還必須同時具備兩個 Relationship 與 Response HashVersion。禁止只複製完整 Mapping 卻漏 Entity 屬性，或把 Reader 可以查作者誤當新版 Submit 可啟用。分批方式需 11／16 核定，尚未實作。

## 3. 作者／教師約束聯集及衝突檢查

| 約束 | 現況／候選保留 | 風險與審查要求 |
|---|---|---|
| Draft PK | task_id | 同 Task 一份草稿；不能新建家長專用第二套草稿表 |
| Response 唯一 | uq_responses_task(task_id) | 家長、教師不能各自為同 Task 插入答案；不移除以解決衝突 |
| Task／版本 FK | fk_drafts_task_version、fk_responses_task_version → case_questionnaires(task_id, questionnaire_version_id)，Restrict | 保留複合範圍，不降為只比 TaskId |
| 教師 Scope FK | fk_drafts_grant_scope(last_saved_by_grant_id, task_id)、fk_responses_grant_scope(submitted_by_grant_id, task_id) → teacher_grant_tasks(grant_id, task_id)，Restrict | 保留現有對應關係／索引；Grant NULL 不表示家長已授權 |
| 教師重送唯一鍵 | uq_responses_grant_key(submitted_by_grant_id, idempotency_key) | 不改教師 Hash 或去重；與家長鍵並存 |
| 家長作者 FK | 16 的 fk_drafts_parent_author／fk_responses_parent_author，對 users.user_id，Restrict | 11 聯集需保留 User 模型；NULL 舊列不修補，非 NULL 孤兒需先盤點 |
| 家長作者索引 | ix_drafts_parent_author(parent_author_user_id) | 新 Reader 按 Task PK 查詢，此索引不是 Reader 查詢必要條件，但完整模型聯集保留 |
| 家長重送唯一鍵 | uq_responses_parent_key(parent_author_user_id, idempotency_key) | 不加 Task／HashVersion 來放寬同作者同 Key；舊非 NULL 重複先停止、不可刪列去重 |

家長／教師作者欄位同列均非 NULL，DB 的兩套 FK 可能都成立，但應用程式仍必須拒絕混合來源；本輪不自行新增 XOR Check Constraint。舊列兩作者皆 NULL 是未知，不代表任意家長可接管。MySQL nullable 唯一鍵不能替代作者驗證；NULL 舊／教師資料仍保留，不變更唯一鍵語意。

EF 可能為 FK 產生支援索引，實際名稱／是否可由既有複合索引覆蓋需在合併模型核對。不得在沒有 EF differ 審查前宣稱實際 DDL 操作數固定。Relationship 無新 FK／索引；HashVersion 不放入唯一鍵，ordinal 比對由核心處理，不依預設 DB collation。

## 4. 11 Case／Guardian／Consent 保留白名單

以下均為 **11 已提交模型、16 尚缺的聯集來源**，不是已准許建表。GuardianMapping 將欄位轉 snake_case，UUID 明訂 ascii_bin、時間明訂 datetime(6)；標「慣例」的 SQL 型別只依 CLR／HasMaxLength 推定，須合併 Provider 模型核實，不當作已生成 DDL。uint 慣例 int unsigned、bool 慣例 tinyint(1)。SQL default 與 CLR 初值不同，不能利用初值修補舊列。

### cases：共用原欄位＋11 擴充

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 來源／必要性 |
|---|---|---|---|
| CaseId | Guid／NN | case_id／UUID | 共；PK，所有關聯保留 |
| CaseCode | string／NN | case_code／varchar(64) | 共；uq_cases_code |
| ChildName | string／NN | child_name／varchar(100) | 共 |
| BirthDate | DateOnly／NN | birth_date／date | 共 |
| Sex | string／NN | sex／ENUM(MALE,FEMALE,UNKNOWN) | 共 |
| CaseStatus | string／NN | case_status／既有六狀態 ENUM | 共；保留原 NEW default；不自動改現存值 |
| CreatedAtUtc／UpdatedAtUtc | DateTime／NN | created_at_utc／updated_at_utc，datetime(6) | 共 |
| OrganizationId | Guid?／N | organization_id／UUID | 11；正式院所 Scope 必要，Restrict FK → organizations，非唯一索引 |
| Revision | uint／NN | revision／慣例 int unsigned | 11；concurrency token，不當 BindingGeneration |
| NationalId | string／NN | national_id／varchar(10) | 11；個案業務欄位，舊值處理及必要性待 11／院方確認 |
| Address | string／NN | address／varchar(300) | 11；同上，不因空字串 CLR 初值自動補值 |

CaseOwnershipConfiguration 在 GuardianModelConfiguration.cs 內擴充同一 Case，不需要複製另一份 CaseRecordConfiguration。舊 Case 院所未知時不得選目前院所代填；NationalId／Address 的 NN 增量也須另審查舊資料方案。

### guardians：GuardianContact

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 必要性 |
|---|---|---|---|
| GuardianId | Guid／NN | guardian_id／UUID | PK |
| OrganizationId | Guid／NN | organization_id／UUID | Restrict FK → organizations |
| ParentUserId | Guid?／N | parent_user_id／UUID | Restrict FK → users；未綁定保留 NULL |
| DisplayName | string／NN | display_name／慣例 varchar(100) | 聯絡資訊，不是填表作者 |
| Email | string／NN | email／慣例 varchar(254) | 不推定已驗證 |
| Phone | string／NN | phone／慣例 varchar(20) | 保留院所＋電話唯一語意 |
| Revision | uint／NN | revision／慣例 int unsigned | concurrency token，不是綁定資格世代 |
| CreatedAtUtc／UpdatedAtUtc | DateTime／NN | created_at_utc／updated_at_utc，datetime(6) | 時間欄位 |

保留 uq_guardians_organization_phone(organization_id, phone)、(organization_id, parent_user_id) 非唯一索引；不得自行改 Email 唯一或共用電話政策。

### case_guardians：CaseGuardian

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 必要性 |
|---|---|---|---|
| CaseId／GuardianId | Guid／NN | case_id／guardian_id，UUID | 複合 PK；各 Restrict FK → Case／Guardian |
| Relationship | string／NN | relationship／慣例 varchar(32) | 業務關係，不補舊 RespondentRelationship |
| IsPrimaryContact | bool／NN | is_primary_contact／慣例 tinyint(1) | 主要聯絡人標記 |
| LinkStatus | string／NN | link_status／慣例 varchar(20) | CLR ACTIVE 不等於 DB default／資格版本 |
| CreatedAtUtc | DateTime／NN | created_at_utc／datetime(6) | 時間 |
| BoundAtUtc／RevokedAtUtc | DateTime?／N | bound_at_utc／revoked_at_utc，datetime(6) | 綁定／撤銷條件，不解決 ABA |
| ActiveCaseId | Guid?／N | active_case_id／UUID，stored computed | 非手填，計算式見下 |

計算式來源為 `CASE WHEN link_status = 'ACTIVE' AND is_primary_contact = 1 THEN case_id ELSE NULL END`；保留 uq_case_guardians_active_case(active_case_id)。只限制有效主要聯絡人，不限制每 Case 只能一位照顧者，亦不取代 BindingGeneration。計算欄位／索引相容性待隔離 MySQL。

### parent_binding_invitations：ParentBindingInvitation

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 必要性 |
|---|---|---|---|
| InvitationId | Guid／NN | invitation_id／UUID | PK |
| CaseId／GuardianId | Guid／NN | case_id／guardian_id，UUID | 複合 Restrict FK → case_guardians |
| CodeHash | string／NN | code_hash／慣例 varchar(64) | 唯一索引，未明訂名稱 |
| TargetPhone | string／NN | target_phone／慣例 varchar(20) | 邀請資訊，不作作者 |
| TargetEmailNormalized | string?／N | target_email_normalized／慣例 varchar(254)，utf8mb4_bin | 舊邀請保留未知，不補 Email |
| IssuedByUserId | Guid／NN | issued_by_user_id／UUID | Restrict FK → users |
| CreatedAtUtc | DateTime／NN | created_at_utc／datetime(6) | 時間 |
| ExpiresAtUtc／ConsumedAtUtc／RevokedAtUtc | DateTime?／N | expires_at_utc／consumed_at_utc／revoked_at_utc，datetime(6) | 不改期限／撤銷政策 |
| ConsumedByUserId | Guid?／N | consumed_by_user_id／UUID | Restrict FK → users |

### consent_document_versions：ConsentDocumentVersion

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 必要性 |
|---|---|---|---|
| DocumentVersionId | Guid／NN | document_version_id／UUID | PK |
| DocumentCode／Version | string／NN | document_code／version，慣例 varchar(64)／varchar(32) | 複合唯一 |
| Title | string／NN | title／慣例 varchar(200) | 不以合成內容代正式本文 |
| Content | string／NN | content／longtext | 同意文件本文，尚無院方核准 seed |
| ContentHash | string／NN | content_hash／慣例 varchar(64) | 後端內容 Hash，不是 Submit Hash |
| ScopeCode | string／NN | scope_code／慣例 varchar(32) | UNCONFIRMED CLR 初值不可啟用 |
| ApprovalStatus | string／NN | approval_status／慣例 varchar(16) | DRAFT 不是核准 |
| ApprovalReference | string／NN | approval_reference／慣例 varchar(200) | 核准證據欄位 |
| ApprovedAtUtc／EffectiveFromUtc／RetiredAtUtc | DateTime?／N | approved_at_utc／effective_from_utc／retired_at_utc，datetime(6) | 必須由正式政策產生 |

### parent_consents：ParentConsentRecord

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 必要性 |
|---|---|---|---|
| ConsentId | Guid／NN | consent_id／UUID | PK |
| DocumentVersionId | Guid／NN | document_version_id／UUID | Restrict FK → 文件 |
| ActorUserId | Guid／NN | actor_user_id／UUID | Restrict FK → users |
| OrganizationId | Guid／NN | organization_id／UUID | Restrict FK → organizations |
| CaseId | Guid?／N | case_id／UUID | Restrict FK → cases；NULL 僅表示帳號層 Scope 的候選語意 |
| ScopeKey | string／NN | scope_key／慣例 varchar(40) | 明確唯一範圍，不能只靠 nullable CaseId |
| DocumentContentHash | string／NN | document_content_hash／慣例 varchar(64) | 同意當下文件 Hash |
| DataUseDecision／TeacherParticipation | string／NN | data_use_decision／teacher_participation，慣例 varchar(16) | UNDECIDED 不等於同意 |
| RequestId | Guid／NN | request_id／UUID | 去重識別 |
| RecordedAtUtc | DateTime／NN | recorded_at_utc／datetime(6) | 同意紀錄時間 |
| RevokedAtUtc | DateTime?／N | revoked_at_utc／datetime(6) | 撤銷政策尚待院方確認 |

保留 (actor_user_id, request_id) 與 (actor_user_id, organization_id, document_version_id, scope_key) 兩個唯一索引（來源未明訂名稱）。同版本撤銷後再次同意可能受後者限制，待 11／院方決定，不刪舊列或改 Key 繞過。表 charset／Provider 慣例與未明訂索引名稱須由合併模型核對。

### 必須一併保留的 User 差異

11 的 Models/Authentication/User.cs 與 IdentityModelConfiguration.cs 增加 phone_verified_at_utc／email_verified_at_utc（DateTime?、datetime(6)、N）、email／normalized_email（string?、varchar(254)、N）。normalized_email 為 utf8mb4_bin、ix_users_normalized_email **非唯一**。其他既有 User PK、Role／Organization FK、帳號狀態與權限不改。不得推定電話／Email 已驗證或將相同 Email 的帳號自動合併。

## 5. 16 History 必須保留

11 固定版本缺此 Entity／DbSet／Mapping；候選聯集保留 16 全部內容，不以 11 DbContext 或 Snapshot 蓋掉。

| CLR 欄位 | CLR／NULL | DB 欄名／型別 | 必要性 |
|---|---|---|---|
| HistoryId | Guid／NN | history_id／UUID | PK |
| CaseId | Guid／NN | case_id／UUID | Restrict FK → cases；ix_case_status_histories_case_time(case_id, changed_at_utc, history_id) |
| OldStatus／NewStatus | string／NN | old_status／new_status，既有六狀態 ENUM | 真正狀態轉換 |
| ChangedAtUtc | DateTime／NN | changed_at_utc／datetime(6) | 時間 |
| ChangedByUserId | Guid?／N | changed_by_user_id／UUID | Restrict FK → users；ix_case_status_histories_changed_by_user |
| TriggerSource | string／NN | trigger_source／ENUM(MANUAL,QUESTIONNAIRE_COMPLETION,SYSTEM_WORKFLOW) | 不把家長當醫護作者 |
| Reason | string?／N | reason／text | 不放答案或秘密 |
| ChangedByUser | User? navigation | 無獨立 DB 欄位 | 保留 navigation／FK，不增加作者欄位副本 |

History 無 Response／輪次事件唯一鍵；若 Workflow 需要新去重資源，另提候選，不能本批先加。既有人工狀態 Service／Store 與 History 查閱政策不因作者聯集被放寬。

## 6. DbContext／Configuration 聯集方式

| 集合 | 候選保留內容 |
|---|---|
| 雙方共通 DbSet | Cases、Questionnaires、QuestionnaireVersions、QuestionnaireTasks、TeacherGrants、TeacherGrantTasks、TeacherSessions、QuestionnaireDrafts、QuestionnaireResponses、AuditLogs、Users、Roles、Permissions、RolePermissions、Organizations |
| 11 額外 | Guardians、CaseGuardians、ParentBindingInvitations |
| 16 額外 | CaseStatusHistories |
| Consent | 現有 11 沒有明確 DbSet，但兩個 IEntityTypeConfiguration 經 assembly 掃描納入，Service 使用 db.Set<T>()；不可因缺 DbSet 而漏模型，不必為 Reader 額外增加 DbSet |

保留 ApplyConfigurationsFromAssembly 與 AuthorizationSeed.Seed；同時只有一個 ApplicationDbContext 定義。保留共用 Core，新增／整合 GuardianModelConfiguration、ConsentModels 中的 Configuration、16 ParentQuestionnaireModelConfiguration 與 CaseStatusHistoryConfiguration；不得再複製同名 Configuration。這些設定不同屬性時可並存，但不能依賴 assembly 掃描順序解決同屬性的矛盾設定。

Core／Identity／Guardian／History 的 User／Case／Organization FK 必須驗證相同 key 型別／charset／collation。單個 FK 成立不保證跨院所或同意 Scope 正確；作者 reader 更不提供 Case 授權。保留 11 Case 院所關聯與 16 History FK，不能為建置通過取消任一方安全關係。

## 7. Migration／Snapshot 精確現況

透過 GitHub 固定目錄核對 11 有前四筆 Migration／Designer；16 有五筆。前四筆及各 Designer 的 blob 均相同，不是需要重寫的衝突：

| 必保留 Migration | 11 | 16 |
|---|---|---|
| 20261005152359_InitialSharedCore | 有 | 有 |
| 20261005180101_AddAuditLogs | 有 | 有 |
| 20261005184434_AddSubmissionReceiptWindow | 有 | 有 |
| 20261006074424_IntegrateIdentityAndAuthorization | 有 | 有 |
| 20261019131342_AddCaseStatusHistories | 無 | 有；連同 Designer 保留 |

- 11 Snapshot blob `c8ad951e49108a62be0974ae377305254bcbbdce`：保留早期核心／Identity，沒有家長作者、Relationship、HashVersion、Guardian／Consent／Case 擴充或 CaseStatusHistory 模型。
- 16 Snapshot blob `5156015938053ea679d2e05c060c89b63e898e61`：有 History，但仍無上述作者三類增量及 11 模型。
- 因此**兩邊的現有 EF 模型都不能等同其 Snapshot 或已部署 Schema**。不得使用 11 Snapshot 覆寫 16 而丟 History，也不能只選 16 Snapshot 就宣稱 11 模型已存在。
- 本輪未執行 EF differ、產生 Migration、建置或重跑測試。既有教師 `no pending schema/model changes` 及舊家長 `only six additive author schema operations proposed` 的 Schema gate 真實 FAIL／後續 BLOCKED 紀錄保持有效；不改預期掩蓋差異。

本作者完整模型相對 16 現 Snapshot 的靜態候選增量為兩個作者欄位、兩個 Relationship、Response HashVersion，以及兩個作者索引與兩個 FK；這不是共同模型全部增量，也不是已生成 DDL。Case／User／Guardian／Consent 所需表欄位另審，不能把所有差異誤稱成五欄。

## 8. 舊資料與安全拒絕規則

1. 舊作者 NULL 不改為目前登入者／Guardian；Guid.Empty 是不合法作者，不轉成不存在。FK 只驗存在，不驗資格。
2. 舊 Relationship 保留 NULL，不拿 CaseGuardian.Relationship 補值。新家長 Submit 必填由 16 核心驗證；教師仍不要求填此欄位。
3. HashVersion NULL 表示未記錄／未驗證版本，不猜舊演算法、不重算 PayloadHash。新版成功重送遇 NULL／未知版本仍拒絕；已授權且已知作者的歷史收據查閱與重送分開。
4. Consent／文件缺核准證據、Scope 未定、綁定／世代未知不作資格通過；不從 UNDECIDED／DRAFT／UNCONFIRMED CLR 初值建立同意。
5. 既有教師 Grant FK／Key／Hash 行為與答案全部保存，不刪或搬移以消除約束衝突。非 NULL 作者孤兒、重複 Key、Task／版本／Grant Scope 孤兒都需先唯讀盤點，再由負責人處理。

## 9. Reader 替換與正式入口界線

11 IParentAuthorVisibility／MetadataParentAuthorVisibility 與 16 共用 Reader 契約相同。wrapper 同時檢查兩列、空 actor／Task、未知／外人作者及 Grant；矛盾的不存在列 metadata 也拒絕，例外／取消不轉成不存在。現有 LegacyParentAuthorMetadataReader 只投影存在／Grant，作者一律 NULL，因此既有列必拒絕；不是正式作者已串接。

替換 EfParentAuthorMetadataReader 的必要前置：

1. 固定共同 Entity／Mapping 編譯來源；依使用路徑決定 Reader-only 或完整 Submit 模型批次。直接拷貝 EF Reader 到沒有作者屬性的 11 版本無法編譯。
2. 核准增量 Schema 並於隔離庫驗證後，才允許真實 DB 路徑使用。即使 Reader SQL 不選 Relationship／HashVersion，也不能把其他新版 Entity 操作接到未升級庫。
3. 在唯一 composition root 明確替換 IParentAuthorMetadataReader 的 Legacy 註冊，保留 IParentAuthorVisibility → MetadataParentAuthorVisibility。11 目前用 TryAddScoped；新增另一個 TryAddScoped 不會覆蓋先前註冊。明確核對單一有效綁定，不靠順序猜測。
4. 手動 new ParentQuestionnaireAccess(..., authors:null) 仍走 LegacyParentAuthorVisibility → Legacy Reader，不會自動使用新 DI Reader。核對所有工廠、測試與手動建構點，明確傳入新版 wrapper；不移除預設安全拒絕改成允許。
5. 傳入原 scoped DbContext／TaskId／CancellationToken，不呼叫 Store.OpenAsync、不另開 Context、不清追蹤／提交交易。Reader 前先完成可信 Actor 與 Case／Task 範圍；預檢之後核心仍要同交易重新授權／查作者。
6. 重跑 metadata-only 真 EF 投影＋wrapper／HTTP 負向案例；兩表缺失、NULL／空／跨作者、教師／混合來源、任一查詢錯誤、取消都必測。兩次 SELECT 不宣稱原子快照，隔離／鎖序另驗。

Reader 替換不等於正式 Submit 可啟用：Unavailable Gateway、16 Deny Adapter、Development gate 仍保留，直到真正 Stamp／資格世代、共同鎖序與所有寫入端均核准並驗收。11 已引用共用 Evidence 不代表它已有來源或授權能力。

## 10. 最小修改責任與順序（候選、需共同批准）

| 階段 | 最小檔案／責任 | 停止條件 |
|---|---|---|
| 1 白名單核定 | 16 統整本文件；11 確認 Case／User／Guardian／Consent 原始欄位、舊資料及語意 | 欄位來源或舊資料政策不明就排除執行 |
| 2 作者來源聯集 | 16 審查 Draft／Response Entity、ParentQuestionnaireModelConfiguration；11 在核准整合版本保留自己的模型增量 | 不直接修改對方 Worktree；缺完整屬性不得拷 Mapping |
| 3 Context 模型聯集 | 16 統整 ApplicationDbContext、CaseRecord、User、GuardianModelConfiguration、ConsentModels、History Entity／Mapping；11 保有業務模型所有權 | 不整檔覆寫、不丟 DbSet／Configuration／Seed；有矛盾設定先決策 |
| 4 離線模型驗證 | 16：合併模型欄位／NULL／FK／索引、教師相容、Reader SQL；11：wrapper／Context／HTTP 遮蔽 | 未完成 Migration 前 Schema gate 不得改為假 PASS |
| 5 增量 Migration 審查 | 16 於另行核准後產生，11／07 共審與各自模型負責；保留五筆歷史 | 產物有非白名單 Drop／Alter／回填即停止審查 |
| 6 隔離 DB 驗收 | 16 經核准後在專用隔離實例驗證；不使用 MySQL80／3306 | 備份、目標、版本、還原能力未確認不操作 |
| 7 Reader 替換 | 11 修改 ParentAuthorVisibility／ParentModuleRegistration、手動建構點與 ParentV53Harness；重用 16 原 EF Reader／契約 | 正式 Gateway 保持 Unavailable，不能順便接 Submit |
| 8 正式資格／交易 | 11 正式 Adapter；16 Gateway／save-only；07 stage-only Workflow／Scope；三人核定全域鎖序 | Case → Task 只是局部偏序；ABA／政策不明不啟用 |

既有 Reader 契約、Evidence、Checks 已逐檔由 11 引用，不另複製第二份型別。16 可先獨立準備欄位／約束驗收資料矩陣及模型差異審查；真實合併、DI 替換、save-only 或 Migration 都需要另批授權與固定交付。

## 11. 隔離驗收前置與待確認問題

以下只列未來檢核，**均非本輪已執行或已通過**：

- [ ] 11 確認 Reader-only 兩作者欄位批次，或一次納入五欄完整 Submit 模型；確認既有 NULL 不回填。
- [ ] 11 確認 Case NN 新欄位的舊資料導入方案、院所未知資料、共用電話／模糊 Email 與 Consent 重新同意唯一鍵語意。
- [ ] 合併模型逐項核對 PK／nullable FK／索引／collation、Configuration 掃描與五筆 History 保留；未確定的 BindingGeneration、Epoch、有效輪次／封存一律不加入本白名單。
- [ ] 核准固定應用版本、Migration／Snapshot 產物、隔離實例／端口、虛構資料、套用前備份與獨立還原庫。
- [ ] 比對 Migration 歷史、各表筆數、原 Hash bytes、NULL 作者／關係／版本保留；測試孤兒 FK、重複作者／Grant Key、同 Task 多 Response 與回滾／復原。
- [ ] MySQL 驗證 active_case_id computed／唯一鍵、UUID collation／FK、nullable 唯一鍵及 Consent Scope；不以 SQLite／替身 ADO 取代。
- [ ] Schema gate 的合法預期只能在核准模型／增量真正一致後更新，不能為使測試通過而刪除 gate。
- [ ] 核對 DI 與手動 wrapper 同用真 Reader；資料庫缺欄位／Timeout／取消不可轉成作者不存在或成功。
- [ ] 核准回復方案：未 Commit 的交易回滾；已套用變更需先評估舊程式相容與獨立還原，不盲目 Down 刪作者／答案／History。

授權／Consent 本文與撤回、重新同意、教師參與撤回及保存期限仍需老師／院方決策。模型聯集、FK 或 metadata 查詢成功都不能替代這些政策及同交易資格重驗。
