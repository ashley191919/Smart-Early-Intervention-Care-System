# Case 交接與教師授權／同意紀錄需求

日期：2026-10-18。依使用者確認，7 沿用既有 Case 程式；本輪不重複詢問是否另建 Case。

## Case 交接範圍

7 接續維護 `CaseRecord` 與個案新增、列表、詳細、修改；11 保留 Guardian、邀請、綁定、家長權限及教師端流程。16 統整共用關聯與 Migration。

| 檔案 | 7 接手的範圍 | 11 保留／共同協調 |
| --- | --- | --- |
| `EarlyInterventionCare.Api/Data/Entities/CaseRecord.cs` | 既有 Case 模型及後續醫療欄位 | 修改欄位時通知家長端；不建立第二份 Entity。 |
| `EarlyInterventionCare.Api/Services/Cases/CaseGuardianService.cs` | `Dto`、`ListAsync`、`CreateAsync`、`UpdateAsync` 的個案 CRUD | `ParentCasesAsync`、Guardian、Invite／Bind／Revoke、Profile、TeacherCompletion。`ReadAsync` 等共用方法需協調，不整檔覆蓋。 |
| `EarlyInterventionCare.Api/Controllers/CasesController.cs` | `/api/cases` GET／POST，以及 `/api/cases/{caseId}` GET／PUT | Guardian／invitation／parent API。目前共用 Controller，暫不搬檔。 |
| `EarlyInterventionCare.Api/Services/Cases/CaseAccessService.cs` | 醫療授權部分配合 RBAC／Case scope | 家長 actor／binding scope 保留；共同修改需對照差異。 |

`CaseFieldValidation`、DTO、revision 與外鍵均可能被雙方呼叫，移動或改名必須同步更新呼叫端。交接不刪除既有程式，也不修改醫療端 DenyAll 或自行增加 Permission 放行。

## 教師授權的現有基礎

- 沿用 `TeacherGrant`、`TeacherGrantTask`、`TeacherSession`；GrantTask 對應指定任務。
- 教師工作台及草稿目前仍使用 `/api/dev/teacher-workspace` 系列接口；不能因畫面可操作就稱為正式家長授權已串接。
- 既有工作流會在提交後更新任務；全部指定任務完成時將 Grant 標示 USED、撤銷 Session。
- 完成後有短暫的回執讀取機制；「授權停止填答」不代表應刪除回執或原答案。後續整合必須保留提交冪等行為。
- 既有家長查詢只回教師完成狀態，不回答案、分數、補充觀察或草稿。

## 待實作的正式對接流程

1. 家長以可信身分進入指定孩子，後端檢查有效綁定及建立授權的資格。
2. 顯示適用的同意本文及版本；後端保存同意事件後，才允許符合範圍的授權。
3. 由家長選取可授權的教師任務，建立 Grant 與 GrantTask；不得任意傳入其他孩子／角色的 taskId。
4. 授權碼、專屬連結或 QR Code 指向同一個授權流程；QR Code 是呈現方式，不是另一套身分驗證。
5. 教師驗證後建立 Session；每次讀取、暫存與提交均檢查有效 Session、Grant、任務及適用同意狀態。
6. 家長撤銷授權時，後端撤銷 Grant 與 Session，阻擋後續讀寫；不刪除草稿或答案。
7. 所有指定任務提交後，停止填答；單份完成不能提前終止尚有未完成任務的授權。

以上是待與共用核心確認的對接需求，不表示目前已完成。撤銷家長綁定是否一併撤銷其教師 Grant、同意撤回的影響範圍及交易順序，尚待定案，不自行推定。

## 同意紀錄的建議資料（尚未定稿）

| 資料 | 用途 |
| --- | --- |
| 同意事件 ID、caseId、可信家長 userId／Guardian 關聯 | 追蹤何人對哪位孩子操作。 |
| 同意文件類型、版本與不可變本文參照或雜湊 | 還原當時實際同意內容；不能只保存目前最新版網址。 |
| 同意範圍及適用任務／Grant 關聯 | 區分一般資料使用與指定教師填答授權。 |
| 同意時間 UTC、事件種類及關聯的原事件 | 區分同意、撤回及後續重新同意，保留歷史。 |
| 撤回時間、操作人與原因（如有需求） | 權限失效及稽核。 |

不以勾選框、前端 localStorage 或手機欄位已填寫作為可信同意紀錄；也不以一般 AuditLog 自動代替可還原本文的同意資料。

## 待確認事項

- 使用單位確認：同意本文、是否強制同意、教師授權有效期限、撤回影響。
- 11／16 確認：Entity／外鍵、Grant／任務資格、Session 撤銷與交易。
- 7／11／16 確認：登入渠道、帳號停用、JWT／Cookie 即時失效及共用權限。

本輪僅完成交接與需求文件，不新增 Consent Entity、Migration、真實同意紀錄或正式授權 API。

## 2026-10-19 教師授權／同意現況盤點

此次依本分支程式重新核對，沒有修改共用核心、資料模型或權限。前面「登入渠道待確認」已由使用者決定家長採 Email OTP；其餘未決規則不能因前端畫面存在而視為定案。

| 階段 | 程式現況 | 正式整合缺口／下一步 |
| --- | --- | --- |
| 家長登入及孩子資格 | Email Actor／Guardian Scope 已實作並隔離驗證；正式入口仍關閉 | 16 Migration／回填後驗收，不以展示資料當有效家長。 |
| 查教師任務進度 | CasesController 的 GET `/api/parent/cases/{caseId}/teacher-completion` 只回 taskId、title、round、isCompleted | 保留最小揭露，不改成教師答案查詢。 |
| 同意閱讀與確認 | 有獨立 prototype 與說明，沒有正式 Consent Entity／保存 API | 本文、版本、保存、撤回效力與 Grant 關聯待核定。閱讀／勾選不算已保存同意。 |
| 家長建立教師 Grant | 展示頁可操作；Development Controller 有建立測試 Grant 的入口 | 尚無家長正式建立入口；11 接可信家長／指定任務／同意，16 配合共用儲存。不將 dev 建立入口直接改名使用。 |
| 授權碼／連結／QR | 既有前端與 Grant／GrantTask 基礎存在 | 三種呈現須對應同一有效 Grant，不另建 FormAccessToken 或第二套任務。 |
| 教師驗證及 Session | Development verify／task／logout 已存在 | 正式入口與 Cookie／CSRF／Session 撤銷規格需對齊 7／16；不移除 IsDevelopment 保護來開放正式資料。 |
| 草稿／提交 | Development Draft／Submit／Receipt 呼叫共用 workflow，檢查 Session、Grant 及指定 task | 16 統整正式共用服務；11 專用 API 接身分與範圍。沿用 revision、Idempotency-Key，不複製保存核心。 |
| 單份／全部完成 | 單份提交後有未完成任務則繼續；全部完成 Grant=USED，撤銷 Session 的填答資格 | 保留回執例外讀取，不能把「不能再填」解讀成刪除歷史答案。 |
| Grant 撤銷 | dev revoke 與 workflow 已有處理 | 家長正式撤銷入口、角色資格及共同交易仍待整合；不可只在畫面改狀態。 |
| 回執 | workflow 只回提交證明；全數完成後有最多 15 分鐘且不超過 Session 期限的讀取窗 | 不把回執當答案；正式規則由共用契約確認。 |

### 狀態與未決事項

- ACTIVE：僅有效 Session、Grant 及指定任務可讀寫；SUBMITTED 任務不再接受新答案。
- USED：全部指定任務完成，停止新填答；依服務允許條件讀既有收據，不能產生新 Response。
- REVOKED／Session 無效或逾期：停止後續讀寫。撤銷不刪除已提交答案。
- Grant 的業務有效期間、Session 登入期限與收據讀取窗是不同概念；沿用目前既定行為，不擅自新增 Grant 到期規則。
- 仍待共同決定：撤銷 Guardian 是否連動其教師 Grant、同意撤回影響範圍、正式 Cookie／CSRF／SessionVersion、作者欄位、交易擁有者與鎖定順序、新照顧者碰到既有草稿。
- 家長 Task List 繼續沿用現有路由；Definition／Draft／Submit／OwnResponse 等待 16 固定服務／DTO。以上缺口不藉由另建保存 API 解決。

### 取得穩定契約後的驗收順序（尚未完成正式驗收）

有效家長與指定孩子 → 保存核定版本同意 → 只選該孩子的 TEACHER 任務建立 Grant → 教師驗證 → 草稿保存／版本衝突 → 單份完成仍可填其他任務 → 全數完成停止寫入 → 原收據可依規則查閱 → 撤銷後新存取拒絕 → 原答案與稽核紀錄保留。

另外測錯碼、錯孩子／任務、重複提交、跨站寫入、帳號／Guardian 失效與撤銷／提交並發。這是後續驗收需求，不宣稱正式 Consent 或 MySQL 交易已完成。
