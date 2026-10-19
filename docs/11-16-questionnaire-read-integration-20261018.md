# 家長／教師問卷介接比對與讀取契約草稿

日期：2026-10-18。這份文件是程式比對、接口提案及待驗收清單，沒有新增正式 API、合併分支或套用 Migration。

## 1. 固定比對版本與結論

| 項目 | 版本 |
| --- | --- |
| 11 家長／教師端 | `feature/case-guardian-management`／`505224ab82fd7eb37bbebca0fe1530ec6d6f0364` |
| 16 共用核心與交接文件 | `feature/shared-rbac-core`／`a9de56965004921c50de0c306dcecf19da4c8e08` |
| 16 共用核心基準 | `1a87aaba71a49de531f2b118ab5531fc7c9de03b` |
| 共同祖先 | `a68ed6d0041cec6efef4549013acdcbf8c0b5a8b` |

已 Fetch 遠端，依上述固定版本檢視。用 Git merge-tree 模擬合併，退出碼 0、沒有文字衝突；沒有修改 HEAD、索引或工作區來實際合併。此結果不代表編譯、權限或交易已驗收，也不包含 7 的醫療分支。

**可以沿用現有家長任務清單；正式讀題仍缺接口；家長寫入要先完成作者模型與交易契約。**

交接依據：[16 的問卷交接文件](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/a9de56965004921c50de0c306dcecf19da4c8e08/docs/questionnaire-integration-handoff.md)。以下分工依 10/15 會議及本次交接方向；未決規則仍需共同確認。

## 2. 三方差異與整合注意事項

比對共同祖先後，雙方都改到的既有檔案只有 `Program.cs` 與 `tests/QuestionnairePhase22Harness.cs`。其他只存在某一分支的成果，不應因整檔覆蓋而遺失。

| 範圍 | 已確認的內容 | 後續處理 |
| --- | --- | --- |
| Program.cs | 11 的 Guardian／家長登入註冊與 16 的 RBAC 註冊可以文字合併。模擬結果保留兩方 DI，PermissionLookupExceptionMiddleware 在 Authentication／Authorization 前。 | 整合分支再確認實際權限及 Middleware 行為；沒有衝突不等於安全驗收。 |
| 共用問卷 DTO | 兩版 `QuestionnaireContracts.cs`、`QuestionnaireWorkflowContracts.cs` 沒有差異。 | 沿用既有欄位，不另造一套草稿／提交格式。 |
| 家長 Scope | 11 有 ActorAsync、CanReadAsync、CanReadParentTaskAsync、CanWriteParentTaskAsync；16 尚未整合。 | 身分取已驗證 principal，再查目前 DB；不能信任前端傳 userId。 |
| 家長任務列表 | 11 已有 `GET /api/parent/cases/{caseId}/tasks`，透過 IQuestionnaireStore 讀取。 | 保留現有入口，不重做清單服務。 |
| 共用問卷 Controller | 11 已改用 CaseAccessService；16 分支仍保留共同祖先的 Development 個案限制。 | 保留 11 的變更，再與 7 的正式醫療個案授權對齊；現有同院所醫療存取不是最終授權規則。 |
| 草稿／答案作者 | 現有 Draft 只有 LastSavedByGrantId；Response 只有 SubmittedByGrantId，沒有家長作者欄位。 | 16 統整作者模型、唯一鍵與 Migration；11 提供可信家長身分與範圍檢查。 |
| 教師共用填答 | 16 已有 GetDraftAsync／SaveDraftAsync／SubmitAsync／GetReceiptAsync，仍要求 Development 與教師 Session Token。 | 不能把 Parent JWT 傳入這些方法，也不能刪除 Development 判斷就宣稱正式串接完成。 |
| 輪次完成度 | 16 新增指定輪次 Evaluator；原 CompletionAsync 為跨輪次摘要。 | 不以舊摘要或最大輪次自行判定有效輪次；不自行更新 CaseStatus。 |
| 測試程式 | 11 加入 Guardian schema handoff 模式，16 加入問卷驗證及交易案例。 | 整合後保留兩方測試與模式，再實際執行。本次未執行合併後測試。 |

### 權限與交易的具體缺口

- 家長任務讀取：帳號、院所 ACTIVE、PARENT、PhoneVerified，以及同院所有效且已綁定的 Guardian 關係。驗證渠道若變更，須同步調整模型與檢查，不能只改畫面文字。
- `CanReadParentTaskAsync` 只代表任務範圍，不能拿來取代草稿／答案作者驗證。`CanWriteParentTaskAsync` 僅允許 PENDING／IN_PROGRESS。
- Scope 方法目前沒有提供與撤銷共同遵守的鎖定協定。提交交易中再查一次仍可能有競態；須由 11／16 共同訂出鎖定資料列、順序及隔離方式，撤銷端也配合。
- 16 的四個 Permission Policy 不等於家長／教師所有操作已授權；不得直接把醫療 task.read Policy 套到家長寫入。
- SessionVersion、家長／教師 Cookie／CSRF、統一錯誤格式尚未正式定案，不能從文件中的提案推定已實作。

## 3. 家長任務讀取：沿用現有契約

**已實作**：`GET /api/parent/cases/{caseId}/tasks`。

由 CasesController 驗證登入家長與 Case Scope 後，讀取共用任務，僅回傳 `respondentRole=PARENT` 且 `taskStatus!=CANCELLED`。目前包含所有已指派輪次及 SUBMITTED 任務，不自動選「最新有效輪次」。

200 回傳 `CaseTaskResponse[]`，沒有任務回 `[]`：

| 欄位 | 型別／用途 |
| --- | --- |
| taskId、caseId、questionnaireId、questionnaireVersionId | Guid；任務與固定版本識別 |
| questionnaireTitle、versionNumber、respondentRole | 字串；顯示用途，respondentRole 應為 PARENT |
| assignmentRound | uint；指派輪次 |
| isRequired | bool；必要任務 |
| taskStatus | 狀態字串；沿用現有狀態，不從畫面推算 |
| createdAtUtc、updatedAtUtc、submittedAtUtc | UTC 時間；submittedAtUtc 可為 null |

清單不含答案、草稿、分數或作者個資；不能因列出 SUBMITTED 任務就開放查看其他照顧者的答案。家長查看教師完成狀態仍使用獨立 teacher-completion 入口，不共用答案接口。

## 4. 家長讀題：待確認契約草稿

**尚未實作，以下路徑與 DTO 是提案，不是 16 現有接口。**

建議 `GET /api/parent/tasks/{taskId}/questionnaire`：以任務讀題，而非讓家長自由查任意 versionId。

建議處理順序：驗證登入與目前帳號 → CanReadParentTaskAsync → 從任務取得固定 questionnaireVersionId → 共用核心取得並驗證版本快照 → 投影為填答專用 DTO。若撤銷發生於讀取途中，後續每次請求仍須重新驗證；不能承諾已送到瀏覽器的資料可被收回。

建議回應僅包含：

- 任務／版本：taskId、caseId、questionnaireId、questionnaireVersionId、questionnaireTitle、versionNumber、respondentRole、assignmentRound、taskStatus。
- 填答定義：說明文字及有固定順序的題目／選項；保留 questionId、選項 value 供既有答案驗證使用。
- 不包含 ScoringDefinition、計分規則、其他人的草稿或答案；不直接序列化 QuestionnaireVersion Entity。

請 16 確認快照內文字、題型、選項與順序的正式 DTO，以及是否由共用核心提供投影方法。目前 ListAsync 只有已發布版本摘要，不含題目；FindVersionAsync 回 Entity，也不做授權，不能直接當對外 API。

版本應從任務固定取得，不自動改成最新版本。已指派版本之後 RETIRED 是否仍可繼續填寫，以及 SUBMITTED／CANCELLED 任務是否可讀題，須明確訂出規則；本提案不改既有任務讀取條件，也不以「只查 PUBLISHED」意外截斷歷史任務。

### 錯誤與前端處理草稿

| 情況 | 已有／建議行為 |
| --- | --- |
| 未登入／登入失效 | 401；現有框架可能沒有 JSON，前端仍須識別 HTTP 狀態、清除受保護畫面並要求登入。 |
| 無有效綁定／錯角色／錯院所／已撤銷 | 拒絕讀取，不洩漏題目或孩子資料；具體 403／404 與 code 由雙方統一，不假設已有新代碼。 |
| 任務不存在或不可存取 | 統一對外結果，避免透過錯誤差異列舉他人任務。 |
| 版本不存在／快照不支援 | 不顯示空白問卷或換最新版本；屬伺服器資料問題，統一安全錯誤，詳細診斷只寫伺服器紀錄。 |
| 資料庫或網路失敗 | 提示重試讀取，不補虛構資料；不得據此產生已儲存／已提交狀態。 |

這一輪僅規劃 GET；正式 Draft／Submit／Response 待作者模型及交易接口確認後接入，不複製教師儲存流程。

## 5. 整合驗收案例（全部為待執行）

下列是驗收規格，沒有以本次唯讀比對代替測試。離線測試可先驗證身分與輸出；交易並發、Migration、外鍵及重啟保存須在整合模型與真實 MySQL 下另行驗收。

| 編號 | 操作／情境 | 預期 |
| --- | --- | --- |
| R01 | 未登入、過期 JWT、停用帳號／院所讀取 | 拒絕且沒有個案、題目、草稿內容。 |
| R02 | 未驗證家長、未綁定、未完成綁定、已撤銷關係 | 拒絕任務與讀題。 |
| R03 | 修改 caseId／taskId 指向他人或跨院所孩子 | 拒絕；不因知道 Guid 就可讀取。 |
| R04 | 同家長兩個孩子快速切換、舊請求晚返回 | 只顯示目前選取孩子；不殘留前一孩子內容。 |
| R05 | 無任務、多輪次、混合 PARENT／TEACHER／CANCELLED 任務 | 空清單正確；只列允許的家長任務，輪次不混算，不附答案。 |
| R06 | 任務版本不同於目前最新版本 | 讀取任務固定版本；不暗中替換題目。 |
| R07 | 快照毀損、未知題型、版本資料缺失 | 安全失敗，不回傳 Entity／計分資料，不讓空表單被視為有效問卷。 |
| R08 | 已指派版本 RETIRED、任務 SUBMITTED／CANCELLED | 依共同確認的狀態矩陣驗收；目前不宣稱可用或不可用已定案。 |
| R09 | 改 userId、authorId、role 等前端欄位 | 不得改變可信身分或可讀範圍。 |
| R10 | 家長讀取教師進度 | 僅完成狀態；沒有教師答案、草稿、分數、補充觀察。 |
| R11 | 登出、上一頁／BFCACHE、載入中撤銷 | 清除舊畫面；恢復頁面重新驗證；後續請求拒絕已失效身分。 |
| R12 | 401／403 非 JSON、503、斷線、解析失敗 | 可理解錯誤與重試；不顯示上次資料或成功提示。 |
| W01 | 家長甲草稿，家長乙同樣有 Case 權限 | 乙不能讀／覆寫甲草稿；新照顧者啟用方式待規則定案。 |
| W02 | 兩次草稿更新使用相同舊 revision | 衝突且保留較新內容。 |
| W03 | 提交後重送相同 key／不同內容／不同作者 | 同意圖不重複建立答案；不同內容／作者不能取得他人結果；精確 key 範圍待定。 |
| W04 | Guardian 撤銷與 Draft／Submit 並行 | 依共同鎖定協定決定先後；撤銷先完成不得寫入，提交先完成保留答案，撤銷後拒絕新存取。 |
| W05 | 教師授權撤銷、單份完成、全部完成 | Session／Grant／指定任務一致；全部完成停止寫入，回執可讀規則依正式契約。 |
| W06 | 提交交易中發生資料庫錯誤 | 答案、任務、草稿及相關狀態一致回復，不回成功回執。 |
| D01 | 整合後 Migration／MySQL 8.4.11／網站重啟 | 由 16 統整部署；模型、外鍵、作者唯一性與資料保存符合契約。 |

## 6. 現在只需要向 16 確認的重點

1. 任務清單沿用現有 API；請確認「依 taskId 取得固定版本填答定義」的共用方法／DTO，避免直接回 Entity。歷史版本及任務狀態讀題規則一起列明。
2. 草稿／答案由可信登入 userId 記錄作者；不同照顧者不得接續或覆寫他人資料。新照顧者遇到舊草稿，先拒絕接手、不刪除原資料；是否新建任務或另有交接流程，待共同決定，不能自動轉移作者。
3. 11 提供 Guardian 身分／範圍檢查，16 將其整合進共用寫入交易；請共同列出撤銷與提交的鎖定順序，以及誰調整撤銷端。僅提供 bool 檢查不足以完成並發保護。
4. 教師身分、同意及授權入口由 11 接，共用草稿／提交與模型由 16 統整；請標出可正式呼叫的方法、錯誤格式與尚未定案的 Session／CSRF 項目。沒有定案前不放寬角色或移除現有保護。

## 7. 本次完成範圍與下一步

本次只完成固定版本程式比對、文字合併模擬、讀取契約草稿及驗收案例；沒有改產品程式、執行整合後測試、產生 Migration、連雲端資料庫或推送。

下一步先確認讀題 DTO／版本狀態規則，再於獨立整合分支串接家長讀取。作者、交易與同意規則確認後，才接寫入及教師正式流程；Migration 仍由 16 統整。
