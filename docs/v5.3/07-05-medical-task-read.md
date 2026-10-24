# V5.3 07-05：醫療端 Task／完成度唯讀消費端

日期：2026-10-25（本輪自 10/24 接續）。工作基準：`feature/medical-backend`，`44ec4e4713ae26a2d367d4e64335c949bddfe3ce`。僅核對本機程式，未 Fetch／Pull／Merge。這是正式路由的前端消費端準備，不是正式 MySQL 驗收。

## 一、本機實際 API 與限制

依 `Controllers/QuestionnairesController.cs` 的 `Tasks`／`Completion`、`DTOs/Questionnaires/QuestionnaireContracts.cs` 的 `CaseTaskResponse`／`CaseTasksCompletionResponse`、`Services/Questionnaires/QuestionnaireService.cs` 的 `ListTasksAsync`／`CompletionAsync`，以及 `EfQuestionnaireStore.ListTasksAsync` 查證：

- GET `/api/cases/{caseId:guid}/tasks`，無 request body、分頁、搜尋或輪次 query；200 直接回傳 `CaseTaskResponse[]`，無任務為 `[]`。沒有 totalCount envelope。
- GET `/api/cases/{caseId:guid}/tasks/completion`，無 request body，200 回單一 `CaseTasksCompletionResponse`。
- `[Authorize]`、`CheckUser` 要求唯一且有效的 userId claim、資料庫 ACTIVE 帳號，角色只能 ADMIN／CASE_MANAGER。這不是完整共用 RBAC／資源 Policy 接入；既有 StaffAuth 又只接受 CASE_MANAGER／MEDICAL_STAFF，角色範圍尚不一致，不能自行擴權或假設 MEDICAL_STAFF 能呼叫。
- `CaseAllowed` 必須 `environment.IsDevelopment()` 且等於 `CoreDevelopmentSeed.CaseId`（`10000000-0000-4000-8000-000000000001`）。非指定個案或非 Development 回 403 `CASE_ACCESS_NOT_INTEGRATED`。本次未更改此閘門，不能把前端可發 UUID 請求宣稱正式個案已接通。
- 未登入／帳號無效為 401；角色不符為 Forbid／403。通過 Development 閘門之後，`RequireCase` 才可能回 404 `CASE_NOT_FOUND`。不可宣稱所有不存在／越院個案目前都回 404；Guid route 格式錯誤亦可能由 routing 回 404。
- `QuestionnaireErrorsAttribute` 將資料庫／EF 更新／Timeout 例外映射 503 `SERVICE_UNAVAILABLE`。接口 NoStore。上述皆是原始碼查證，沒有啟動真 API 或連線資料庫測試。

### Task wire DTO：十三欄位

|欄位|既有型別／消費方式|
|---|---|
|taskId、caseId、questionnaireId、questionnaireVersionId|Guid → UUID 字串；caseId 必須與當前請求完全一致，拒絕 Demo ID／跨個案資料|
|questionnaireTitle、versionNumber|字串；安全文字顯示，不拼入 HTML|
|respondentRole|PARENT／TEACHER；只顯示家長／教師，沒有填答者姓名|
|assignmentRound|uint 正整數；原值顯示，不猜有效輪次|
|isRequired|boolean；顯示必要／非必要|
|taskStatus|PENDING／IN_PROGRESS／SUBMITTED／CANCELLED，資料模型以字串及 MySQL ENUM 約束，非新增 C# enum|
|createdAtUtc、updatedAtUtc|UTC DateTime；Client 驗證並保留 metadata|
|submittedAtUtc|nullable UTC DateTime；未發生為 null，畫面未提供；已提交須有時間，非提交任務不偽造時間|

沿用既有消費端「SUBMITTED 有提交時間、其他狀態為 null」驗證，待 16 確認未來是否存在已提交再取消的語意。Client 已補 uint 上限、有效 UTC 日期、完整欄位保留及拒絕重複 taskId。額外 answers／score／observation／token／permissions 不進入消費 DTO／ViewModel。

四種正式狀態直接映射：PENDING＝待填、IN_PROGRESS＝進行中、SUBMITTED＝已提交、CANCELLED＝已取消。「已指派」是清單標題與服務的 totalTaskCount，不新增 ASSIGNED 狀態；SUBMITTED 不代表評估完成或結案。

### Completion wire DTO：七欄位

`caseId`、`totalTaskCount`、`requiredTaskCount`、`requiredSubmittedCount`、`requiredPendingCount`、`requiredInProgressCount`、`allRequiredSubmitted`。五個 count 是非負 int、最後一欄 boolean。Client 僅驗型別、範圍及 caseId，不從清單重算。

現有後端 `CompletionAsync` 直接使用該個案全部 tasks：total 為全部筆數，required 為全部 IsRequired，依 SUBMITTED／PENDING／IN_PROGRESS 計數；required 非空且全部提交時 allRequiredSubmitted=true。`EfQuestionnaireStore.ListTasksAsync` 沒有篩最新輪次或排除 CANCELLED；因此取消的必要任務仍可能在 required 分母，歷史輪次也可能混入。這是現況，不是 07 核定的正式完成度規則。前端保留原回傳數字，不計算百分比、取消分母或有效輪次，也不加總推測缺少的 count。

固定虛構 completion 範例（不是資料庫 seed）：

```json
{
  "caseId": "20000000-0000-4000-8000-000000000001",
  "totalTaskCount": 4,
  "requiredTaskCount": 3,
  "requiredSubmittedCount": 1,
  "requiredPendingCount": 1,
  "requiredInProgressCount": 1,
  "allRequiredSubmitted": false
}
```

完整十三欄 Task JSON 固定範例見 `tests/MedicalTaskFixtures.cjs`；四種狀態均為明確虛構資料，Fixture CaseId 不是正式 Development seed 的白名單。本機 HTTP Fixture 回 200 不表示繞過或修改正式 CaseAllowed。

## 二、前端架構與資料隔離

1. `medical-api.js.createClient().tasks(caseId)`／`taskCompletion(caseId)` 沿用唯一 Transport 與 StaffAuth Cookie／CSRF／錯誤處理；沒有第二套 fetch 或登入。
2. `medical-task-model.js`：`createApiAdapter`、`toViewModel`、`createStore`。資料取得、DTO 邊界、ViewModel、載入／空／錯誤狀態與畫面分離。清單與統計分開讀取，並非原子快照；revision 防止舊個案及 pagehide 後回應覆蓋。每次載入立即清舊值，回應時再驗 Session。
3. `medical-task-view.js.mount`：純文字 DOM render。清單顯示問卷／版本、填答角色、狀態、原輪次、必要性、臺北提交時間及結果未提供。沒有前端原始答案、計分、填答者姓名或下載 URL 契約，不產生結果／Demo 連結。
4. `medical-case-detail.js` 僅在 `MedicalCaseMode.mode=api` 且 Case GET 成功後載入兩個 Task 接點。表單紀錄頁籤與上方統計使用真路由消費值，沒有值時未提供或載入中，不補零／假成功。保留 Sidebar／Header／綠色樣式及原 Case 遮罩。
5. 純 Adapter 可以在 completion 503 時保留獨立成功的 Task 清單；**實際 StaffAuth 目前對任何非成功 HTTP 都會撤銷當前權限**。此時整個 Case／Task 立即清空，由父頁顯示安全錯誤與重試，不保留局部醫療資料。本次只修正消費端避免該事件把 403／503 錯誤吞成永久載入；未修改 StaffAuth。401 仍依原流程清空保護區並導向登入。重試先重新確認 Session。
6. 既有 `case-detail.js`／`system-demo-ui.js` 在 API 模式提前 return，未修改其 Demo 寫入。`shared-demo.js` 原 localStorage 僅保留既有展示用途；新 Task 模組不讀寫 localStorage／sessionStorage，也不依 browser role／organizationId 授權。正式模式沒有 SharedDemo 回退。
7. `response-detail.js`／HTML 目前仍是受保護 Development Demo／既有 local-demo 的結果流程，不是一般醫療 Response API。本次保留 DEMO-001 SNAP-IV 26 題，不改 Draft／Submit／答案來源；不把正式 SUBMITTED 導向 Demo 答案。

## 三、實際修改範圍

新增：`wwwroot/js/medical-task-model.js`、`wwwroot/js/medical-task-view.js`、`tests/MedicalTaskFixtures.cjs`、`tests/MedicalTaskFrontend.test.cjs`、本文件。

修改：`wwwroot/js/medical-api.js`、`wwwroot/js/medical-case-detail.js`、`wwwroot/case-detail.html`、`wwwroot/case-detail-empty.html`、`wwwroot/css/case-detail.css`、`tests/MedicalCasePreviewServer.cjs`。合計 11 檔，5 新增／6 修改。不改 C#、Case CRUD、RBAC、Authentication、Workflow、SessionVersion、資料模型、DbContext、Migration、Seed 或 DenyAll。

## 四、驗證與重現

```powershell
node --test tests/MedicalTaskFrontend.test.cjs
node --test tests/MedicalTaskFrontend.test.cjs tests/MedicalApiClient.test.cjs tests/MedicalCaseFrontend.test.cjs tests/MedicalCaseGetContract.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/MedicalDemoFixtures.test.cjs tests/MedicalDemoEntry.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:UseAppHost=false
dotnet run --project tests/QuestionnairePhase21Harness.csproj --no-restore -p:UseAppHost=false
node tests/MedicalCasePreviewServer.cjs
git diff --check
```

- 新增 Task 21 項通過；完整前端回歸 214 項通過、0 失敗／跳過。測試注入 Fake Response、隔離 Adapter、最小 DOM，另用 loopback HTTP Fixture 驗證真 JS Client 所需路徑／JSON／錯誤，不使用真資料庫。
- 涵蓋清單、completion、空清單、四狀態、401／403／404／503、跨 Case Task／統計、重複 task、無效 ID／DTO、uint／UTC／null、舊回應與權限撤銷、正式模式無 Demo 回退、清除舊 DOM、安全文字及重試。
- API Release Build 成功，0 警告／0 錯誤。
- 現有 QuestionnairePhase21Harness 離線 Fake Store 指派／完成度回歸通過；執行時出現 NU1900 套件弱點資訊來源不可用警告，與上述 Release Build 結果分開記錄。
- 嘗試 QuestionnairePhase22Harness `--no-restore` 因缺少其 `project.assets.json`，NETSDK1004，**未執行測試**；沒有為此還原／下載套件，不宣稱 Phase 2-2 通過。
- 瀏覽器 1440×900：正式模式清單四狀態、server 原值統計、臺北跨日時間、空清單、403／503 清資料後可見錯誤、401 回登入、跨 Case Task 拒絕；無結果連結。長清單可由 main 垂直捲動，表格底部可到達，無文件橫向溢出。390×844 文件寬 375px（不超 viewport）；結束還原 viewport。
- Demo 從表單紀錄開啟原始答案，確認「26 題」且 DOM 有 26 題，Console 最後檢查無 error／warn。這是合成醫療 Session，不是正式登入或 HTTPS Cookie 驗收。
- Fixture 操作：使用 console 輸出的 loopback 位址，開 `/case-detail.html?source=api&caseId=20000000-0000-4000-8000-000000000001`，點表單紀錄；加 `taskScenario=empty|expired|forbidden|missing|unavailable|wrong-case|completion-unavailable` 測情境。這些參數僅由測試 Host 判讀，產品 Client 不送它們給正式 API。

## 五、待 16 確認／正式整合阻塞

- 提供固定 Task／completion producer Commit 及契約版本；核對正式可用環境、醫療角色與共用 `questionnaire.task.read`／資源授權接入。不可由 07 自行刪除 CaseAllowed／DenyAll 或擴權。
- 由 16 與權限主責確定一般 Case 的 scope／院所邊界及 403／404 行為，與 11 Case GET 一致；目前 Case GET 可讀不表示 Task API 可讀。
- 確認 completion 有效輪次、歷史輪次、取消必要任務、無必要任務的語意。07 不從列表另外實作同一套正式計算。兩接口無 snapshot/version，若需一致時點須另訂契約。
- 確認取消與 submittedAtUtc 的組合、未來新狀態／欄位的版本策略。目前對未知狀態安全拒絕。
- 若要正式結果查閱／匯出，需 16 提供受授權 Response DTO／路由、taskId／caseId 一致性與錯誤碼，再另做 07-06；目前不創建候選假路由，不改 Response 核心。
- 由 16 提供全虛構資料的地端 API／MySQL 固定環境、正式醫療 Session／HTTPS 與可讀／越權 Case 測試資料，核對跨院所拒絕、提交後重查、重啟保留。上述 Fake／HTTP／瀏覽器驗收都不能替代此真服務驗收。

未連接或變更 MySQL，未執行 Migration、部署或任何 Git 合併／提交／推送。既有 Workflow／SQL／其他未追蹤成果未動。本批適合獨立 Commit，但尚未建立。
