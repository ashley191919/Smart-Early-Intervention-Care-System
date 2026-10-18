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
