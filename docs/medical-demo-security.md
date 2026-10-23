# 醫療端唯讀 Demo：Development 與 Session 閘門

## 服務端閘門建立時的檔案範圍（前輪）

- 新增 `EarlyInterventionCare.Api/Development/MedicalDemoEndpoints.cs`、`MedicalDemoFixture.json`；增量修改 `Program.cs` 與 API csproj。
- 移除 `wwwroot/js/medical-demo-fixtures.js`，新增 `wwwroot/js/medical-demo-client.js`；修改 `system-demo-ui.js`、`response-detail.js`。
- 修改 `staff-dashboard.html`、`cases.html`、`case-detail.html`、`case-detail-empty.html`、`response-detail.html` 與 `css/staff-dashboard.css`。
- 新增 `MedicalDemoHttpHarness.cs/.csproj`、`MedicalDemoEntry.test.cjs`、`MedicalDemoPreviewServer.cjs`；更新 `MedicalDemoFixtures.test.cjs`、`ResponseDetailFrontend.test.cjs`。
- 新增本文件並更新 `medical-response-detail-ui.md` 的現況指引。其餘既存未提交的詳細頁 CSS、Workflow、SQL 等保留，不歸入本輪新增修改。

## 使用方式

醫療人員正常登入 → Dashboard →「個案管理」→ 既有「查看個案：王○安 DEMO-001」→「表單紀錄」→「查看原始答案」。Dashboard 與個案管理不提供額外 Demo 按鈕。

正常個案連結維持 `case-detail.html`，不需 `medicalDemo=1`。既有 StaffAuth 完成後，僅兩個固定 Demo case 的表單區向服務端 Demo API 查詢；DEMO-001 合併顯示伺服器核准的已完成教師 SNAP-IV，保留既有 localStorage 任務，DEMO-024 無合成問卷。其他 case 不發出 Demo 請求，不會因正式 API 失敗而補入 Demo。

清單與答案的取得仍由 ASP.NET 環境及目前醫療 Session 決定，不以 query 或前端角色授權。Production 的清單 404 只保留原有本機任務，不增加任何合成結果；403／503／網路及格式錯誤不補資料，401 沿用 StaffAuth 重新驗證登入。頁面離開、登入失效及延遲回應不會恢復舊答案。成功的合成紀錄清楚標示「Demo 模擬資料」。

新詳細連結為 `response-detail.html?taskId=medical-demo-snap-teacher-001-v1&caseId=DEMO-001&source=medical-demo`；返回 `case-detail.html#shared-tasks`，不需模式參數。舊連結如有 medicalDemo 參數，仍檢查唯一且值為 1，但它不是授權依據。

本輪僅修改五個醫療 HTML 的引用／移除按鈕、`medical-demo-client.js`、`system-demo-ui.js`、共用 CSS、前端測試與隔離 PreviewServer。服務端閘門、fixture、Program、Authentication、RBAC、DenyAll、MySQL、Draft／Submit 皆未再修改，因此本輪不需重啟 ASP.NET Core。新版引用版本為 `20261023-medical-case-demo2`，重新整理即可取得。

## 實際路由（全部 GET）

| 路由 | 內容 |
| --- | --- |
| `/api/medical-demo/availability` | 可用旗標與 Demo 標示，不含答案 |
| `/api/medical-demo/cases/DEMO-001/tasks` | 固定教師 SNAP-IV 已完成摘要，不含 draft／答案 |
| `/api/medical-demo/cases/DEMO-024/tasks` | 空陣列 |
| `/api/medical-demo/cases/DEMO-001/tasks/medical-demo-snap-teacher-001-v1` | 合成題目、26 題答案與補充觀察 |

程式位於 `Development/MedicalDemoEndpoints.cs`，由 Program.cs 增量呼叫 `UseMedicalDemoBoundary`（靜態檔案前）及 `MapMedicalDemoEndpoints`。僅 ASP.NET Core `IsDevelopment()` 成立才提供；不依 hostname、Origin、query、前端旗標或請求角色判斷環境。Production 在驗證身分／查詢帳號之前即回 404。

開發環境 API 使用原有認證 middleware 與 RequireAuthorization。單一有效 userId 必須通過既有 `IAuthenticationService.GetCurrentUserAsync`，重查 ACTIVE 帳號與目前角色，再沿用 `StaffSessionAuthorization.CanCreateStaffSession` 判斷醫療端資格。未登入／逾期／目前帳號無效回 401，PARENT／TEACHER／ADMIN 等非既有醫療端角色回 403，帳號查詢故障安全回 503。不採信前端角色或過期角色 claim，不新增正式 Permission 或 RolePermission 配置。

這是虛構展示資料的工作區資格檢查，不是正式個案權限，不能用來存取真實 Case、Task、Response。未修改正式 RBAC、DenyAll、AuthenticationService、JWT／Cookie、CSRF、SessionVersion、正式 Case API、問卷 Draft／Submit 或資料庫。SessionVersion 仍維持原本尚未啟用的核心狀態。

## 答案隔離與快取

- 原本的 `wwwroot/js/medical-demo-fixtures.js` 已移除。舊路徑（包含 query 版本）由閘門在任何環境回 404；不是只從 HTML 移除引用。
- 答案改存 `Development/MedicalDemoFixture.json`，作為 assembly EmbeddedResource，移出公開 wwwroot；不複製成公開 publish content。API 不接受任意檔案路徑或真實個案 ID，只提供兩個固定 Demo case。
- 公開 `medical-demo-client.js` 只有 transport／參數驗證／導覽，沒有合成題目或答案。HTML／JS／共用 CSS 引用有新版版本號，避免沿用舊的公開 fixture renderer。
- API 及舊靜態網址拒絕回應均 `Cache-Control: no-store`。前端 GET 使用 same-origin credentials、no-store，資料不寫 localStorage／sessionStorage／正式 Task／Response。不清除或覆寫原本瀏覽器任務。
- 已經在舊版本下載的公開合成答案，無法從使用者的既有快取或自行保存副本中追回；本次不清除使用者儲存。新版伺服器與新請求已拒絕舊路徑，這項限制不能宣稱為可抹除過去下載內容。
- 題目與合成選項模式沿用先前隔離 fixture。沒有正式計分來源，詳細頁仍一律顯示「尚未計分」，不計算總分／分量表或診斷。

## 驗收與限制

`MedicalDemoHttpHarness` 使用實際新路由、靜態 middleware 與 ASP.NET Development／Production 環境；只有身分處理器與目前帳號服務為明確隔離替身，不載入正式 Program、secrets、DbContext 或 MySQL。68 項通過：三路由的未登入／失效／停用帳號 401，非醫療角色 403，兩種醫療角色成功，角色改變重查、資料來源／26 題、空個案、跨個案／未知 task 404、503、無寫入接口，以及 Production 全部 404／不查帳號／靜態路徑無答案。

本輪前端 131 項通過（一般個案導覽、無旗標詳細頁、401／403／404／503 拒絕、失效及延遲回應），並重跑上述 68 項 Demo HTTP 安全測試通過。API Release Build 使用獨立輸出成功，0 警告、0 錯誤。前輪既有 AuthenticationService 160、StaffSession 29、SessionVersion 122、MedicalRbacHttpHarness 17 項通過紀錄保留；這些回歸本輪未改核心，未再重跑。全部為離線／隔離測試，不是 MySQL 資料寫入或真實帳號完整驗收。

```powershell
dotnet run --project tests/MedicalDemoHttpHarness.csproj -c Release -p:NuGetAudit=false -p:OutputPath=bin/MedicalDemoVerification/
node --test tests/MedicalDemoFixtures.test.cjs tests/MedicalDemoEntry.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/TeacherDraftClient.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj -c Release --no-restore
```

`MedicalDemoPreviewServer.cjs` 是瀏覽器專用合成身分 Host（不是 Production 授權實作），不連 MySQL。本輪使用 `--require-login` 啟動隔離登入表單，以程式中固定的非真實測試值驗收完整登入 → Dashboard → 個案管理 → DEMO-001 → 表單紀錄 → 原始答案流程。此模式只在測試來源設隔離 HttpOnly Cookie，不核發正式 JWT／Staff Cookie，不使用真實帳密，也不修改真實來源的 localStorage。

1440×900、390×844 已驗收無額外按鈕、無旗標導覽、完整 26 題、「尚未計分」、返回頁籤、DEMO-024 空狀態與 Production 零答案；文件無橫向溢出，手機底部返回／匯出可完整捲入可視區。viewport 已還原，測試服務已停止。ASP.NET 環境安全由 HTTP Harness 另外驗證。

本輪真實 7127 的既有 Session 已失效，開 Dashboard 導回登入。使用者選擇僅完成隔離驗收，故未操作真實帳號，也未宣稱本輪完成真實網站完整登入驗收。

前輪執行紀錄：依使用者授權，只重啟核對路徑的原專案程序 PID 33444；標準 Release Build 成功，0 警告、0 錯誤，新服務繼續監聽 7127／5177。無 Cookie 的實際 7127 請求：Demo API 401、舊 fixture／私有 JSON 靜態網址 404，均無答案。該輪的額外入口已於本輪移除。

未 Commit／Push／Merge；Workflow、SQL 及其他未完成檔案保留。建議 Commit：`feat(medical): gate read-only demo behind development staff sessions`。
