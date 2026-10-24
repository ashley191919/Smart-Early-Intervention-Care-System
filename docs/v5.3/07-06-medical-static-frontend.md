# V5.3 07-06：醫療端靜態發布與本機獨立執行

日期：2026-10-25。分支 `feature/medical-backend`，HEAD `44ec4e4713ae26a2d367d4e64335c949bddfe3ce`。發布內容取自目前工作目錄，包含尚未提交的 07-05 前端成果；不是只有此 HEAD 的乾淨版本。每個檔案以 manifest SHA-256 固定實際內容。

## 1. 隔離工具差異與保護範圍

已唯讀核對已存在的 `.codex/visualizations/2026/10/20/01a15876-64e4-7831-a3c8-ab0cc0f38890/medical-v53`（本機 worktree HEAD `7c811aa`）。使用者提供路徑中的 `Sy.codex` 實際解析為 `Sy/.codex`；未建立另一個同名目錄。

隔離工具使用舊 `medical.html`／`medical-login.html`、`MedicalConfig` 及另一套 process-local Fake 登入，與目前 `staff-login.html`、`MedicalApiConfig`、StaffAuth、完整 CaseDto／Task 不相容。因此只沿用明確 allowlist、全新輸出目錄、SHA-256 manifest 的發布方式；沒有合併 worktree、覆蓋前端或沿用舊登入。原先 14 個資源改為目前 54 個依賴。

本次只新增 6 個檔案：

- `tools/medical-frontend/assets.cjs`：12 頁及其資源 allowlist，含動態載入的 case-demo-data 與 07-05 Task 模組。
- `tools/medical-frontend/package.cjs`：`buildPackage`、設定驗證及 manifest，拒絕既有輸出目錄，預先確認來源並拒絕越界 symbolic link，不刪除或覆蓋檔案。
- `tools/medical-frontend/server.cjs`：`createStaticServer`，只綁 loopback、只服務 allowlist，無代理／Fake API／Cookie／CORS。
- `tests/MedicalFrontendPackage.test.cjs`：發布、內容、路徑、設定、HTTP 與隔離流程驗證。
- `tests/MedicalFrontendPackagePreview.cjs`：只在 tests 的明確 Fake Host，重用既有虛構 Case／Task／Development 問卷 fixture；不進發布包。
- 本文件。

沒有修改 wwwroot 或既有 07-05、Authentication、RBAC、Program、Entity、DbContext、Migration、Seed、Workflow、SessionVersion、DenyAll。保留原 ASP.NET Core wwwroot 執行方式。既有 Workflow／SQL／其他未追蹤檔案未動。

## 2. 產生與啟動發布包

需要本機 Node.js（本次用已安裝 runtime，無 npm install）。從 Repository 根目錄執行：

```powershell
$medicalOutput = Join-Path $env:TEMP ('medical-static-' + [guid]::NewGuid().ToString('N'))
node tools/medical-frontend/package.cjs --out "$medicalOutput"
node tools/medical-frontend/server.cjs --root "$medicalOutput" --port 5200
```

開啟 `http://127.0.0.1:5200/staff-login.html`；`/` 也是同一入口。Ctrl+C 停止。工具不會清理或覆蓋舊發布包；重新發布必須提供全新目錄。伺服器不開放目錄列出／任意 repo 檔案，GET／HEAD 以外靜態操作回 405。

同源 API prefix 可在產生時指定：

```powershell
node tools/medical-frontend/package.cjs --out C:/Temp/medical-prefix-NEW --api-base /gateway
```

仍須使用全新目錄。`/gateway` 只影響 API 路徑（例如 `/gateway/api/auth/me`），不自動建立代理或代表前端支援掛在 `/gateway/` 子路徑。不同 origin／`//host`／query／hash／編碼或 traversal prefix 均拒絕。

若只想服務目前 wwwroot，可以不帶 `--root`；這會保留來源原本設定，不會產生發布設定，且仍是無 API 的靜態模式。發布工具預設生成 `caseSource=api`；來源檔目前的 `caseSource=demo` 不受修改。

## 3. 發布包內容與可重現性

```text
release/
  *.html              12 個醫療頁面
  js/*.js             25 個目前醫療／必要共用腳本
  css/*.css           16 個目前樣式
  images/hospital-logo.png
  manifest.json       相對路徑、位元組數、SHA-256、非機密部署設定
```

共 54 個靜態資源，加 manifest 為 55 個檔案。HTML：staff-login、staff-dashboard、cases、case-detail、case-detail-empty、case-create、case-edit、form-assign、response-detail、staff-forgot-password、staff-reset-password、invitation-success。

完整清單以 `tools/medical-frontend/assets.cjs` 與輸出的 manifest 為準。相同來源及參數產生相同 manifest，不含時間戳、隨機 ID 或機器絕對路徑。除 `js/medical-config.js` 依發布參數生成外，其餘資源保持來源位元組，不替換較新醫療程式。

不包含 C#、appsettings／User Secrets／.env、Git、資料庫／SQL／Migration／Seed、tests、工具伺服器、node_modules、測試帳密、JWT 或受保護的 MedicalDemoFixture 原始答案 JSON。現有 `case-demo-data.js`／SharedDemo 是明確的合成展示資源，為保留既有 Demo 而保留，不能稱為正式資料或測試資料庫；正式 Case／Task 不會交給它們保存。沒有把受保護的 26 題答案移入公開 JS。

不包含家長／教師工作台及根 index；原登入的跨角色入口保留原碼，但本包不提供跨角色流程，`/?role=parent|teacher` 不等於已進入其他端。未確認的 Dashboard 統計、建立／編輯／指派、密碼重設與邀請頁仍是既有 UI／展示或各自尚待接線流程；打包不代表它們已完成正式 API。切勿把本包當成正式上線驗收通過。

本次已產生兩份實際包，位於：

```text
C:/Users/Sy/.codex/visualizations/2026/10/20/01a15876-64e4-7831-a3c8-ab0cc0f38890/07-06-medical-static-api
C:/Users/Sy/.codex/visualizations/2026/10/20/01a15876-64e4-7831-a3c8-ab0cc0f38890/07-06-medical-static-demo
```

第二包透過明確 `--case-source demo` 產生，僅為保留展示模式的獨立驗收；不因 API 失敗或 Production 環境自動切換。兩包都不含 Fake API。

## 4. API／HTTPS／Cookie／CSRF 界線

- 唯一設定是包內 `js/medical-config.js` 的 `window.MedicalApiConfig`：`apiBaseUrl`、`timeoutMs=15000`、`caseSource`。不使用舊 `MedicalConfig`、不從 localStorage 或網址拿 API origin，不放憑證。
- 保留 MedicalApi Transport 的同源檢查、`credentials=same-origin`、no-store、redirect error、timeout，以及 StaffAuth 的 Cookie Session／me／logout／CSRF／Permission 行為。
- 純靜態 HTTP server 沒有 API、TLS 或 reverse proxy。任何 `/api/*`／同源 prefix 下的 API 都回 503 `API_NOT_CONNECTED`，不查 DB、不轉送 Cookie、不核發 Session，也不回 Fake 成功。登入服務不可用會顯示錯誤，受保護頁不回退 Demo。
- 真正接線需要同一瀏覽器 origin（scheme／host／port）提供 **HTTPS 靜態資源與 API**；例如前端在 `https://host/`，反向代理將 `/api/*` 或 `/gateway/api/*` 對應至既有後端。因目前資源及導頁含根路徑，應在站台根提供資源，不能僅改 API prefix 就假定支援前端子目錄掛載。
- 反向代理需正確保留／處理外部 HTTPS、Host、Origin、Set-Cookie／Cookie、CSRF header；確保 auth POST／CSRF 路由均被同樣代理，非只代理 Case／Task。不記錄密碼、Cookie、token 或問卷答案，不快取 auth／個案／問卷回應。TLS termination／可信 proxy 設定須由後端主責核對後驗收，這次未改 Program 或 forwarded headers。
- 現有 `__Host-earlycare-staff`／`__Host-earlycare-csrf`、Secure、HttpOnly、SameSite=Strict、Path=/ 及 `X-EarlyCare-CSRF`／Origin 檢查不得為展示而放寬。前端另一個 port 就是另一個 origin；HTTP loopback 成功不證明正式 Secure Cookie 可用。
- 不新增跨來源 Cookie、CORS `*`、credentials 放寬、憑證忽略或允許陌生 Origin。公有雲跨站登入／雲端連地端 API 不屬本次完成範圍。

## 5. 明確 Fake Host（僅離線驗收）

```powershell
node tests/MedicalFrontendPackagePreview.cjs --root "$medicalOutput"
```

只綁 127.0.0.1 隨機 port，console 明確列 SYNTHETIC HTTP FIXTURE。使用合成 /me，不做真密碼登入或安全 Cookie 驗證。開 `/cases.html` 或 `/case-detail.html?caseId=20000000-0000-4000-8000-000000000001` 驗證 API 模式清單／遮罩／Task。

錯誤情境由測試 Host 明確指定：

```powershell
node tests/MedicalFrontendPackagePreview.cjs --root "$medicalOutput" --task-scenario unavailable
```

可選 empty／expired／forbidden／missing／unavailable／wrong-case／completion-unavailable。這不是產品開關；伺服器自己配置舊 Fixture 所需的情境，不移除發布 server 的 no-referrer 保護，也不透過產品 UI query 影響正式 API。

26 題 Demo 須另外明確產生 `--case-source demo` 包，再以測試 Host 啟動，從 `/case-detail.html` 的表單紀錄查看原始答案。純靜態 server 本身無法取得受保護 Demo API，這是預期安全界線；原 ASP.NET Development 執行仍須符合既有登入與 Development 閘門。

## 6. 驗證與限制

```powershell
node --test tests/MedicalFrontendPackage.test.cjs tests/MedicalTaskFrontend.test.cjs tests/MedicalApiClient.test.cjs tests/MedicalCaseFrontend.test.cjs tests/MedicalCaseGetContract.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/MedicalDemoFixtures.test.cjs tests/MedicalDemoEntry.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:UseAppHost=false
git diff --check
```

發布包測試 6 項，與現有回歸合計 220 項，沒有改 authentication、Cookie、CSRF、Permission 或問卷後端來讓測試通過。測試確認 allowlist／完整目錄內容、SHA-256、相同來源重現、拒絕覆蓋、設定拒絕外部 origin、HTML／CSS／動態 Case Demo 資源、HTTP GET／HEAD／405／404／503、無 CORS／Set-Cookie、Case／Task Fixture 及受保護 Demo 的 26 題。

最終執行結果：**220 通過、0 失敗、0 跳過**（6 項發布測試包含在內，非另加）。API Release Build 成功，**0 警告、0 錯誤**；`git diff --check` 通過，僅既有 LF→CRLF 提示。沒有 restore／安裝新依賴，沒有執行真 API Program 或 MySQL Migration。

瀏覽器直接使用輸出的包，而非來源 wwwroot：1440×900 登入按鈕與院徽正常、390×844 無文件橫向溢出；純靜態受保護頁在 auth 503 顯示錯誤，沒有展示個案；API 包 Fake Case／Task 可讀且遮罩；明確 Demo 包從紀錄顯示 26 題及 26 個答案項目。長清單／問卷允許垂直捲動；測完還原 viewport。

沙箱內伺服器曾被本機 socket 權限阻擋，未當成產品測試通過；經核准以沙箱外 loopback Host 重新驗證。沒有修改防火牆、瀏覽器安全限制、正式 Origin 或任何資料庫設定。

待整合：11 的固定 Case producer、16 正式 Task 資源授權／Development CaseId 限制與完成度語意、同源 HTTPS／反向代理、真 Session Cookie／CSRF、跨院所拒絕、全虛構資料 MySQL 保存／重啟後狀態查詢。Fake Host 與 HTTP 靜態包不能代替上述驗收。

本次未公網部署、未連 MySQL、未暫存／Commit／Push／Merge。07-05 尚未提交，07-06 可獨立提交工具與文件，但內容依賴 07-05 Task 資源，獨立分享時要先固定／帶入 07-05，不可聲稱任意舊 HEAD 均可打包。
