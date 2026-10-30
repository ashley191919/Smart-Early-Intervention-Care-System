# V5.3 醫療端畫面與流程盤點／必要修正

日期：2026-10-25。分支：feature/medical-backend。

本輪先唯讀檢查 AGENTS.md、shared-contract-v1.md、SQL 設計草案、07-02／07-04／07-05／07-06 文件、現有醫療頁面及腳本，再進行小範圍修改。對照依據是使用者提供的 V5.3 需求摘要與 repository 文件；未取得院方原始會議紀錄或正式簽核，不宣稱所有業務規則已定案。

## 一、原有功能，不把畫面存在當作正式完成

| 畫面／模組 | 修改前現況 | 本輪結果 |
| --- | --- | --- |
| staff-login／staff-auth | 有 Session、CSRF、me、logout、permission 失效與 bfcache 防護；不是本輪新建的登入 | 保留，不改認證或 RBAC；前端回歸驗證既有行為，沒有真帳號／HTTPS Cookie 驗收 |
| staff-dashboard | 三類展示個案統計與固定近期工作；API 設定未阻止固定數字顯示 | API 模式數值顯示未提供，移除固定待辦列並停用無資料的篩選；保留追蹤／評估提醒位置，不建立排程、不推算正式總數；問候語不再固定帶姓名 |
| cases | Demo 24 筆、10 筆分頁；API 分頁 20 筆，搜尋／篩選只作用本頁；完整 CaseDto、安全投影與錯誤／舊回應防護 | 不改功能。正式六狀態與 Demo 三種展示分類並存，不自行重定義狀態機 |
| case-detail／case-detail-empty | Demo 三頁籤：基本資料、追蹤紀錄、表單紀錄；結果入口已併入表單紀錄，另有獨立結果頁。API 模式先移除靜態資料／Demo 對話框，再用 medical-case-detail 渲染 | 不重建，不新增重複結果頁籤；保留遮罩與追蹤功能。正式欄位缺漏時依現有固定契約拒絕錯 DTO；契約未提供的聯絡人等展示欄位顯示未提供 |
| medical-api／medical-case-model | 共用唯一 Client、安全 DTO、同源 transport；不由 query／角色／本機儲存授權 | 不改 Client、DTO 或 Store，不接管 11 的 CRUD |
| medical-task-model／medical-task-view | 正式 Task 與 completion 唯讀 consumer、獨立載入與錯誤狀態；實際角色／必要性／輪次／提交時間 | 完整保留 07-05。PENDING=待填、IN_PROGRESS=進行中、SUBMITTED=已提交、CANCELLED=已取消；不自行換算完成率或推算有效輪次 |
| form-assign | 五份問卷 UI，其中 SNAP-IV／克氏接既有 SharedDemo；沒有 Session guard 或指派 Permission，提交會寫既有本機展示儲存 | 接回既有 StaffAuth 與 Permission presenter；未確認指派權限時停用確認，submit 再檢查。正式模式清除固定個案與展示表單，不呼叫 SharedDemo.assign；新增停用的常用問卷入口，沒有預勾清單 |
| response-detail | 受保護 Development fixture／既有 local-demo 的唯讀答案；SNAP-IV 26 題、無正式計分；匯出停用；沒有正式 Response reader | 正式設定、source=api 或正式 UUID 個案路由顯示「尚未提供正式結果」，不讀 Demo；返回保留 API 個案。既有 26 題流程不改；分開預留停用 Excel／Word 按鈕，沒有下載 URL |
| staff-header | 真正登出委派 StaffAuth；舊 API Case 模式已排除固定提醒 | 擴充排除條件至設定／其他正式入口；導覽保留 source=api，不從正式頁返回展示列表／Dashboard／新增個案 |
| 07-06 靜態發布 | allowlist、manifest、只綁 loopback 的靜態 server／測試用 Fake Host | 原工具不改；新增使用的資源都在既有 allowlist，完整發布回歸仍通過 |

本輪沒有修改 case-detail.html、case-detail-empty.html、medical-case-detail.js、medical-task-model.js、medical-task-view.js、medical-api.js、medical-case-model.js、staff-auth.js 或 07-02／04／05／06 原成果文件。

## 二、權限與展示邊界

- 現有 StaffAuth 只認得 case.read、case.status.update、case.status_history.read、questionnaire.task.read 四個已註冊 code。
- questionnaire.task.assign 是本輪 UI 的停用占位名稱，**不是已核定或已註冊 Permission**。StaffAuth 對它回 false；沒有新增 policy、角色授權、RBAC 註冊或前端放行。須由權限與 16 主責確認後，另行對接；不能拿 case.read／task.read 或 CASE_MANAGER 角色替代寫入授權。
- 原指派選項、計數及 SharedDemo 程式保留，但目前確認指派不可執行；不把停用視為正式指派已完成。直接提交事件同樣會被 Permission 檢查擋住。沒有第二套 Task 儲存。
- 常用問卷按鈕保持 disabled，所有可操作 checkbox 初始未勾選；沒有決定常用組合、必要性、角色或輪次。
- 正式設定不會因 401／403／404／503、錯誤 DTO 或缺少 producer 回退展示。Dashboard 不以本頁 Case 數量估算總數。
- 既有 SharedDemo 仍是舊展示流程的 localStorage 模組，未因本輪而變成正式後端。新加的正式 guard 不讀寫 localStorage／sessionStorage；未修改其他角色的 Draft、Submit、TeacherGrant 或其儲存行為。
- 本次新畫面狀態使用「未提供」「尚無可顯示的待辦資料」「目前無可指派的問卷」等資料狀態。受保護問卷既有 Demo 標示依本次需求保留；舊追蹤功能中的展示說明沒有擴大重寫。文字不代表登入或保存已完成。

## 三、實際修改檔案

- wwwroot/staff-dashboard.html：移除固定姓名問候。
- wwwroot/js/staff-dashboard.js：正式統計／待辦安全空狀態。
- wwwroot/js/staff-header.js：正式提醒與導覽隔離；原 Session／logout 不改。
- wwwroot/form-assign.html：既有登入／Permission 腳本與保護區、常用問卷停用入口、指派權限提示。
- wwwroot/js/form-assign.js：正式模式阻止展示寫入、submit Permission 檢查、預設停用確認。
- wwwroot/response-detail.html：Excel／Word 停用按鈕。
- wwwroot/js/response-detail.js：正式結果空狀態與 API 返回路由。
- tests/MedicalV53Ui.test.cjs：10 項隔離測試。
- 本文件。

所有 wwwroot 路徑均位於 EarlyInterventionCare.Api。未新增產品頁面或 CSS，未修改其他組員後端。初始已有未追蹤的 CaseStatusWorkflowService、ICaseStatusWorkflowStore、對應 Harness、SQL 草案及根目錄 git 檔案，均未操作、刪除或納入本輪成果。

## 四、仍缺的功能與組員協助

### 11：Case／Guardian／Consent

- 提供固定 producer commit 與可驗收環境；目前 Case GET consumer 依既有固定 11 欄契約驗證，不等於已讀到 11 最新實作。
- 核對列表／詳細院所及個案範圍、403／404、全資料庫搜尋／排序／總數，以及 Guardian 聯絡摘要與個資可見範圍。
- 個案新增、編輯、聯絡、預約與後續待辦正式儲存，狀態歷程及其寫入權限仍需各自契約；本輪沒有完成這些 CRUD。
- Consent 與家長綁定仍由主責處理；醫療端不可代替家長教師授權。

### 16：問卷／Task／Response

- 固定正式 Task／completion producer、醫療角色／Permission 與 case scope；目前控制器仍限制 Development 指定 CaseId，沒有移除 CaseAllowed／DenyAll。
- 既有 CheckUser 的 ADMIN／CASE_MANAGER 與 StaffAuth 的 CASE_MANAGER／MEDICAL_STAFF 範圍不完全一致，須主責核對，不由 07 擴權。
- 指派問卷版本清單、PUBLISHED／角色檢查、必要性、assignmentRound、重複指派 409 與正式指派 Permission。
- 提供受授權的醫療 Response reader：caseId／taskId／questionnaireVersionId／respondentRole、一致快照題目與答案／提交時間；與家長僅見教師完成狀態的限制分離。
- 提供匯出 DTO／API、格式、權限與可匯出欄位；不放授權碼或憑證。醫護修改離線 Excel 不回寫原始答案。
- 若提供計分，須先有院方認可規則與版本；現有 SNAP-IV 沒有正式規則，不自行加總或判讀。

### 老師／院方尚待確認

- 常用問卷預設組合、各角色及初評／複評適用條件，是否／哪些為必要問卷。
- 何時開新輪次、取消必要任務與歷史輪次如何影響當輪完成度、零必要任務語意；目前只顯示後端回傳統計。
- 評估完成／整體結案的人工決策及可操作角色，不能直接由 SUBMITTED 推定。
- 追蹤／評估提醒的觸發日、提前天數、是否考量工作日、誰負責與取消條件。現有日期算法只是既有展示，不建立正式排程。
- Excel／Word 的院方版型、必備欄位、遮罩、下載與離線修訂權限。
- 哪些完整敏感欄位允許醫護查看、是否需要操作稽核。前端遮罩不取代後端資料授權。

已確認的「教師授權僅由已綁定家長操作、不限時、指定任務全部提交或撤銷才失效」沿用共用契約，不重新自行定案。

## 五、測試結果

### 程式化

- 修改前完整前端回歸：220 通過。
- 最終 node --test tests/*.test.cjs：230 通過、0 失敗、0 跳過。包含發布包、MedicalApi、Case、Task、Session／Permission、受保護 Demo、Response、SharedDemo、TeacherDraft 回歸及本輪 10 項測試。
- 最初沙箱 loopback 出現 EACCES，經核准在沙箱外重跑通過；不是忽略 HTTP 測試。中途舊 source guard 的文字斷言失敗，保留原明確 guard 並增加其他正式模式條件後，完整回歸重跑通過。
- API Release Build：dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:UseAppHost=false，0 警告、0 錯誤。
- git diff --check 通過。

### 實際瀏覽器：隔離 Host，不是真登入／真 MySQL

使用既有 tests/MedicalCasePreviewServer.cjs，提供合成 /me、Case／Task 與受保護 Development 問卷 fixture；沒有啟動正式 Program、讀 secrets、操作 MySQL 或核發真正憑證。沒有改 Fake Host 放行任何正式政策。

- 1440×900：正式列表與詳細可開啟；姓名／身分證／地址遮罩，聯絡人等缺少欄位未提供；正式詳細 DOM 無 data-demo-full 或 Demo dialog。
- 三個 Tab 可切換；Task 四種狀態、輪次、必要性與臺北提交時間正確，SUBMITTED 不改個案狀態。
- Task 空清單有提示；401 導回 staff-login.html。
- 正式結果顯示「尚未提供正式結果」，沒有原始答案；正式指派沒有固定 Demo 個案或可操作寫入表單。
- Dashboard source=api：四項未提供、待辦列 0，不顯示固定 24／5／8／11；相關導覽保持 API source。
- DEMO-001 從表單紀錄點「查看原始答案」：26 題、26 個答案、尚未計分，Excel／Word 停用。DEMO-024 保留 0 / 0 與追蹤空狀態。
- 1440×900：document 1440×900，結果 main 高 820、scrollHeight 6282；可捲至頁尾按鈕，按鈕邊界在 viewport 內。
- 390×844：正式 Task、展示結果及指派頁 document 寬 375，不超 viewport；26 題均保留，長內容使用既有手機垂直捲動。結果頁返回／Excel／Word 按鈕可捲入可視區，最下方 bottom775.6。1440×900 登入按鈕 bottom559.4，完整可見。
- 聯絡 dialog：桌面 x410／y106.28／right1030／bottom793.73；手機 x12／y12／right363.2／bottom832，未超 viewport。手機 dialog 內容內部可捲動，Escape 關閉正常。
- 臨時 viewport 已還原；本輪隔離 Host 於驗收後關閉，未停止使用者原有服務。

未執行：真實醫療帳號／正式 HTTPS Cookie、跨院所 Case／Task 拒絕、MySQL 保存重啟、正式指派／Response／匯出／排程端到端。這些仍需 11／16 及受控環境，不列為完成。

## 六、建議 Commit 訊息

feat(medical): align V5.3 UI and prevent API-to-demo fallback

僅為建議；本輪不執行 Commit、Push、Merge，也不替使用者暫存未追蹤的其他成果。

## 七、後續 Dashboard 提醒節點 UI（2026-10-25）

Dashboard 依最新要求保留原追蹤功能並補充提醒：標題為「近期追蹤與提醒」，維持單一個案狀態篩選，恢復個案編號、原追蹤標題、優先標示及最近更新，補回歷史 DEMO-006；五節點另列為提醒事項，缺漏顯示未提供，評估日期有提供才顯示。排序仍是個案高優先／更新日期，已完成篩選只按更新日期；不提供提醒分類或節點排序。正式來源仍不讀展示記錄，空狀態為「提醒資料尚未提供」。提醒內容不是已通知紀錄、通知排程或個案狀態。

此後續批次的欄位、排序、資料界線、待確認規則與驗收結果見 [07-dashboard-assessment-reminders.md](07-dashboard-assessment-reminders.md)。不修改本文件前輪的歷史驗收紀錄。
