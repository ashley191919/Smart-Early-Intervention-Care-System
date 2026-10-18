# 家長／教師整合準備與交接清單

日期：2026-10-18。以使用者指定的《專題開會紀錄檔_1015》為最新分工。
目前程式基準：`feature/case-guardian-management`／`1f1742f`。
`1f1742f` 為本輪之前的交接基準。本輪交接文件及前端補強的提交編號，請以此分支最新 Git log 與推送回報為準。

## 1. 責任與程式交接

| 範圍 | 最新主責 | 現有程式與交接界線 |
| --- | --- | --- |
| Case Entity／CRUD | 7 | `Data/Entities/CaseRecord.cs`、`Services/Cases/CaseGuardianService.cs` 的 Case CRUD、`Controllers/CasesController.cs` 的 `/api/cases` GET／POST／PUT。沿用既有程式，不另建 Case。 |
| Guardian／邀請／綁定／撤銷 | 11 | 同一 Service／Controller 的 Guardian、invitation、binding 方法，以及 `GuardianContact`、`CaseGuardian`、`ParentBindingInvitation`。 |
| 家長個案範圍 | 11，與 7／16 對齊 | `Services/Cases/CaseAccessService.cs`；醫療與家長邏輯目前共用此檔，修改前協調，不能整檔覆蓋。 |
| 問卷共用儲存／RBAC | 16 | 家長／教師專用 API 呼叫共用核心；不複製 Draft／Submit／Response 儲存流程。 |
| Schema／Migration | 16 | 本輪不產 Migration，不修改資料庫、Seed 或 ModelSnapshot。 |

**重要：Case 與 Guardian 目前同檔，尚未實體拆分。** 上表是責任交接，不代表已搬移程式。建議先按方法確認修改範圍，再於整合分支協調拆分；不要刪除既有 Controller 或 Service。

模型、欄位及權限細節沿用 [既有交接](11-case-guardian-handoff.md) 與 [後端成果及限制](11-parent-backend-completion.md)。舊文件的分工、驗證渠道及授權期限若與 10/15 會議不一致，以會議後確認的規則為準。

## 2. 現有 API 範圍

| API | 用途 | 歸屬 |
| --- | --- | --- |
| GET／POST `/api/cases`；GET／PUT `/api/cases/{caseId}` | 個案管理 | 交接 7 |
| GET／POST `/api/cases/{caseId}/guardians`；PUT `.../{guardianId}` | 照顧者資料 | 11 |
| POST `.../{guardianId}/link`、`.../{guardianId}/revoke` | 核准／撤銷關係 | 11 |
| POST `.../{guardianId}/invitations` | 建立綁定邀請 | 11 |
| POST `/api/parent-binding-invitations/{invitationId}/revoke` | 撤銷邀請 | 11 |
| GET `/api/parent/cases`；POST `/api/parent/bindings` | 取得自己的孩子／綁定 | 11 |
| GET `/api/parent/profile`；PUT `/api/parent/profile/{guardianId}` | 本人聯絡資料 | 11 |
| GET `/api/parent/cases/{caseId}/tasks` | 家長任務清單 | 11，接共用任務資料 |
| GET `/api/parent/cases/{caseId}/teacher-completion` | 教師完成狀態，沒有答案 | 11 |

目前範圍方法為 `CanReadAsync`、`CanReadParentTaskAsync`、`CanWriteParentTaskAsync`。它們是權限檢查，不能取代 Submit／撤銷之間的交易鎖定。

## 3. 畫面與填答流程盤點

| 流程 | 目前實作 | 接下來要接／確認 |
| --- | --- | --- |
| 正式家長登入 | `parent-otp-client.js`、後端 OTP 介面；未設定服務時拒絕登入 | SMS／Email 待確認，首次帳號建立及共用 Session 撤銷待協調。 |
| 家長工作台 | `parent-account-workspace.js`，正式 account 模式讀取孩子、任務、聯絡資料與教師完成狀態 | 不以展示資料補正式 API 失敗。 |
| 家長正式填答 | 正式任務列表尚未提供填答入口 | 等共用服務契約後接題目、草稿、提交與結果；不導向展示填答冒充正式流程。 |
| 教師授權任務 | `teacher-access.js`、`teacher-workspace.js`，已有授權任務／Session 流程 | 現行接口仍有 `/api/dev/teacher-workspace`，需和共用 RBAC／同意紀錄整合，不能只改 URL 宣稱正式完成。 |
| 教師草稿 | `teacher-draft.js` 已有載入、版本衝突、保存中、重試及輸入保留 | 既有流程保留，不重做一份；後續確認共用服務契約。 |
| 教師提交 | 已有必填檢查、確認框、提交中及重試同次提交 | 尚待跨模組與實際 MySQL 驗收。 |
| 展示家長工作台／填答 | `parent-workspace.js` 及共用展示流程 | 僅作畫面展示，與 account 模式分開，不作權限／持久化證據。 |
| 同意書 | account 模式目前提供承辦窗口說明 | 同意本文、版本、紀錄、撤回及 Grant 關聯待定；本輪不做假同意保存。 |

## 4. 本輪獨立補強

- 家長 API 連線失敗時提供可理解訊息；不自動重送可能已寫入的操作。
- 非 JSON 的 403／404 仍保留 HTTP 狀態，使工作台能清除受保護資料。
- 綁定／修改姓名以執行中旗標阻擋重複提交。
- 修改請求收到 401／403／404 後關閉對話框、清除畫面資料，並讓既有載入結果失效。
- 僅改錯誤與請求處理，不更動字體、配色或版面尺寸。

## 5. 給共用核心的待確認契約

1. 已 Push 的共用問卷版本、Commit、Service 方法及 DTO。
2. 題目／版本、草稿讀寫、提交回執與本人答案查詢的方法。
3. 作者識別由後端提供；草稿版本、冪等鍵與錯誤格式。
4. 哪一層持有交易、Scope 檢查與鎖定順序，讓撤銷與提交可共同遵守。
5. 功能 Permission 與孩子／Grant 範圍同時檢查；功能權限不能取代資料範圍。
6. JWT／Cookie／CSRF 及 SessionVersion 等共用規格。不自行放寬現有登入規則。

## 6. 整合後驗收清單（尚未執行）

以下是待驗收案例，不代表本輪已通過正式 MySQL 驗收。

- [ ] 無登入、到期登入：不能查看個案與問卷，提示重新登入。
- [ ] 未綁定、錯院所、未驗證帳號：不能存取指定個案與任務。
- [ ] 不同孩子切換：不顯示上個孩子或舊請求的資料。
- [ ] 空任務與網路中斷：顯示正確狀態，不顯示虛構任務。
- [ ] 草稿版本衝突：不覆寫新資料，提示重新載入。
- [ ] 不同照顧者：不得接續、讀取或覆寫他人草稿；此為待確認及實作的建議規則。
- [ ] 提交中連按：只有同一提交意圖；失敗重試不重複新增答案。
- [ ] 提交成功：顯示伺服器回執，家長／教師不可修改原答案。
- [ ] 綁定撤銷後：任務、草稿與答案存取均被拒絕，原資料保留。
- [ ] 撤銷先完成：提交失敗且不新增答案；提交先完成：保留答案，撤銷後阻擋後續存取。
- [ ] 教師授權撤銷：相關 Session 不能讀寫或提交。
- [ ] 指定任務全部提交後：該次教師授權停止填答。
- [ ] 家長教師進度 API：不回傳教師答案、草稿、分數或補充觀察。
- [ ] 真實 MySQL：資料保存、關聯與狀態在重啟後仍正確。

## 7. 等待期間與回覆後的工作

目前可交接本文件與前端補強；未獲共用契約前，不增加正式填答 API、不更動共用 Schema。依使用者後續確認，7 沿用既有 Case 程式，不再重複詢問是否另建 Case；精確方法界線見 [Case 交接與教師同意需求](11-case-transfer-and-teacher-consent.md)。
回覆後順序：確認固定版本 → 接家長正式填答 → 教師授權／同意紀錄整合 → 共同驗收 → 由 16 統整 Migration 與部署檢查。

## 8. 本輪驗證結果

`node --test tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs tests/ParentAccountClient.test.cjs tests/ParentOtpClient.test.cjs`：29 項通過，0 項失敗。新增案例包含非 JSON 拒絕存取、寫入斷線不自動重送、解析失敗期間登入切換。

`parent-account-workspace.js` 語法檢查及 `git diff --check` 通過。後續已完成隔離瀏覽器 1440×900 驗收，詳見 [前端驗收結果](11-parent-browser-qa-20261018.md)。上述結果不代表真實 MySQL、正式 OTP 或跨模組驗收完成。
