# V5.3 醫療Task／基本Completion唯讀契約

固定實作起點：16 `f8272f55936a46782fb873ca02c3213d077aa305`，分支`codex/v53-parent-core`。本批僅新增獨立測試，不修改正式來源、Gate、Schema或07／11契約。

```powershell
dotnet restore tests/MedicalReadContract/MedicalReadContractHarness.csproj --source tests/MedicalReadContract -p:NuGetAudit=false
dotnet build tests/MedicalReadContract/MedicalReadContractHarness.csproj --no-restore -p:NuGetAudit=false
dotnet tests/MedicalReadContract/bin/Debug/net8.0/MedicalReadContractHarness.dll
$LASTEXITCODE
```

使用現有本機套件快取；若缺少套件，停止，不更換來源下載。本次實跑**46 PASS、0 FAIL、5 BLOCKED，退出碼2**；建置0警告、0錯誤。1代表FAIL，2代表尚有BLOCKED，不改為成功退出碼。初次43 PASS後新增三項DTO型別／任務讀取錯誤測試，並非修正正式規則。

## 真正執行的正式程式

- [Controller](../../EarlyInterventionCare.Api/Controllers/QuestionnairesController.cs)：直接呼叫Tasks／Completion及其帳號／角色／Development fixture gate；反射核對HttpGet、Authorize與NoStore metadata。
- [Service](../../EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireService.cs)：原始ListTasksAsync、RequireCase與CompletionAsync，不複製完成度演算法。
- [EF Store](../../EarlyInterventionCare.Api/Services/Questionnaires/EfQuestionnaireStore.cs)：實際Pomelo翻譯Case EXISTS、帳號JOIN、Task／Version／Questionnaire查詢並執行EF物化與DTO投影。
- [DTO](../../EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireContracts.cs)：直接使用CaseTaskResponse與CaseTasksCompletionResponse，檢查精確欄位／CLR型別及JsonSerializerDefaults.Web輸出。
- 重用[OfflineAdo](../EfParentStore/OfflineAdo.cs)的無Socket DbConnection／DbCommand；新Reader只攔截並提供合成列。連線Open被抑制，未攔截命令會拋錯，SaveChanges被拒絕，使用明確MySQL 8.0.46轉譯設定，沒有連線字串、AutoDetect或私人設定。

## 固定GET契約

| Method／Route | Request | Response |
|---|---|---|
| GET `/api/cases/{caseId:guid}/tasks` | 路徑Guid caseId，無Body | CaseTaskResponse陣列；存在個案但無任務時`[]` |
| GET `/api/cases/{caseId:guid}/tasks/completion` | 路徑Guid caseId，無Body | CaseTasksCompletionResponse；基本跨輪次統計 |

Task JSON精確欄位：taskId、caseId、questionnaireId、questionnaireVersionId、questionnaireTitle、versionNumber、respondentRole、assignmentRound、isRequired、taskStatus、createdAtUtc、updatedAtUtc、submittedAtUtc。Guid為字串，round為uint數值，isRequired為bool；只有submittedAtUtc為nullable DateTime，未提交保持JSON null。正式Store將時間標記為UTC，序列化為Z；不含答案、Hash、教師Grant或取消時間。

Completion JSON精確欄位：caseId、totalTaskCount、requiredTaskCount、requiredSubmittedCount、requiredPendingCount、requiredInProgressCount、allRequiredSubmitted。不含有效輪次、政策來源或輪次狀態；不可當成可信的有效輪次完成度。

排序核對**正式產生的SQL ORDER BY**依序為assignment_round、questionnaire_code、respondent_role、task_id，且有Case條件與兩個INNER JOIN。替身提供已排好順序的合成列，只驗證EF物化後順序保留；它不執行SQL排序／JOIN／WHERE引擎。不同Case的合成列由替身按參數篩出，配合SQL篩選條件斷言，不能宣稱資料庫隔離已驗收。

## 現有Completion行為（觀察，不是政策批准）

| 合成情境 | 總數／必要／已提交／待填／填寫中 | allRequiredSubmitted |
|---|---|---|
| 必要兩份SUBMITTED，選填一份PENDING | 3／2／2／0／0 | true |
| 必要SUBMITTED＋PENDING | 2／2／1／1／0 | false |
| 必要IN_PROGRESS | 1／1／0／0／1 | false |
| 只有選填SUBMITTED | 1／0／0／0／0 | false |
| 必要SUBMITTED＋必要CANCELLED | 2／2／1／0／0 | false |
| 必要SUBMITTED＋選填CANCELLED | 2／1／1／0／0 | true |
| 舊輪必要PENDING＋新輪必要SUBMITTED | 2／2／1／1／0 | false |
| 兩輪必要都SUBMITTED | 2／2／2／0／0 | true |
| 存在個案，無任務 | 0／0／0／0／0 | false |

取消的必要任務沒有自動排除，因此必要數未必等於已提交＋待填＋填寫中。沒有有效輪次過濾，不自動更新CaseStatus／History。不存在Case為Service的CASE_NOT_FOUND／404例外；空Guid在查詢前INVALID_INPUT／400；兩者均不是HTTP驗收。

## 安全邊界與五項BLOCKED

1. **HTTP／認證管線**：本工具沒有啟動API或HttpServer；Authorize Filter、JWT／Cookie、實際路由匹配、MVC輸出formatter與401／403／404全管線驗收都BLOCKED。直接Controller的UnauthorizedResult／ForbidResult／ObjectResult不是線上HTTP回應。
2. **正式Permission／Scope**：Controller仍從資料庫投影ACTIVE帳號／角色，只允許ADMIN、CASE_MANAGER；MEDICAL_STAFF、PARENT、TEACHER均Forbid。Development只允許既有CoreDevelopmentSeed.CaseId；IsolatedTest／Production與其他Case拒絕且不查Task。缺少／非法／重複userId不查詢；停用帳號拒絕。DenyCaseTaskReadScope的Case／Task方法仍false，但它未接入此Controller；沒有把舊Development Gate當成07的正式Scope，也沒有補授權或新增角色Permission。
3. **政策／Workflow**：有效輪次、取消必要任務、零必要任務的正式規則與Workflow尚待共同確認，本測試只保留現有跨輪次行為。
4. **MySQL／Schema**：EF SQL翻譯、參數、物化／DTO投影PASS；真正MySQL排序、JOIN、Case過濾、FK／Index及共同Migration／Snapshot仍BLOCKED。沒有SQL送往任何資料庫。原教師／舊家長Schema Gate完全未修改，本批沒有以此Harness取代那些Gate。
5. **全域例外安全**：讀取失敗會原樣拋錯，不偽裝空清單／完成；既有QuestionnaireErrors Filter的已知DbException安全503分支可直接測試，但不代表通用Exception Handler或完整HTTP安全回應已完成。

所有資料為明示合成ID／文字，不建立帳號、token、Stamp或病患資料。測試Reader不是授權Adapter，也不提供正式可注入的入口。Program／DI未變；實際DI及部署驗收不在本批PASS範圍。

## 07可依據與下一步

可固定上述兩個GET的路由、DTO及跨輪次基本統計；還不能承諾MEDICAL_STAFF可讀、IsolatedTest業務HTTP成功、正式Case Scope、有效輪次完成度或共同資料庫已可用。需07提供固定Case GET／Scope交接版本，再共同核准只讀Permission接線與隔離Schema；不得藉此開放POST指派或家長Submit。
