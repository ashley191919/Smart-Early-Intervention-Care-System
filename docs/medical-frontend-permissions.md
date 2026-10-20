# 醫療端前端 Permission 呈現（第四階段）

## 範圍與契約

本階段只新增瀏覽器操作呈現。正式後端 Permission Policy、RolePermission、資料範圍與 DenyAll 均未修改；未連接 MySQL。角色仍用於既有醫療工作區身分檢查，不能代替個別 Permission。

`StaffAuth.hasPermission(code)` 只接受目前 `AddFirstBatchPermissionPolicies` 的四個 code：`case.read`、`case.status.update`、`case.status_history.read`、`questionnaire.task.read`。它讀取閉包中的 `/api/auth/me` 結果，沒有從 event detail、角色、localStorage 或網址授予權限。權限陣列複製後凍結。

`staff-authenticated` 沿用現有成功事件；`staff-permissions-changed` 通知 pending、error、expired 狀態，供共用 renderer 與頁面清除尚未送出的狀態選擇。兩個事件都只促使重新讀取 `StaffAuth`，不直接使用事件內容作為權限來源。

`staff-permissions.js` 對 `[data-staff-permission]` 原生 control 設定 disabled / aria-disabled；`data-permission-mode="hide"` 可選擇隱藏入口。`data-permission-hint` 指向可及性說明元素。HTML 須先寫 disabled 或 hidden，防止腳本尚未執行時閃現可操作狀態。此 renderer 不負責業務驗證或解除其他業務限制，因此僅將由它單獨管理的 control 納入標記。

## 目前套用位置

- case-edit.html 的 `#status`：要求 `case.status.update`，缺權時保留原狀態並停用，顯示權限提示。
- case-edit.js 在權限失效及表單提交前恢復未授權的狀態選擇。此頁仍只操作既有虛構資料，沒有新增 fetch、真正狀態 API 請求或儲存成功行為。
- case-edit.html 加入既有登入狀態 guard；Dashboard 繼續使用相同 guard。共用 CSS 明確保證 hidden 的受保護區域不被 grid 樣式顯示。
- case.status_history.read 已由共用函式支援，隔離測試以歷程按鈕驗證顯示／隱藏。產品頁面尚無「個案狀態歷程」入口；現有「預約歷程」是另一個 Demo 功能，本次不套用錯誤的權限，也不新增無資料來源的按鈕。

沒有替新增個案、編輯其他個案欄位、整張表單的儲存按鈕、指派問卷、聯絡／預約／待辦或結果查看設定授權規則。它們仍屬既有 Demo，不能因畫面可操作而視為正式 API 可用。`case.create`、`case.update`、問卷指派／結果讀取的 code 與角色配置須由 16 號、老師及使用單位確認。

## 失敗處理

- `/api/auth/me` 未完成或重新查詢中：立即清除已快取的操作權限；control 不可操作。
- 401：清除受保護內容、權限並導回登入。
- 403、其他失敗、網路錯誤或身分回應錯誤：不授權操作；工作區 guard 依原有行為顯示錯誤。
- permissions 缺失、非陣列、混入非字串、空白或格式錯誤：整份清單拒絕；空陣列也沒有操作權限。
- `cookieFetch` 的 401、其他非成功回應或網路錯誤，以及 CSRF token 取得失敗，都停止沿用先前權限。錯誤 Response 保留給呼叫端處理；CSRF／same-origin 流程保留。
- pagehide 清除權限；從 bfcache 還原時重新查 `/api/auth/me`。舊的較慢回應不能覆蓋較新的失效狀態。

前端只能在重新驗證或收到 API 401 後得知 Session 已失效；不宣稱瀏覽器可在完全沒有網路請求時即時偵測後端撤權。每個正式 API 的最終授權仍由後端完成。

## 驗收方式

Node 測試：

```powershell
node --test tests/StaffPermissionPresentation.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/StaffLoginEntry.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release
```

瀏覽器使用 `node tests/StaffPermissionPreviewServer.cjs` 顯示的獨立 loopback 埠；**不可將此測試伺服器當作正式登入服務**。它只提供靜態檔案及合成 `/api/auth/me`，其他 API 維持拒絕，完全不啟動 API Program、讀取 secrets、發出有效憑證或連接資料庫。

在該埠開啟 `/case-edit.html?id=DEMO-001&permissionFixture=read`（歷程唯讀）或 `write`（兩種狀態 Permission）；還有 `pending`（延遲八秒）、`invalid`、`expired`、`forbidden`、`unavailable` 與未指定（空權限）情境。fixture 僅根據同來源 Referer 中的參數提供測試回應，產品程式不讀取該參數。

已在隔離瀏覽器檢查 1440×900 與 390×844：狀態欄位及提示、長表單捲動、儲存按鈕邊界、文件寬度沒有橫向溢出；桌面身分選單在可視區內。1440×900 編輯頁 document 為 1440×900，內容由 main 區捲動；手機使用頁面垂直捲動。pending 與 403 時受保護區域為 display:none，欄位保持 disabled。臨時 viewport 在驗收後重設。

這些是前端隔離驗收，不是實際帳號、正式 RolePermission、MySQL 或跨院所個案授權驗收。
