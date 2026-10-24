# V5.3 07-04：Case GET 固定契約消費端

日期：2026-10-24。07 工作基準 `feature/medical-backend` / `0a0644ae0d6a41879158e6afe97a9cfb8f08dc68`。

## 來源與查證界線

使用者提供的固定來源：`feature/case-guardian-management` / `0e4e16939b2fb711190aa490ca80098f0615ff47`。

本機 `git cat-file -t` 查無該 object，未 Fetch/Pull。以下依使用者貼出的固定契約實作並作 consumer acceptance；沒有讀取、執行或宣稱驗證該 Commit 的實際 producer。曾已取得的較舊 Case 分支不代表此指定版本；不直接合併或重寫 11 的 Case CRUD。

## 路由及分頁

- GET `/api/cases?page=1&pageSize=20` → `CaseDto[]`，不是含 items/totalCount 的 envelope。
- GET `/api/cases/{caseId}` → 同形狀的單一 CaseDto。
- Client 預設 page=1/pageSize=20，pageSize 1–100；page 檢查為正安全整數，不另猜測 provider 的頁碼上限。任何實際後端限制仍按 HTTP 錯誤呈現。
- 200 [] 是空資料；沒有 totalCount，不推算總筆數/總頁數。滿頁僅提供嘗試下一頁，空頁可回上一頁。
- 頁面只傳 page/pageSize，不自行加 query/status/sort/organizationId/role/permission。

## CaseDto → 安全消費 DTO → ViewModel

`medical-api.js.caseDto` 檢查完整十一欄位，再回傳同欄位名稱的安全投影；敏感字串先遮罩，非原始 DTO 的可編輯副本。

|固定 wire 欄位|驗證與消費結果|
|---|---|
|caseId|非空 UUID；詳細回應須與請求相同，不接受 DEMO-001|
|caseCode|非空字串；顯示個案編號，不當主鍵|
|childName|非空字串；三字如王測安→王○安，較長姓名隱藏中間全部字元，兩字只留姓，單字全遮罩；列表/詳細/aria-label 同樣使用遮罩值|
|birthDate|有效 Gregorian yyyy-MM-dd，排除不存在日期；顯示 yyyy/MM/dd，不套時區|
|sex|沿用現有 shared-contract-v1 的 MALE/FEMALE/UNKNOWN；顯示男/女/未知|
|nationalId|字串（空值可為空字串）；例如 A123456789→A12*******；未知格式不回顯，空值顯示未提供|
|address|字串；可辨識市縣時僅保留市縣及○○區○○路，其他非空格式顯示已遮罩；空值顯示未提供|
|caseStatus|沿用共用契約六種大寫狀態，FORM_COMPLETED 不等於個案結案|
|revision|數字整數 0–4294967295，保留 uint 最大值，不接受字串/小數/負值/溢位；僅 metadata，不推定可編輯權限|
|createdAtUtc / updatedAtUtc|有效 UTC ISO timestamp，接受 Z 或 +00:00 及 .NET 最多七位小數；不接受無時區/非零 offset；最近更新顯示 Asia/Taipei|

必要 wire 欄位缺漏/型別不符會拒絕整份回應，不用假值補齊。sex/狀態字彙與既有共用規格一致；若指定 producer 有差異，需要 11 提供確認，不修改其 DTO。未新增輸入格式、日期先後或臨床年齡規則。

Guardian/聯絡人、電話、email、關係、優先程度、表單進度、追蹤紀錄及編輯權限不在固定 DTO；UI 保持未提供或不開放未接線操作。額外 organizationId/role/permissions 不進入安全投影，也不能授權。revision/createdAtUtc 不被當成 PUT 或其他寫入能力。

遮罩在 Client 返回頁面前完成，ViewModel 重複驗證仍為同一遮罩，不保留額外原始姓名供搜尋。UI 沒有解除遮罩或原始值 data attribute。這不是後端權限防護：正式 GET 本身仍可能在已授權 HTTP 回應中提供原始欄位，後端必須獨立落實院所/個案範圍。這個安全投影不可拿去更新 Case。

## 固定虛構 JSON 範例

下列全部是假資料，不是病患或有效身分證驗證範例。列表以陣列包裝，詳細回傳單一物件。

```json
{
  "caseId": "20000000-0000-4000-8000-000000000001",
  "caseCode": "CASE-TEST-001",
  "childName": "王測安",
  "birthDate": "2021-05-12",
  "sex": "MALE",
  "nationalId": "A123456789",
  "address": "新北市測試區虛構路123號",
  "caseStatus": "NEW",
  "revision": 4294967295,
  "createdAtUtc": "2026-10-01T00:00:00Z",
  "updatedAtUtc": "2026-10-24T01:00:00Z"
}
```

## 模式與 UI 限制

- Demo 維持預設 caseSource=demo、24 筆展示資料、本機每頁 10 筆及既有問卷結果流程。
- `cases.html?source=api` 明確呼叫正式路由；列表預設每頁 20 筆。
- 詳細連結使用 `case-detail.html?source=api&caseId=<UUID>`，回列表保留 API 模式；指定/無效 ID 不得改顯示另一個 Demo 幼兒。
- 搜尋/狀態篩選/排序只作用於目前頁面；姓名搜尋只比對遮罩姓名，亦可用 caseCode。沒有宣稱全名、全文或全資料庫搜尋。
- API 401/403/404/503 或任何錯誤都不回退 Demo；錯誤清舊資料、延遲回應不能覆蓋新個案。
- 沿用 StaffAuth 的 Cookie/CSRF/401/403，未修改 Authentication/SessionVersion/權限核心。沒有憑證或 Case 資料持久化。

## 修改範圍及測試

主要修改 medical-api.js、medical-case-model.js、cases.js；七個醫療 HTML 僅更新共用 Client/相關 Case 腳本 cache 版本。更新兩組既有 Case/Client 測試、完整 DTO 的 MedicalCaseFixtures 與隔離 Host 預設分頁。新增 `tests/MedicalCaseGetContract.test.cjs` 與本文件；舊前端準備文件加上版本替代註記。

```powershell
node --test tests/MedicalCaseGetContract.test.cjs tests/MedicalApiClient.test.cjs tests/MedicalCaseFrontend.test.cjs
node --test --test-reporter=dot tests/MedicalApiClient.test.cjs tests/MedicalCaseFrontend.test.cjs tests/MedicalCaseGetContract.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/MedicalDemoFixtures.test.cjs tests/MedicalDemoEntry.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:UseAppHost=false
node tests/MedicalCasePreviewServer.cjs
```

Contract Test 包含完整陣列/單物件、十一欄位缺漏、uint 範圍、合法日期與 UTC、1/20 預設及明確頁碼、pageSize 100、200 []、拒絕含 totalCount envelope、401/403/404/503、UUID/Demo ID 隔離、不同 Case 回應拒絕、個資遮罩與不信任額外權限欄位。既有登入/Permission/Demo/原始答案/草稿前端回歸同樣保留。

隔離瀏覽器使用完整虛構 DTO 與合成身分，不是正式帳號。1440×900 已確認姓名/身分證/地址遮罩、DOB/性別映射、頁碼/筆數及缺漏聯絡欄位；詳細 DOM 無原始值 data attribute。另測 390×844 無文件橫向溢出，結束還原 viewport。

### 本次最終驗證結果（2026-10-24）

- Client／Case／固定契約三組合計 **55 項通過，0 失敗**；其中固定契約測試 16 項。這些測試注入 Fake HTTP Response，不執行 11 的 Controller 或資料庫。
- 上列完整前端回歸指令共 **193 項通過，0 失敗、0 跳過**。涵蓋既有登入、Cookie／CSRF 消費端、Permission、Demo、原始答案及教師草稿回歸；不代表真帳號、HTTPS Cookie 或真實後端授權已完成端到端驗收。
- API Release Build 成功，**0 警告、0 錯誤**；僅編譯，未啟動 API、連線 MySQL 或執行 Migration。
- 詳細頁瀏覽器實測：`birthDate=2024-02-29` 仍顯示 `2024/02/29`；`updatedAtUtc=2026-10-24T23:30:00Z` 顯示臺北時間 `2026/10/25 上午7:30:00`。出生日期是日期字串映射，不受瀏覽器時區轉換影響；UTC metadata 中 createdAtUtc 保留原值，並未新增建立時間 UI。
- 空身分證／地址與未在契約提供的聯絡欄位顯示「未提供」。必要 wire 欄位若為 null 或缺漏，整份回應安全拒絕並顯示格式異常，不把 null 自行解讀成合法 DTO，也不保留前一位個案資料；實際 producer nullable 規則仍需與 11 核對。
- 隔離 Host 的 `blank-fields`、`null-fields`、`utc-boundary` 情境僅供虛構 HTTP/UI 測試。503 畫面只有錯誤及重試入口，不回退 Demo。API 無 totalCount，沒有虛構總筆數或總頁數；搜尋／篩選仍只作用於當頁。
- 從 Demo 個案的「表單紀錄」開啟「查看原始答案」，實際顯示 SNAP-IV **26 題**及第 1–26 題內容。仍走既有受保護的 Demo 接點，沒有將答案移入公開靜態 JS，沒有改 Draft／Submit。
- `git diff --check` 通過；Git 只有既有 LF→CRLF 提示，無空白格式錯誤。本次不暫存、不 Commit、不 Push、不 Merge。既有 Workflow、SQL 與其他未追蹤成果保持未動。

## 待整合驗收

需取得並核對指定 Commit、由團隊核准整合 11 唯一 Case producer 及資料模型相容性、地端同源 HTTPS/測試帳號與全虛構資料。用相同 Client 測真路由、跨院所不可見/404、停用/撤權、重整及一致性。這批 Fake/Contract/UI 測試不證明指定 11 producer 或真 MySQL 已通過。未合併分支、未改 Case CRUD/16 核心/DbContext/Migration/Seed/DenyAll/Workflow，未 Commit/Push 或操作資料庫。
