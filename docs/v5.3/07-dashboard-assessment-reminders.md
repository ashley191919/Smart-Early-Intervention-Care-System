# 07 Dashboard：追蹤與評估提醒節點 UI

日期：2026-10-25；分支 feature/medical-backend。

## 目前版本：保留原追蹤資訊，再補充提醒事項

本節為最新驗收；下面「上一版簡化」與更早雙篩選設計僅保留歷史，不代表目前畫面。

### Git 比對與最小恢復

先檢查 git status／diff／log，唯讀參考目前 HEAD 2a6d1e4、Dashboard 最後內容異動 44ec4e4，以及原始近期追蹤版本 cae4cdd；沒有 restore／reset／覆蓋整頁。cae4cdd 的列表有三種處理狀態、高優先排序、更新日期與 DEMO-001～006；後來的工作概況版本改成類型／截止日排序。最近簡化又移除了編號、優先標示及可見更新時間。

| 原有功能 | 最近簡化後 | 本次恢復／保留 | 提醒整合 |
| --- | --- | --- | --- |
| 四項摘要與既有版面 | 保留 | 不變，Demo 24／5／8／11 | 不以提醒筆數改統計 |
| 原追蹤列表／事項 | 部分標題被提醒取代，黃○恩缺漏 | 恢復六筆原工作標題，補回 DEMO-006 黃○恩，保留目前其他記錄 | 原 h3 不再被提醒覆蓋 |
| 全部／待聯繫／進行中／已完成 | 保留 | 仍僅一組篩選 | 不依提醒節點篩選 |
| 高優先排序 | 有，但 raw priority 為待辦維度 | 用明確 casePriority 依現有 case-demo-data.js 排序 | 不設提醒 rank |
| 更新日期新到舊 | 保留 | 保留；已完成篩選依原邏輯只比較更新日期 | 不拿評估日期代替 |
| 姓名／個案編號 | 姓名有、編號移除 | 恢復編號 | 不改姓名或 caseId |
| 個案處理狀態 | 保留 | 維持目前 Case Demo 狀態 | 不回退舊王○安狀態 |
| 優先程度標示 | 移除 | 優先 badge；一般以 — 表示 | 與提醒無關 |
| 最近更新顯示 | 僅留資料 | 恢復日期與 time 元素 | 評估日期另列、有資料才顯示 |
| 查看個案 | 保留 | DEMO-001／024 正確導覽，其餘停用 | 不新增錯誤連結 |
| 空狀態／數量 | 保留 | 保留篩選筆數與空狀態 | 缺提醒文字顯示未提供 |
| Header／Sidebar／鈴鐺 | 保留 | 原程式與互動不改 | 無新通知規則 |
| 登入／Session／Permission | 保留 | 原控制不改 | 不繞過登入 |
| API／Demo 隔離 | 保留 | API 缺資料仍提醒資料尚未提供 | 不讀 Demo 或回退 |

只做上述資訊恢復：名稱下方加回編號；右側加回優先標示；原追蹤標題下方另加「提醒事項」與「最近更新」。保留原 Avatar、三欄 Grid、樣式、字體及 RWD，沒有改回古早整張 Table 或重建 Dashboard。

data-priority／todo-status／due-date 保留原工作值供既有 Header 使用；新增 data-case-priority 明確沿用目前 Case Demo 的個案優先程度，避免把同個案不同工作的 priority 混成個案優先級。case-demo-data.js 本身不變，沒有改寫 CaseStatus 或建立新的正式個案。

同一個案原本有不同追蹤事項的列仍各自保留，不合併或刪掉工作；補回黃○恩後是8筆追蹤記錄、7個不同展示個案，篩選待聯繫／進行中／已完成為2／5／1筆。這是清單筆數，不是全系統個案總數。

### 本次檔案與驗收

- 修改 staff-dashboard.html、js/staff-dashboard.js、css/staff-dashboard.css、tests/MedicalDashboardReminders.test.cjs，以及本文件與 07-medical-v53-ui-audit.md。
- 完整前端回歸240通過、0失敗、0跳過；Dashboard 共10項，新增舊追蹤標題／個案涵蓋與可見優先／更新欄位回歸。
- RBAC Policy Harness43通過、RBAC Offline Harness22通過；API Release Build 0警告、0錯誤；git diff --check通過。
- 1440×900：document1440×900，main高820／scrollHeight1325；8筆編號、8筆更新日期及3個優先標籤可讀；只有一套篩選，長列表可垂直捲動。
- 390×844：document寬375／高2655，追蹤列／日期／操作區溢出數量0；日期與長文字換行，無橫向溢出。
- 已實際點擊 DEMO-001 至 case-detail.html、DEMO-024 至 case-detail-empty.html；其餘展示個案仍無錯誤導覽。
- 鈴鐺可展開，使用者選單開啟後鈴鐺收合；Escape 關閉正常。正式 source=api 清單0列、4個篩選停用、提醒資料尚未提供；401仍回登入頁。
- 比對本輪開始／結束 SHA-256，staff-header.js、staff-auth.js、medical-api.js、medical-case-model.js、medical-task-model.js、form-assign.js、response-detail.js 保持原樣。其他組員／Workflow／SQL 成果未動。
- 使用既有隔離虛構 Host，未啟動正式 API Program 或連 MySQL；臨時 viewport 已還原，驗收 Host 測後停止。不是正式帳號或真通知機制驗收。

建議訊息：fix(medical): restore dashboard tracking fields alongside reminders。適合作為獨立邏輯變更，但 HTML／JS 與前輪未提交修改重疊，應先固定基準或分段審查，不能將整個工作目錄直接暫存。本輪不 Commit／Push／Merge。

## 上一版：簡化為近期追蹤與提醒（歷史）

本節取代下方上一輪「雙重篩選／節點欄位／評估日期排序」設計；下方內容保留為歷史盤點，不再代表目前 UI。

- 標題改成「近期追蹤與提醒」，摘要面板、24／5／8／11 展示數字與原字體保留。
- 每列只顯示個案名稱、提醒事項、有明確提供才顯示的評估日期、個案處理狀態，以及原有查看個案操作。移除可見個案編號、待辦類型、截止日、逾期提示、獨立節點欄位及重複待辦狀態／優先標籤。
- 只有一套「全部／待聯繫／進行中／已完成」篩選；原節點與類型篩選相關程式已移除。
- 五種節點直接作為虛構提醒事項文字的前綴，不保留節點分類資料或排序權重，也不依日期差選擇節點。
- Demo 排序僅為 high 在前、updatedAt 由新到舊、原順序作穩定 tie-break。data-updated-at 明確沿用 case-demo-data.js 同個案的既有日期；不用 deadline 或 assessmentDate 冒充更新日期。
- 評估日期缺漏或無效時隱藏整個日期列。此處不再額外顯示「未提供」，依最新使用者需求「有提供才顯示」。
- Header 用到的 data-todo-type/status/due-date 保留，不更改鈴鐺／登入／登出邏輯；它們不再是 Dashboard 可見欄位或排序依據。
- 正式設定及明確 API 路由仍先清除展示列，顯示「提醒資料尚未提供」，停用四個篩選，統計未提供。保留既有受保護區與導覽；未新增 API、儲存、通知或排程。

### 簡化版實際修改

staff-dashboard.html、js/staff-dashboard.js、css/staff-dashboard.css、tests/MedicalDashboardReminders.test.cjs，以及本文件與 07-medical-v53-ui-audit.md 的目前版本說明。CSS 只移除上一輪專屬節點／欄位樣式；測試改驗單篩選、日期條件顯示、高優先＋更新日期排序及原導覽，沒有移除安全測試。

### 簡化版驗收結果

- 完整前端回歸 238 通過、0 失敗、0 跳過；其中 Dashboard 8 項測試依目前需求調整，原登入／權限／Case／Task／Response／發布包回歸保留。
- API Release Build：0 警告、0 錯誤；git diff --check 通過。
- 隔離瀏覽器 1440×900：document1440×900，main高820、scrollHeight1114；列表可垂直捲動，只有一套篩選，缺日期的三筆不顯示日期列。
- 待聯繫／進行中／已完成分別2／4／1筆；單篩選切換、計數與按鈕 aria-pressed 正確更新。
- 390×844：document寬375、高2098，提醒列與篩選超出 viewport 的數量0；完成篩選1筆正常。
- API 模式：展示列0、四個篩選停用、提醒資料尚未提供；不回退展示。
- 使用既有虛構資料驗收 Host，不啟動正式 API Program 或連 MySQL；viewport 已還原，驗收 Host 測後停止。不是正式登入或通知驗收。

## 盤點與變更

本次先閱讀 AGENTS.md、staff-dashboard.html、staff-dashboard.js、共用 Dashboard CSS、staff-header.js、case-demo-data.js、既有前端測試及 07-medical-v53-ui-audit.md。

原 Dashboard 是六筆近期工作，提供「全部／聯絡／預約／表單」類型篩選，而不是個案狀態篩選。既有 priority 是待辦優先程度；同一個案不同工作可以不同 priority。原排序為高優先→未完成且逾期→待辦到期日→原始順序。正式來源隔離與沒有統計 API 時顯示未提供的前輪成果已存在。

本輪改為「追蹤與評估提醒」，增加五個節點與評估日期欄位，以及「全部／待聯繫／進行中／已完成」個案狀態篩選；保留原類型篩選，兩組可交集使用。摘要面板與 Demo 24／5／8／11 不變。移除無功能且附開發說明的查看全部待辦入口，不建立其他頁面。保持原字體大小，僅增加必要的欄位換行與間距 CSS。

## 資料欄位與模式

展示資料仍位於原 HTML li 的 data attributes，不建立新資料來源、Task Store 或 API Client：

| 欄位 | 意義 |
| --- | --- |
| data-case-status | 虛構個案處理狀態 pending-contact／in-progress／completed，與既有個案清單一致；不修改正式 CaseStatus／Workflow |
| data-todo-status | 這一筆待辦工作自己的狀態 pending／completed 等；不是個案或問卷提交狀態 |
| data-priority | 這筆工作原本的 high／normal 優先程度；不是提醒節點，亦不保證等於個案 priority |
| data-todo-type | 聯絡／預約／表單工作類型 |
| data-due-date | 原待辦處理截止日，不是通知觸發日或 Case updatedAt |
| data-reminder-node | 明確指定 initial／month／week／day-before／assessment-day；缺漏或未知值顯示未提供 |
| data-assessment-date | 明確指定的本機日曆評估日期；缺漏或無效日期顯示未提供 |

五種節點文字：初次通知、評估前一個月、評估前一週、評估前一天、評估日。程式不依今天、截止日期或日期差自動選擇節點，不將節點當成正式已通知紀錄。

| 展示工作 | 節點 | 評估日期 |
| --- | --- | --- |
| TODO-001／林○樂 | 初次通知 | 未提供 |
| TODO-002／王○安 | 評估前一週 | 2026/10/15，沿用詳細頁已有展示評估日期 |
| TODO-003／陳○恩 | 評估前一個月 | 2026/11/06，明確新增虛構展示日期 |
| TODO-004／張○庭 | 評估前一天 | 2026/10/13，明確新增虛構展示日期 |
| TODO-005／王○安 | 評估日 | 2026/10/15，同一個案日期一致 |
| TODO-006／林○宇 | 未提供 | 未提供 |
| TODO-007／李○安 | 未提供 | 未提供 |

TODO-007 是新增的虛構已完成工作，沿用 case-demo-data.js 的 DEMO-005 李○安／completed／normal，供完成篩選驗收。原六筆工作名稱、caseId、dueDate、待辦狀態與 priority 未改。Header 仍讀取獨立待辦狀態，這筆已完成工作不計入鈴鐺提醒；原鈴鐺日期演算法不改。

待辦截止日與評估日期、節點是不同欄位；不宣稱既有截止日等於五個節點的正式觸發日。這批明確資料不會被當成正式 API 回應。

## 篩選與 Demo 排序

1. 個案狀態篩選只比對 data-case-status，不比對提醒節點或 data-todo-status。
2. 類型篩選只比對 data-todo-type；兩個選取條件以 AND 組合。
3. 篩選只改顯示狀態與結果數量，不修改原始資料；無符合項目有空狀態。
4. 先按既有規則排序：high 優先、未完成且逾期優先、dueDate 由近到遠、原始位置作穩定 tie-break。取消或完成不算逾期。
5. 在相同 priority 層級中，只將「有效評估日期的記錄」依 assessmentDate 由近到遠，填回原本有日期的位置。缺日期記錄的位置完全保留既有排序結果；相同日期回用原排序比較。
6. 所有記錄都缺評估日期時，結果就是原排序。這個位置保留策略避免混合缺值時的成對 comparator 產生循環、順序不穩。
7. 不設定五個節點的 rank，不決定正式提醒優先級，不按問卷 SUBMITTED 修改整體個案狀態。

這是展示排序，不是老師／院方核定的正式通知優先演算法。

## 正式模式

caseSource=api 或明確的 source／caseId 路由會先清掉展示列、停用兩組篩選、統計顯示未提供，提醒顯示「提醒資料尚未提供」。不先讀 HTML 展示列或載入另一份 Demo，亦不從 CaseDto.updatedAtUtc、Case 本頁資料或 Task 提交狀態推算評估日期。

正式提醒 producer 尚未提供，因此本輪沒有發明 API 路徑，也沒有做通知 API 失敗時的重試或展示 fallback。現有 StaffAuth 的 401 導回登入、權限失效與保護區處理保留。

需要主責提供的正式契約：

- 經授權的 Dashboard 整體統計，不能從分頁 Case 列表推算。
- 可讀取的提醒清單／個案評估資訊，包含 caseId、遮罩名稱／代碼、明確評估日期、節點、獨立工作狀態及排序依據。
- scope／Permission、無資料與未提供的差別、日期／時區、歷次／本次評估的關聯，以及正式錯誤碼與版本。欄位名稱與 API 路由由主責共同確認，不由本頁決定。

尚待老師確認：一個月是日曆月或固定天數、週末／假日處理、補登與延期／取消評估後節點處理、重複提醒、初次通知與評估節點的優先關係、待辦 priority／評估日期／逾期的正式權重，以及何時算已通知。現在不建立排程、背景服務、通知紀錄、Email／SMS／推播。

## 修改檔案與相容性

- EarlyInterventionCare.Api/wwwroot/staff-dashboard.html
- EarlyInterventionCare.Api/wwwroot/js/staff-dashboard.js
- EarlyInterventionCare.Api/wwwroot/css/staff-dashboard.css
- tests/MedicalDashboardReminders.test.cjs
- docs/v5.3/07-medical-v53-ui-audit.md（追加本批索引）
- 本文件

純排序／日期函式由既有腳本提供 CommonJS 測試出口，不新增瀏覽器依賴，07-06 的資源 allowlist 不需改。沒有修改其他頁面、Authentication、SessionVersion、RBAC、Case CRUD、Questionnaire、Workflow、SQL、DbContext、Migration 或 Seed。既有未提交與未追蹤成果保留。

## 驗收

- 新增 8 項測試：五節點、缺值／無效日期、與個案狀態隔離、保持原工作優先級、兩組篩選／空狀態、日期與缺值排序穩定、API 模式提前排除展示、無通知／儲存機制。
- 完整 node --test tests/*.test.cjs：238 通過、0 失敗、0 跳過，包含原有 Dashboard、登入／權限／API／Task／Response／發布回歸。本機 loopback 測試經核准於沙箱外執行，不連資料庫。
- API Release Build（--no-restore -c Release -p:UseAppHost=false）：0 警告、0 錯誤。
- 額外 RBAC Policy Harness 43 通過、RBAC Offline Harness 22 通過，均0失敗，沒有資料庫連線。這兩組 Debug 測試建置另出現 MSB3061：既有 EarlyInterventionCare.Api.exe 存取遭拒；未刪檔、停止使用者服務或調整權限。此警告與上述 Release Build 的0警告分開記錄，不視為產品已通過正式部署。
- git diff --check 通過。
- 實際瀏覽器使用既有 MedicalCasePreviewServer 隔離虛構 Host，不啟動正式 API Program／MySQL，也不修改使用者原本的登入分頁。
- 1440×900：document=1440×900，main clientHeight820／scrollHeight1352；長提醒列表在既有主內容區捲動，沒有文件橫向溢出。五節點、未提供、評估日期、兩種狀態可清楚區分。
- 待聯繫／進行中／已完成分別顯示 2／4／1 筆；待聯繫＋預約為空，空狀態及結果數量正確。
- 390×844：document 寬375、高2566；主內容寬375，提醒列、metadata 及兩組篩選沒有超出 viewport，文字自然換行，狀態切換正常。
- 正式 source=api：展示列0、兩組共8個篩選停用、提醒資料尚未提供；正式模式沒有回退展示。
- 隔離 Host 回傳 401 時導向 staff-login.html，不出現展示工作臺。
- 臨時 viewport 已還原；本輪隔離服務驗收後停止。不是正式帳號／Cookie／跨院所授權或真實通知驗收。

## Commit 建議

feat(medical): add assessment reminder nodes to dashboard

適合作為一個獨立邏輯變更，但工作目錄原本含前輪未提交的 API 隔離修正，且 staff-dashboard.html/js 有重疊。應先固定前輪成果或分段審查／暫存，不能直接將整個工作目錄一次加入。本輪不執行 Commit、Push 或 Merge。
