# V5.3 07-04：醫療 Case 查詢前端準備

日期：2026-10-24。基準 `feature/medical-backend` / `f630cdc682d570bc65b3b45159200ce147fcdd30`。

後續契約對接已更新為完整 11 欄位及 API 預設每頁 20 筆，見 [07-04-case-get-contract.md](07-04-case-get-contract.md)。以下保留為前端準備階段的歷史基準，不作最新 DTO 規格。

## 現有流程與本次範圍

原本 cases.js 內建 24 筆虛構個案，搜尋姓名/代碼、三種狀態篩選、優先程度再依日期排序、每頁 10 筆；只有 DEMO-001 和 DEMO-024 有對應詳細頁。詳細頁 HTML 是固定資料，case-detail.js 管理 Tabs、敏感值顯示及記憶體聯絡/預約/待辦。system-demo-ui 將 SharedDemo 與需登入的 Development Demo API 結果注入表單紀錄。

本次只準備唯讀消費端與畫面狀態。未整合 11 分支、未修改 Controller/DTO/Entity/Service、16 核心、認證/SessionVersion、DbContext/Migration/Seed、DenyAll、Draft/Submit 或既有 Workflow/SQL。沒有資料庫操作、Commit、Push、Merge。

## 模組與來源

|程式|責任|
|---|---|
|medical-api.js|沿用 07-02 HTTP Client 與 Case GET DTO；本批不變|
|medical-config.js|增加 caseSource，預設 demo；Base URL/timeout/同源安全限制不變|
|medical-case-model.js|路由來源選擇、API/Demo Adapter、Case ViewModel、載入/錯誤/空資料 state、過期回應防護、本頁篩選/排序|
|medical-case-source.js|在 Demo/Session 腳本前固定來源；API 模式清除靜態個案內容和 Demo 對話框，保留 API 返回路由|
|cases.js|清單 DOM、搜尋/篩選/排序、API 頁碼與 Demo 本機分頁；資料取得委派 Adapter|
|medical-case-detail.js|API 詳細頁渲染，沿用 Sidebar/Header/色系、摘要卡及三個 Tabs；缺少欄位顯示「未提供」|
|case-demo-data.js|搬出原有 24 筆遮罩個案清單；不是答案 Fixture，不含問卷題目/答案|
|case-detail.js/system-demo-ui.js/staff-header.js|僅對 API Case 模式排除舊 Demo 互動、表單注入與固定提醒；其他頁維持原行為|

正式模式的 Adapter 呼叫 `MedicalApi.createClient().cases(page,pageSize)` / `.caseDetail(caseId)`，Session 仍完全由 StaffAuth 管理。沒有第二套 fetch、沒有角色或 organizationId 授權來源、沒有儲存真實資料。未假設 case.read 前端顯示就等於後端資源授權。

## 明確選擇模式

- `cases.html`、`case-detail.html`、`case-detail-empty.html`：維持原 Demo。只有 Demo 清單路徑會動態載入 case-demo-data.js。
- `cases.html?source=api`：明確嘗試正式 GET 消費端；目前分支尚缺 Case producer，不能當成已接通。
- `case-detail.html?source=api&caseId=<UUID>`：API 詳細頁唯一建議路徑；列表連結與返回列表都保留 source=api。
- `medical-config.js` 的 caseSource 改成 api 可將 Case 頁的預設來源固定為 API；本次仍保留 demo 預設，避免影響既有展示。
- 任何 caseId、舊 id 參數、未知/重複 source 都不能退回靜態 Demo 個案。無效 ID/重複參數會拒絕；query 只選擇讀取來源，不授予任何權限。
- case-detail-empty.html 若明確帶 API source，使用同一 API renderer，不會繼續顯示 DEMO-024。正式流程不需要兩份詳細頁。

API 401/403/404/503、錯誤 DTO 或個案 ID 不一致均清空資料，不載入另一個 Adapter。Session/權限失效時先清資料；pagehide 作廢請求；較舊回應不可覆蓋新選擇。重試若 Session owner 已清除目前使用者，會先透過原有 requireStaffSession 重驗，不自行發明登入。

API 模式不顯示 Demo 表單紀錄/答案連結、固定提醒或聯絡/預約/待辦操作，也不連到 Demo 新增/編輯/指派入口。這是未接寫入功能的邊界，不是新訂角色授權規則。問卷原始答案仍只由原有受保護 Development 路由提供，未搬回公開靜態 JS。SharedDemo 的既有其他角色行為未改動。

## 暫定消費端契約（待 11 確認）

沿用 07-02 Client 的必要子集合，沒有修改 11 的 DTO：

```json
{
  "caseId": "20000000-0000-4000-8000-000000000001",
  "caseCode": "CASE-TEST-001",
  "childName": "測試幼兒1",
  "caseStatus": "NEW",
  "updatedAtUtc": "2026-10-24T01:00:00Z"
}
```

清單是上述物件的陣列；詳細是同一形狀單一物件，caseId 必須符合路由。案例/日期全部虛構。時間依現有 Client 要求使用 UTC Z，UI 轉 Asia/Taipei。必要欄位錯誤時整份拒絕，不從其他個案補值。

狀態保留 NEW/TO_CONTACT/APPOINTED/WAITING_FORM/FORM_COMPLETED/COMPLETED 六種，不將 FORM_COMPLETED 當成結案。API 列表狀態按原值精確篩選；Demo 保留原三類。

目前 Client 選取的五個欄位以外，性別、出生日期、身分證、地址、聯絡人/關係/電話/email、優先程度、表單進度、追蹤紀錄及編輯權限均沒有本批可使用的確認契約。即使其他分支 DTO 帶有個資，本批 Client 仍未開放消費，不假裝已接上。缺少的顯示欄位為「未提供」，不是 0、無紀錄或虛構個資。organizationId/role 不取自 query 或當成可信授權。

需 11 固定：路由、陣列/分頁契約、五個必要欄位/UTC/狀態、404/403 語意、院所/個案範圍、停用/撤權行為與可整合 Commit。total、全文搜尋、篩選排序參數、Guardian 摘要及可顯示個資仍待確認；不自行擴充 producer。

## 搜尋、排序、分頁

Demo 保留 24 筆整份資料的搜尋、三類篩選、優先程度/日期排序及數字分頁，另提供明確排序選項。

API 每次只 GET page/pageSize，不送未定義的 query/status/sort。姓名/代碼搜尋、六類篩選與排序只作用於本頁，畫面明列「不是全資料庫搜尋」。顯示本頁取得/篩選筆數，不捏造總數/總頁數。滿 10 筆僅允許嘗試下一頁，不保證還有資料；空頁仍保留上一頁。不得把目前頁資料推算成 Dashboard 全院統計。

## 隔離測試與重現

`tests/MedicalCaseFixtures.cjs` 的 FakeCaseAdapter 只在 tests，提供 12 筆 UUID 虛構個案、失敗狀態及錯 ID。正式頁沒有 Fake HTTP 實作。

```powershell
node --test tests/MedicalCaseFrontend.test.cjs
node --test tests/MedicalApiClient.test.cjs tests/MedicalCaseFrontend.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/MedicalDemoFixtures.test.cjs tests/MedicalDemoEntry.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:UseAppHost=false
node tests/MedicalCasePreviewServer.cjs
```

PreviewServer 只綁 127.0.0.1、列印隨機 port；使用合成醫療身分，沿用隔離 Demo Host，沒有正式 JWT/HTTPS/DB 驗證。對 `/api/cases` 額外提供測試資料，拒絕非 GET。可在本機測試 URL 加 `scenario=empty|expired|forbidden|missing|unavailable|wrong-id`；這些情境只由測試 Host 解讀，不是正式 API query 契約。不能公開部署此 Host。

本批新增 Case Fake/Contract 測試 16 項；完整 Node 回歸 177 項全部通過、0 失敗（包含既有登入/權限/Client/Demo/原始答案/教師草稿前端回歸）。Release Build 通過，0 警告/0 錯誤；git diff --check 通過。首次文字掃描誤把「不含答案」註解當成答案欄位，已改為檢查欄位語法；沒有略過測試。

瀏覽器使用隔離 Host 的合成醫療身分驗收，基準 1440×900，另測 390×844；列表/詳細文件均無橫向溢出，列表可垂直捲動。已操作兩頁分頁、本頁搜尋、指定第 11 號測試個案、空資料、403/404/503、錯 ID 拒絕、401 導回登入；確認 API 詳細 DOM 不含 Demo 敏感值屬性或 Demo 對話框。Demo 24 筆列表、DEMO-001 的 26 題原始答案、DEMO-024 空個案均可讀；Demo 聯絡對話框於 1440×900 邊界為 x=410/y=106.3/w=620/h=687.5，完整位於 viewport。初次沙箱 Host 連線逾時，經核准改以本機 loopback 隔離 Host 完成，未操作真實帳號或正式來源。測後還原 viewport；API 版面截圖保存在任務 artifacts，不納入程式發布。

## 正式 API 驗收仍未完成

需要 11 固定版與 Case scope、團隊核准整合、模型/Migration 唯一維護者確認相容性、受控地端 HTTPS/同源設定及全虛構資料測試帳號。再用相同 Client 驗證實際列表/詳細、跨院所不可見、401/403/404/503、重整及資料一致性。這些 Fake/Contract/UI 測試不證明真實 Case API 或 MySQL 已驗收。本批也未接 Task/完成度/答案 producer，不因詳細頁存在而算完成。
