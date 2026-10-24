# V5.3 07-02：醫療共用 API Client 選擇性整合

日期：2026-10-24。基準：`feature/medical-backend` / `100cb858bf740decdbe774149a2702150a1163f8`。

## 範圍與來源

選擇性重用隔離工作目錄 `C:/Users/Sy/.codex/visualizations/2026/10/20/01a15876-64e4-7831-a3c8-ab0cc0f38890/medical-v53` 的 `medical-api.js` 傳輸、錯誤與 Case/Task DTO 驗證設計；不是合併隔離分支。隔離成果本身未改動。

不帶入該版的 login/me/logout、獨立 medical-login/medical 頁、ReadModel、Fixture server 或發布工具，避免出現第二個 Session owner。本次沒有替換 Dashboard/Case/答案資料來源或改變畫面布局；七個醫療 HTML 只增加依賴腳本與 cache 版本。

## 分工

|程式|責任|
|---|---|
|`js/medical-config.js`|唯一醫療正式 API Base URL 與傳輸 timeout 設定，預設同源根路徑；不存秘密|
|`MedicalApi.createTransport()`|同源/API 路徑檢查，統一 Accept、Cookie、no-store、禁止 redirect、網路/逾時錯誤；回傳原始 Response，保留既有 Session 邏輯|
|`staff-login.js`|仍是既有醫療登入表單 owner；使用共用傳輸，仍先檢查 CSRF（僅匿名 401 可略過），POST staff-session|
|`staff-auth.js`|仍是唯一工作區 Session/permission owner；me、cookieFetch、CSRF、401 導向、permission 失效、登出、bfcache；只更換底層傳輸|
|`MedicalApi.createClient()`|唯讀 Case/Task consumer；預設委派既有 StaffAuth.cookieFetch，使用安全 JSON/錯誤/DTO 檢查；不提供另一套登入/登出/me|
|`medical-demo-client.js`|保持原有專用 `/api/medical-demo` 資料來源、路由、認證與 Development 邊界；不改成正式 API 備援|

後端認證、JWT、Cookie、CSRF middleware、RBAC、SessionVersion、DenyAll、Program.cs 均未改動。`staff-auth` 既有「權限格式不合法時不啟用操作」行為保持不變，沒有搬入隔離版較嚴格的 userDto 來偷偷變更登入契約。

## 設定與使用

依序載入 `medical-config.js`、`medical-api.js`、`staff-auth.js`。登入頁最後一個改為既有 `staff-login.js`。

```js
// 部署時修改此靜態公開設定；不是網址 query/localStorage 控制項。
window.MedicalApiConfig = Object.freeze({ apiBaseUrl: "", timeoutMs: 15000 });
// 已驗證 Session 的工作區，後續 Case/Task 頁才使用：
const medical = window.MedicalApi.createClient();
const cases = await medical.cases(1, 10);
const detail = await medical.caseDetail(cases[0].caseId);
const tasks = await medical.tasks(detail.caseId);
```

`apiBaseUrl` 是同源 host/path prefix，不含 `/api` 尾碼，例如 `/gateway` 會使 auth 與 Case/Task 都呼叫 `/gateway/api/...`。需由部署 Host 提供該 prefix；本批不建立 reverse proxy。Demo 路徑不受此正式 API 設定影響。

跨來源、URL 帳密、query/hash、含編碼的 prefix、network-path prefix 會在送出前被拒絕。API 呼叫只接受固定 `/api/...` 路徑，拒絕 traversal 與 `/api/medical-demo`。Cookie fetch 對 unsafe request 先驗路徑再取得 CSRF，避免錯誤目標觸發不必要憑證請求。

傳輸強制 `credentials: same-origin`、`cache: no-store`、`redirect: error`。timeout 涵蓋 fetch 到收到 Response，不宣稱提供整個串流 body 的下載 deadline。HTTPS/可信 Origin/__Host Cookie/CSRF 限制仍由既有正式後端執行，不新增跨站登入或 CORS 放行。

## 已具備的 consumer 方法與尚缺的 producer

|方法|路由|目前 producer 狀態|
|---|---|---|
|`cases(page,pageSize,signal)`|GET `/api/cases?page=...&pageSize=...`|目前分支未提供；本機 11 分支 `505224ab...` 有實作，待整合|
|`caseDetail(caseId,signal)`|GET `/api/cases/{caseId}`|同上，不重寫 11 Case Service|
|`tasks(caseId,signal)`|GET `/api/cases/{caseId}/tasks`|目前 Controller 有路由，但限定 Development 固定 case，MEDICAL_STAFF 仍被拒絕，不能算正式個案已接通|

`StaffAuth` 的 auth 路由仍為 GET me/csrf、POST logout，登入仍為 POST staff-session；不改成通用 `/api/auth/login` 或在 JS 保存 JWT。

Case 分頁基準為 11 現有陣列 DTO，沒有 total/query/statusFilter。Client 只取 caseId/caseCode/childName/caseStatus/updatedAtUtc；不回傳 nationalId/address。Task 使用目前 `QuestionnaireContracts.cs` 的 CaseTaskResponse，剔除額外答案欄位；檢查 caseId、UUID、版本、角色、輪次、狀態、UTC 時間及提交時間的一致性。只有 SUBMITTED 是提交，不等於評估完成/結案。

不呼叫目前跨輪次且包含取消任務的 completion API，也不新增正式答案/匯出或 Task 寫入方法。待 11 確認 DTO/分頁/院所 scope 與固定版本；待 16 確認醫療 Task 讀取 policy/Case scope、狀態/時間不變量、當輪 completion 與後續結果 DTO。organizationId 不是假設已存在的公開 DTO 欄位。

## 錯誤、資料隔離與消費端義務

400/401/403/404/409/429/503 與其他 HTTP 失敗一律 reject；JSON、空值、HTML 錯誤保留 status，只顯示固定訊息，不回顯後端 message/SQL/HTML。200 HTML/null/錯 DTO 為 INVALID_RESPONSE；網路與 timeout 為 NETWORK_ERROR/REQUEST_TIMEOUT；取消保留 AbortError。沒有自動 retry。

Client 無資料快取、不存憑證/答案、不載入 SharedDemo、沒有 Demo fallback。401 仍由 StaffAuth 清除 protected DOM 並導向登入；非成功 API 仍使現有權限失效。Client 不假裝替所有舊頁清資料：未來頁面接線時必須在載入/錯誤/取消時清除舊 Case/Task，並防止晚到回應覆蓋新選擇；本批尚未移入隔離 ReadModel。

原有 Demo 頁仍是 Demo，並未因載入 Client 變成真實 Case/Task。正式 producer 缺少時只能報錯，不可顯示現有 Demo 作成功回應。沒有修改家長/教師頁或共用問卷交易。

## 驗證

新增 `tests/MedicalApiClient.test.cjs` 使用獨立 fetch/DOM adapter 與實際 staff-auth 程式，測試 transport、Case/Task 契約、錯誤、CSRF、權限失效與腳本載入順序；沒有啟動正式 Program 或連接資料庫。既有三組登入/權限/入口測試調整必要的模組載入，保留原有測試斷言與行為。

執行指令（Repository 根目錄）：

```powershell
node --test tests/MedicalApiClient.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/MedicalDemoFixtures.test.cjs tests/MedicalDemoEntry.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs

$clientHarnesses = @('StaffAuthenticationAcceptanceHarness', 'StaffSessionHarness', 'SessionVersionHarness', 'RbacOfflineHarness', 'PermissionServiceOfflineHarness', 'RbacPolicyHarness', 'RbacHttpHarness', 'MedicalRbacHttpHarness', 'MedicalDemoHttpHarness')
foreach ($clientHarness in $clientHarnesses) {
    dotnet run --project "tests/$clientHarness.csproj" --no-restore -p:NuGetAudit=false -p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw "$clientHarness failed" }
}
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:UseAppHost=false
git diff --check
```

本批結果：Node 完整回歸 161 項（新 Client 23 項、既有/擴充前端 138 項）；AuthenticationService 160、StaffSession 29、SessionVersion 122、RBAC offline 22、PermissionService 34、RBAC policy 43、RBAC HTTP 19、Medical RBAC HTTP 17、Medical Demo HTTP 68；均通過。API Release Build 0 警告/0 錯誤。首次沙箱內入口 HTTP 測試遭 loopback EACCES；經核准在本機隔離範圍重跑通過，沒有略過測試。

沒有更動可見布局，未重做瀏覽器視覺驗收；腳本順序、實際 Session owner、登入 prefix/CSRF/錯誤及 API consumer 已用 VM/fetch adapter 回歸。離線/loopback 測試不等於真實帳號、跨網路或 MySQL 驗收。正式驗收需 11 Case 與 16 Task/scope 固定版、受控 HTTPS 環境及全虛構資料。
