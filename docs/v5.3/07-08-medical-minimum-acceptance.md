# V5.3 07-08：11/09 醫療端最小整合驗收紀錄

更新日期：2026-11-08（Asia/Taipei）。工作分支：`feature/medical-backend`。

**結論：07 Consumer、API 導覽及支援性測試已有通過紀錄；共同 HTTPS Host／隔離 MySQL 的 12 項正式驗收全部 BLOCKED，尚未通過。**

本輪只更新本文件，核對 Git、程式與既有紀錄；沒有重跑測試、Build、啟動 Host、連線資料庫、執行 Migration／Seed 或 Git 整合。以下 PASS 均標示先前實際執行的來源，不是本輪新結果。其他未提交／未追蹤成果保留，不暫存、Commit 或 Push。

## 1. 固定版本與證據界線

|模組|固定版本|目前可驗證的範圍|
|---|---|---|
|07 醫療端|`0c91baa22967884f6d694560aef2915df945be44`|本機完整 Git object、目前 HEAD；本輪未查即時遠端，不能據此宣稱此 SHA 已 Push|
|07 Case Consumer|`56eb9f21f8928e4310cb5c5eb7255778b35923bb`|已包含於上述 HEAD；07-04 固定成果|
|07 Task 契約對齊|`fdeeb8c00f2834e94f27477f141047ae354f5969`|已包含於上述 HEAD；07-05 固定成果|
|11 Case Producer／CaseAccess|`d268b7d70be33e04d44bdfa044b13339448815f6`|本輪 git cat-file 查無 object；先前以 HTTPS 核對固定原始碼，**未驗證完整 Git 版本**，未在 07 執行此 producer|
|16 Task／Completion|`3d5b01cb221bda9b54e9c04a4b8ca6332233a164`|本輪 git cat-file 查無 object；先前以 HTTPS 核對固定原始碼，**未驗證完整 Git 版本**，07 未執行該固定版 Harness|
|共同整合 Host|尚無核准固定 SHA|三個 pin 並列不表示已有共同可執行版本|

證據分級：

- **07 實際執行紀錄**：本工作對話先前執行的測試／瀏覽器／Build，對照 07 文件；未保存的完整 console log 不宣稱已封存。
- **固定原始碼核對**：可核對路由、DTO、Gate 與邏輯，不證明已部署、Schema 相容或真實 HTTP 成功。
- **16 固定文件回報**：保留來源及替身界線，不列為 07 親自執行；不採口頭 PASS 代替證據。
- **正式驗收**：需核准共同 HTTPS Host、相容 Schema、隔離 MySQL、全虛構帳號／個案及可追溯的實際輸出，目前尚無結果。

狀態定義：PASS＝該列限定測試已執行且符合預期；FAIL＝已執行但不符合預期；BLOCKED＝已知前置條件未齊，無法執行；NOT RUN＝尚未執行且未確認具體阻塞。正式表不可用離線 PASS 代填。

## 2. 現有成果與固定契約

### 07 醫療端

|範圍|已存在的成果|來源／驗收界線|
|---|---|---|
|醫療登入|POST /api/auth/staff-session；GET /api/auth/me；GET /api/auth/csrf；POST /api/auth/logout|StaffSessionController、AuthController、AuthenticationService；真實 HTTP／MySQL 登入未通過|
|驗證方式|預設 Scheme **Bearer**；無 Authorization Header 才讀固定 JWT Cookie；有 Header 不退回 Cookie；維持簽章／Issuer／Audience／期限驗證|Program.cs、StaffCookieTokenReader；不是 AddSession／UseSession。可信 userId Claim 解析為非空 Guid，不接受前端自報身分|
|Cookie／CSRF|__Host-earlycare-staff：HttpOnly、Secure、SameSite=Strict、Path=/、無 Domain、到期不超過 JWT；Cookie 寫入驗 Origin＋Antiforgery|StaffSessionCookieService、StaffCookieCsrfMiddleware；登出只清 Cookie，不撤銷 JWT 本身|
|角色|Cookie 登入允許 CASE_MANAGER／MEDICAL_STAFF；核對 ACTIVE 帳號、目前 Role、ACTIVE 院所及單位代碼|StaffSessionAuthorization；登入成功不代表所有 Permission／Case Scope。ADMIN 不因 Case Producer 可讀就自動開放登入|
|API Client／Case|集中式 medical-api.js、GUID／分頁、安全 DTO、遮罩、錯誤清資料、無 totalCount 假設|[07-04](07-04-case-get-contract.md)；不替代後端授權、不新增敏感列表展示|
|Task／基本 Completion|四狀態、必要性、原始輪次與七欄統計，空資料／錯誤處理|[07-05](07-05-medical-task-read.md)；不推算有效輪次、不觸發 Workflow|
|API 導覽／發布|登入→Dashboard→列表→GUID 詳細→Task／Completion→返回→登出保留 API 模式|[07-06](07-06-medical-static-frontend.md)；來源 wwwroot 預設仍為 Demo，須明確 source=api 或 API 發布包；API 失敗不得切回 Demo|

### 11 Case：d268b7d（固定原始碼核對，非執行驗收）

- GET `/api/cases?page=1&pageSize=20`：直接陣列；空資料 []；無 totalCount。page 1–100000，pageSize 1–100。
- GET `/api/cases/{caseId:guid}`：不存在或不可讀均 404 CASE_NOT_FOUND，不洩漏存在性。
- CaseDto：caseId、caseCode、childName、birthDate、sex、nationalId、address、caseStatus、revision、createdAtUtc、updatedAtUtc。型別／安全投影詳見 07-04；未提供資料不補成 Demo。
- CaseAccess 以已驗證 userId 重新查目前 ACTIVE User／Organization 與 DB Role；ADMIN／CASE_MANAGER／MEDICAL_STAFF 可讀同院所，跨院所不可讀。這是目前版本契約，非院方最終政策。
- Controller 為一般 [Authorize]；11 獨立 Program 沒有 07 Cookie reader。共同 Host 須保留 07 Bearer／Cookie／CSRF 及 11 CaseAccess，不能只換 API URL。
- 固定來源：[CasesController](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/d268b7d70be33e04d44bdfa044b13339448815f6/EarlyInterventionCare.Api/Controllers/CasesController.cs)、[CaseAccessService](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/d268b7d70be33e04d44bdfa044b13339448815f6/EarlyInterventionCare.Api/Services/Cases/CaseAccessService.cs)、[CaseContracts](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/d268b7d70be33e04d44bdfa044b13339448815f6/EarlyInterventionCare.Api/DTOs/Cases/CaseContracts.cs)。

### 16 Task／Completion：3d5b01c（固定原始碼核對，非執行驗收）

- GET `/api/cases/{caseId:guid}/tasks`：所有輪次 Task 直接陣列；空資料 []；不是有效輪次 API。
- Task 13 欄：taskId、caseId、questionnaireId、questionnaireVersionId（Guid）；questionnaireTitle、versionNumber、respondentRole、taskStatus（string）；assignmentRound（uint）、isRequired（bool）；createdAtUtc／updatedAtUtc（DateTime）、submittedAtUtc（DateTime?）。
- 四狀態：PENDING／IN_PROGRESS／SUBMITTED／CANCELLED。未知值安全拒絕；取消後 submittedAtUtc 是否保留等跨欄規則待 16 ACK。
- GET `/api/cases/{caseId:guid}/tasks/completion`：caseId（Guid）；totalTaskCount、requiredTaskCount、requiredSubmittedCount、requiredPendingCount、requiredInProgressCount（int）；allRequiredSubmitted（bool）。
- Completion 是**跨所有輪次的基本統計**。必要 CANCELLED 留在 required，非 SUBMITTED，不計入三個提交／待填／進行中子計數；三數和可小於 required。零必要為 false。這不是核定取消政策、可信有效輪次完成度、評估完成或結案。
- 現行 Gate：ACTIVE 帳號、ADMIN／CASE_MANAGER、Development 固定 CaseId `10000000-0000-4000-8000-000000000001`。MEDICAL_STAFF 被拒絕；非 Development／非固定 Case 為 403 CASE_ACCESS_NOT_INTEGRATED。不可換環境名稱或繞過限制過測。
- questionnaire.task.read＋可信 Case Scope **尚未接入兩個 GET**。通過 Gate 後查無 Case 為 404；已知 DB／EF／Timeout 映射 503；完整 HTTP／例外契約待驗收。GET 接線不得連帶放寬 POST Assign。
- 固定來源：[QuestionnairesController](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/3d5b01cb221bda9b54e9c04a4b8ca6332233a164/EarlyInterventionCare.Api/Controllers/QuestionnairesController.cs)、[QuestionnaireContracts](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/3d5b01cb221bda9b54e9c04a4b8ca6332233a164/EarlyInterventionCare.Api/DTOs/Questionnaires/QuestionnaireContracts.cs)、[QuestionnaireService](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/3d5b01cb221bda9b54e9c04a4b8ca6332233a164/EarlyInterventionCare.Api/Services/Questionnaires/QuestionnaireService.cs)。

## 3. 已執行的支援性測試（不等於正式整合驗收）

以下為本工作對話中 07 先前實際執行，本輪只整理。測試檔存在不等於本輪重跑。

|證據編號／類型|固定版本／執行時點|實際結果／狀態|來源與限制|
|---|---|---|---|
|E01 前端／Contract 回歸|07-06 修改及提交前重跑；同內容固定於 0c91baa|257 PASS、0 FAIL、0 跳過；PASS|[07-06 紀錄](07-06-medical-static-frontend.md)，第 8 節 15 檔指令；54 項 Auth／Client、37 項 Package／Task 是子集合，不另加總|
|E02 Case Consumer|07-04 修改完成，固定於 56eb9f2|Case／Client／Frontend 59 PASS，其中 Case Contract 20；當時完整回歸 244 PASS；PASS|[07-04](07-04-case-get-contract.md)、tests/MedicalCaseGetContract.test.cjs；Fake／Contract，非 CaseAccess 真實資料驗收|
|E03 Task Consumer|07-05 修改完成，固定於 fdeeb8c|Task 29 PASS；當時完整回歸 252 PASS；PASS|[07-05](07-05-medical-task-read.md)、tests/MedicalTaskFrontend.test.cjs；取消／零必要／跨輪次／Gate／不回 Demo，不是執行 16 producer|
|E04 Auth Service 模組隔離|較早 b9f1133 工作目錄檢查；非 0c91baa 重跑|StaffAuthenticationAcceptanceHarness 160 PASS；PASS|tests/StaffAuthenticationAcceptanceHarness.cs；真 AuthenticationService／BCrypt／JWT，EF 在 Open 前攔截、synthetic SQL reader，無 MySQL／瀏覽器|
|E05 Cookie／CSRF 離線|較早 b9f1133 工作目錄及 07-04 重跑；非本輪|StaffSessionHarness 29 PASS；PASS|tests/StaffSessionHarness.cs；Fake Authentication／Antiforgery，Cookie／Bearer 優先／期限／Origin／CSRF／logout，不證明共同 Host 可用|
|E06 RBAC loopback HTTP|較早 b9f1133 工作目錄；非 0c91baa 重跑|RbacHttpHarness 19 PASS；PASS|tests/RbacHttpHarness.cs；真 HTTP／JWT／授權 pipeline、測試路由、禁止網路的 SQL 替身；不是正式 Case／Task|
|E07 醫療 RBAC loopback HTTP|較早 b9f1133 工作目錄；非 0c91baa 重跑|MedicalRbacHttpHarness 17 PASS；PASS|tests/MedicalRbacHttpHarness.cs；真 CaseStatus／History Controller；身分／Permission／Scope／Store 是 Fake，不是 Case／Task 授權接線|
|E08 靜態包／Fake HTTP 瀏覽器|07-06 提交前工作目錄，產品修改後固定於 0c91baa|54 資源 manifest hash 一致；六段 API 導覽及 401 返回登入通過；PASS|[07-06](07-06-medical-static-frontend.md)、tests/MedicalFrontendPackagePreview.cjs --require-login；1440×900 無文件橫向溢出，已還原 viewport／停止 Host；測試 Cookie，非正式 JWT／RBAC／MySQL|
|E09 語法／差異檢查|07-06 提交前|9 個變更 JS／CJS 語法、diff／cached check 通過；PASS|07-06 提交前結果／紀錄；本輪未重跑 JS|
|E10 API Release Build|07-04 工作目錄；非本輪／非 0c91baa Build|替代輸出目錄建置 0 警告／0 錯誤；PASS（限編譯）|原輸出曾因既有 .NET Host 鎖 DLL 而 MSB3027／MSB3021；改新 TEMP OutputPath 成功，未停他人程序。SDK 亦編譯未追蹤 API C#，非乾淨固定 HEAD 證據，不代表 Workflow 已交付|

以上數字跨時間且重疊，不提供跨表加總。E04／E06／E07 的歷史執行亦記在既有 integration-manifest.md 工作目錄增補；該文件有未提交內容，本輪不修改、不冒充固定 Commit 證據。

E08 實際包（repo 外，本輪未重建）：

```text
C:/Users/Sy/.codex/visualizations/2026/10/11/01a12c0a-e6a8-7820-8ca5-20f95cb619ff/medical-07-06-afbcd0053df24403b0de64ae2984feb9
```

當時由工作目錄產生並檢查 manifest，不宣稱從乾淨 0c91baa checkout 重建。

### 16 外部回報（不是 07 執行）

|來源|固定文件記載|證據界線|
|---|---|---|
|3d5b01c [MedicalReadContract README](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/3d5b01cb221bda9b54e9c04a4b8ca6332233a164/tests/MedicalReadContract/README.md)|46 PASS／0 FAIL／5 BLOCKED，退出碼 **2**|先前唯讀核對；直接呼叫 Controller、EF／Pomelo SQL 翻譯與合成 ADO 物化，無完整 ASP.NET HTTP／JWT Cookie／MySQL 引擎驗收。07 未執行此 Harness，不將整組記為正式 PASS，不併入 E01|

## 4. 07-06 部署驗收分層

|層級|狀態|證據／停止點|
|---|---|---|
|A 靜態前端獨立啟動|PASS（歷史）|E08／發布測試；54 資源，--case-source api 有效，來源設定不變|
|B 獨立前端呼叫測試替身|PASS（歷史）|E01、E08；Fake HTTP 六段導覽；替身不進發布包|
|C 同源 HTTPS 與正式 ASP.NET API 通訊|BLOCKED|共同 Host／DI／Producer／Scope 未整合；loopback Fake HTTP 不能代替|
|D 隔離 MySQL 真實資料驗收|BLOCKED|需 C＋核准隔離庫、相容 Schema、虛構帳號／個案／Task|

tools/medical-frontend/server.cjs 無 TLS／Proxy，API 路徑回 503 API_NOT_CONNECTED，不能正式登入。優先使用共同同源 ASP.NET HTTPS Host；若需代理，須同一外部 origin 代理所有 auth／Case／Task，保留 HTTPS、Origin、Cookie／Set-Cookie、CSRF、no-store。prefix 不自動產生 Proxy，不放寬 CORS／Cookie。

## 5. 正式共同 HTTPS／隔離 MySQL 驗收表

以下 12 項**均未執行正式驗收**。固定版本欄是目標基準，不表示已整合可執行；開始前另填共同整合完整 SHA、部署時間及環境核准紀錄。

|測試編號|測試情境|對應模組|固定版本（短 SHA）|預期結果|實際結果|狀態|測試證據|
|---|---|---|---|---|---|---|---|
|MED-01|CASE_MANAGER 登入|07 Authentication|07 0c91baa|核准虛構 ACTIVE 帳號／院所，正確單位及密碼；staff-session 200、JSON 無憑證、安全 Cookie；錯密碼拒絕|未執行，缺共同 HTTPS／隔離帳號／資料庫驗收|BLOCKED|E04、E05 僅離線支援；無正式 HTTP 記錄|
|MED-02|MEDICAL_STAFF 登入|07 Authentication|07 0c91baa|同 MED-01；合法登入不代表有 Task 讀取權|未執行，帳號／環境未確認；16 Task Gate 另拒絕此角色|BLOCKED|E04、E05；不推定已授 questionnaire.task.read|
|MED-03|GET /api/auth/me|07 身分／前端|07 0c91baa|Cookie 有效 200、目前 userId／角色／permissions；無效／過期 401，清資料、返回 API 登入；跨頁／重整維持安全身分檢查|未執行共同 Host，無真 Secure Cookie 跨頁證據|BLOCKED|E01、E05、E08 是替身／離線|
|MED-04|同院所 Case 列表／詳細|07 Consumer＋11 CaseAccess|07 0c91baa；11 d268b7d|列表 200 陣列、GUID 詳細 200；可信 ACTIVE User／Role／Organization 限 Scope；不依賴 totalCount|未執行，缺整合 Case GET／模型／DI|BLOCKED|E02、07-04 固定原始碼；無共同 HTTP／MySQL 證據|
|MED-05|跨院所 Case 拒絕|11 CaseAccess＋07|07 0c91baa；11 d268b7d|列表不出現；直接詳細 404 CASE_NOT_FOUND；不洩漏名稱／Task／統計|未執行，缺兩院所虛構資料及共同 Scope|BLOCKED|固定 CaseAccess 原始碼；E07 Fake Scope 不能替代|
|MED-06|空 Case 列表|11 Producer＋07 Consumer|07 0c91baa；11 d268b7d|已授權但無可讀資料為 200 []，空狀態；滿頁後空頁可返回，無假總數／Demo|未執行正式空資料帳號情境|BLOCKED|E01、E02、E08 有合成空資料，無正式資料範圍證據|
|MED-07|Task GET|16 GET／Scope＋07|07 0c91baa；16 3d5b01c；11 d268b7d|核定 questionnaire.task.read＋Case Scope 才 200；Task 屬指定 Case、四狀態／nullable 安全，無任務 []|未執行，Development 固定 Case／角色 Gate，Permission／Scope 未接|BLOCKED|E03；16 文件回報不等於此項 HTTP PASS|
|MED-08|Completion GET|16 基本統計＋07|07 0c91baa；16 3d5b01c|同 Task 授權；七欄原值、跨輪次；必要取消留分母、零必要 false；不宣稱有效輪次完成／結案|未執行，同 MED-07 且缺核准隔離 Task 資料|BLOCKED|E03、固定 QuestionnaireService；無共同 HTTP／MySQL 證據|
|MED-09|缺 Task Permission 拒絕|16 GET 授權＋07|07 0c91baa；16 3d5b01c|接線後已登入且同院所但缺 questionnaire.task.read 仍拒絕（提案 403，待 ACK），無 Task／統計|未執行，兩 GET 尚未檢查此 Permission；其他 Gate 的 403 不算此項通過|BLOCKED|E06／E07 只驗各自路由／替身，缺正式 GET 接線|
|MED-10|CSRF 驗證|07 Middleware／logout|07 0c91baa|csrf 取得 request token；可信 Origin＋X-EarlyCare-CSRF 才允許 logout；缺／錯 Token、錯／缺 Origin 拒絕；無效 Authorization Header 不繞過 Cookie 防護|未執行真 HTTPS，不用 Fake Token 代驗真 Antiforgery|BLOCKED|E05、E01、E08；正式以 logout 測試，不呼叫未核准業務寫入|
|MED-11|登出|07 Cookie／前端|07 0c91baa|CSRF 保護 POST logout 204、Cookie 清除；me／受保護 GET 拒絕，再開頁返回 API 登入；不宣稱 JWT 撤銷|未執行共同 Host 真 Cookie 登出|BLOCKED|E05、E08 僅離線／Fake Session|
|MED-12|API 錯誤不回退 Demo|07 Client／頁面|07 0c91baa＋共同 producer 待固定|401／403／404／503、斷線／錯 JSON 清舊資料或提示；無 Demo 成功，401 保留 API 模式返回登入|正式 Host 未執行；合成錯誤 PASS 不升格|BLOCKED|E01、E02、E03、E08；需核准可控錯誤方式，不破壞 DB|

正式結果：**PASS 0／FAIL 0／BLOCKED 12／NOT RUN 0**。無 FAIL 證據不代表系統可用。先前 500／503 缺當次去識別 HTTP 及伺服器例外紀錄，不能指定根因或宣告結案。

每項正式執行須補：共同完整 SHA、三方 pin、時間／操作者、非機密 origin、環境核准、虛構角色／資料代號、method／path／status、預期與實際、去識別截圖／輸出位置。不得附密碼、完整連線字串、JWT、Cookie、CSRF Token 或未遮蔽 HAR。缺證據保持未取得，不造 log。

## 6. 阻塞、環境觀察及待交付證據

|編號／優先度|缺口|最短解法／主責|
|---|---|---|
|B01／P0|共同 Host 未整合、無共同 SHA|07 協調；11 提供 Case Producer／CaseAccess／DI，16 提供 Task 接線；共同 review 後新整合 pin，保留 Cookie reader／CSRF 及 CaseStatus／History DenyAll|
|B02／P0|11／16 完整 Git 物件尚未取得|主責提供可取得固定版本／完整交付清單，經授權另行取得；本輪不 Fetch，不以原始碼片段冒充完整 Git 驗證|
|B03／P0|Task GET Permission／Scope 未接；MEDICAL_STAFF 角色 Gate 不符目標|16 提供 GET 專用接線／新 pin／HTTP 拒絕測試；11 ACK Case Scope；07 review Cookie／Consumer；不授權、不直接移除 Development 限制，不連帶放寬 Assign|
|B04／P0|共用 DbContext／Schema／Migration 尚未統整|16 共用模型主責與 11 核對 Case／Guardian／User／History、snapshot、已套用版本；model／snapshot 差異不因 Build 消失；本輪不建立或執行 Migration|
|B05／P0|隔離 MySQL／虛構帳號／資料未確認|環境主責交核准隔離證據、Schema 唯讀報告、兩角色／有無 Permission／同跨院所／空列表／Task 資料代號；密碼另走安全管道，本輪不登入／Seed|
|B06／P0|醫療登入真 HTTP／MySQL 未通過|B01–B05 齊備後 07 先驗 MED-01～03；500／503 保留去識別時間、狀態、例外分類，不把故障當空資料成功|
|B07／P1|取消後 submittedAtUtc、拒絕碼與合法隔離 HTTPS Gate 待 ACK|11／16 明確回覆；不可繞過 IsolatedTest／Development Gate，不改跨輪次 Completion 成 Workflow 決策|

先前 07-03 唯讀觀察（2026-11-08；非本輪連線測試）：

- launch profile https 為 Development，預期 https://localhost:7127；當時 localhost 開發憑證信任檢查成功。
- 當時必要 JWT／ConnectionStrings User Secrets、可信 localhost Origin 有設定，未輸出值；這不證明帳密正確／Host 正在運行。
- 非機密 DB 目標為 127.0.0.1:3306／earlycare_dev，與先前 Workbench 13306 不同。當時 7127／5177／44384／3306／13306／33326／5186 未觀察到 listener；僅時間點觀察，不能當作現在仍停止或遠端 DB 故障證據。
- localhost 可能 Tunnel 到共用雲端；dev 名稱、設定載入或 TCP 可連均不證明核准隔離。未確認前不做登入／DB 測試。
- 未重現 500／503、沒有可核對的當次例外鏈；須分層確認設定、listener、DB 認證／Schema／逾時等，不指定單一根因。

仍需交付：

- **11**：完整固定 Git 版本與 CaseAccess／DI 清單；同院所列表／詳細、跨院所隱藏／404、停用帳號／院所、空列表的實際測試來源及去識別結果；模型需求、Scope／例外 ACK。
- **16**：完整固定 Git 版本；GET 與 Assign 分離的 Permission／Scope 新 pin、角色核定、缺權限／跨院所 HTTP 證據；隔離 Host 合法 Gate、Schema／Migration 核對、虛構資料清單、Completion 七欄及取消時間 ACK。46／0／5 模組報告不能替代。
- **三方／環境主責**：共同 HTTPS SHA、安全設定存在性、核准隔離庫證據、無秘密的角色／Permission／資料代號對照。

## 7. 11/09 正式驗收順序（前置條件完成後）

1. 核准 B01–B05，記錄共同 SHA、同源 HTTPS origin、Schema 與虛構資料代號；缺項維持 BLOCKED。
2. 無登入先測 me／Case／Task GET 拒絕，確認沒有 Demo 資料及保存實際狀態碼。
3. 兩個獨立瀏覽器 Session 分別跑 MED-01／02，再跑 MED-03 的 me／跨頁／重整與核准的過期情境；不推定 MEDICAL_STAFF 有 Task 權限。
4. MED-04～06：同院所列表／GUID 詳細／分頁／空列表、跨院所隱藏／404。Case Scope 確認後才讀 Task。
5. MED-07～09：有 Permission 同院所 Task／Completion、空 Task、四狀態、跨輪次／取消／零必要；缺 Permission、跨院所、未知 Case 拒絕。不讀答案、不擴權。
6. MED-10：logout 缺／錯 Token、錯／缺 Origin 應拒絕且仍登入；再正確 CSRF 跑 MED-11 登出，後續 GET 拒絕。不呼叫 Case／問卷業務寫入。
7. MED-12：核准且可控的錯誤情境，驗清舊資料、保留 API 模式、不回 Demo；不破壞共用資料庫製造 503。
8. 依真實輸出填結果與證據位置；必要案例全部通過才簽最小整合，不將 BLOCKED／替身改填 PASS。

正式指派、匯出、通知、進階 Dashboard、SessionVersion 新整合、Workflow 自動轉態延後；未追蹤 Workflow 不加入本交付。

## 8. 支援性測試重現（本輪未執行）

從 repo 根目錄執行；只重現支援性測試，不構成正式驗收：

```powershell
$medicalTests = @(
  'tests/MedicalFrontendPackage.test.cjs',
  'tests/MedicalTaskFrontend.test.cjs',
  'tests/MedicalApiClient.test.cjs',
  'tests/MedicalCaseFrontend.test.cjs',
  'tests/MedicalCaseGetContract.test.cjs',
  'tests/StaffAuthenticationFrontend.test.cjs',
  'tests/StaffPermissionPresentation.test.cjs',
  'tests/StaffLoginEntry.test.cjs',
  'tests/MedicalDemoFixtures.test.cjs',
  'tests/MedicalDemoEntry.test.cjs',
  'tests/ResponseDetailFrontend.test.cjs',
  'tests/SharedFrontendDemo.test.cjs',
  'tests/TeacherDraftClient.test.cjs',
  'tests/MedicalV53Ui.test.cjs',
  'tests/MedicalDashboardReminders.test.cjs'
)
node --test --test-reporter=tap @medicalTests
# 數量／結果依當次輸出，勿把歷史 257 填成新執行結果。

dotnet run --project tests/StaffAuthenticationAcceptanceHarness.csproj --no-restore -c Release -p:UseAppHost=false
dotnet run --project tests/StaffSessionHarness.csproj --no-restore -c Release -p:UseAppHost=false
dotnet run --project tests/RbacHttpHarness.csproj --no-restore -c Release -p:UseAppHost=false
dotnet run --project tests/MedicalRbacHttpHarness.csproj --no-restore -c Release -p:UseAppHost=false
```

Harness 僅用測試身分／SQL／Scope 替身，不載入正式測試帳密；不得把 Fake 註冊到正式 Program。缺依賴或執行失敗須保留實際結果，不沿用歷史 PASS。

## 9. 文件提交建議

可先獨立 Commit 本文件，保存**尚未通過驗收與阻塞的紀錄**，不必等正式驗收才存檔；不得描述成整合通過。提交前只精確暫存本文件並審查 staged diff，不加入既有 manifest、07-01、SQL 或 Workflow。本輪不暫存／Commit／Push。

建議訊息：`docs(medical): record V5.3 minimum acceptance evidence and blockers`。
