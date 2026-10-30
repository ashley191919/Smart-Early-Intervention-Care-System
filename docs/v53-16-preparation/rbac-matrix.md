# 16-06：RBAC 現況與資源邊界

依 `bb74e72` 原始碼盤點；沒有讀取實際 DB RolePermission，也沒有修改任何授權。程式支援、需求已確認、院方尚待確認是不同狀態。

## 角色／Permission／Policy 盤點

- AuthorizationSeed 有 ADMIN、CASE_MANAGER、MEDICAL_STAFF、PARENT、TEACHER 五個角色名稱；目前模型為 User.RoleId 單一角色，並非多角色。ADMIN 在 PermissionService 沒有隱含全權。
- PermissionService 以當前 User→Role→RolePermission→Permission 查詢；只接受精確 ACTIVE 與 Ordinal Permission，不信任 JWT role／permission，不快取。實際指派未查 DB，不能宣稱哪些角色已獲授權。
- `AddFirstBatchPermissionPolicies` 定義四個登入＋PermissionRequirement Policy：`Permission:case.read`、`Permission:questionnaire.task.read`、`Permission:case.status.update`、`Permission:case.status_history.read`。註冊 Policy 不等於 seed Permission 或 RolePermission。
- `AddScopedCaseTaskReads` 重用 Handler，要求前兩個 Policy 的最終內容精確符合契約；`ICaseTaskReadScope` 預設 Deny。Scope 需 07 實作可信 principal、目前帳號／院所、Case 範圍及 Task 真正隸屬 Case；Permission 成功不能取代 scope。
- 舊 `docs/roles-permissions.md` 的 case.create、case.update、form.read／assign／fill／export、teacher.invite、dashboard.read 是候選名稱與候選配置，不是本批新授權；其中 form.read 與 questionnaire.task.read 不可視為同義字。role.read、permission.read、role_permission.update 僅未來提案。
- 教師採 Grant／Task／Session scope，不可將教師 Session 加上 userId claim 後變成一般醫護／家長 RBAC 身分。

## 操作矩陣（條件允許不表示已接正式 API）

| 資源／操作 | PARENT | TEACHER | MEDICAL_STAFF | CASE_MANAGER／ADMIN |
|---|---|---|---|---|
| Case 讀取 | 僅有效綁定最小資料；正式 DTO／Adapter待11 | 僅授權任務所需最小識別，無一般Case權 | Permission＋同院所／核定Case scope；07接入待確認 | 各自Permission＋scope；ADMIN無隱含全權 |
| Case 建立／修改／狀態 | 不由家長問卷核心提供 | 不提供 | 角色、欄位與狀態轉換由院方／07確認 | 不由本批推定；舊Controller角色檢查不等於正式許可 |
| Task 清單／狀態 | 本人PARENT任務；教師僅完成狀態 | 僅Grant指定TEACHER任務 | questionnaire.task.read＋scope候選；現有Controller拒絕醫護 | 現有Controller僅ADMIN／CASE_MANAGER且限Development固定Case；正式scope未接 |
| Task 指派／取消 | 本批無此授權 | 不提供 | 有Case權限的醫護決定指派／複評是共同契約方向；具體Permission與角色配置待核 | 未確認，不新增grant；取消／有效輪次待07 |
| Draft 讀／保存 | 有效綁定＋適用同意＋固定PARENT版本＋本人作者；首次rev0 | Grant＋Session＋指定Task／版本；rev檢查 | 不因form.fill候選而允許讀／代填家長教師Draft | 不放行代填／接管 |
| Response 提交 | 本人Task完整payload；原子提交與作者＋key去重 | 指定Task完整payload；Grant＋key去重 | 醫護是否另有自身量表Task待院方確認，不能代填 | 不放行 |
| Response 收據 | 本人且當前scope仍有效；不含答案 | 依既有Session／收據窗口，不等於永久讀取權 | 正式醫療讀取／匯出另審 | 另審，不能拿家長receipt擴權 |
| Response 答案／分數／匯出 | 不能看教師答案；本人答案專用讀取尚未提供，不以receipt代替 | receipt不含答案，不推定可重讀已提交問卷 | 同院所／角色／欄位／匯出範圍待院方確認 | 同左，不預設ADMIN全讀 |
| Response 修改／刪除／Excel匯回 | 提交後不可改 | 提交後不可改 | 只修改匯出的Excel、不匯回；原答案保留 | 不新增原答案修改能力 |
| 教師Grant建立／撤銷 | 僅已綁定家長；適用Consent與交易待11 | 無建立他人Grant權 | 不代辦 | 目前個管師不能代辦；ADMIN不例外放行 |

## MEDICAL_STAFF 無法存取 Questionnaire Controller

固定版本 `QuestionnairesController.CheckUser` 查目前帳號，ACTIVE 後只接受 `ADMIN`／`CASE_MANAGER`，其餘 Forbid，因此 MEDICAL_STAFF 即使有候選 form.read 或 questionnaire.task.read 仍遭403。Task／Completion／Assign 還有 Development 固定Case gate；Controller 尚未用上述四個 Permission Policy。這是現況，不是院方核定「醫護不得讀問卷」。

待院方與三人確認：醫護可讀空白版本／Task狀態／原答案／計分哪些欄位；哪些人可指派、取消與匯出；院所／分派範圍如何表示；新Permission名稱及初始化由誰統整；scope在讀取與寫入交易內何時重驗。未確認前保留拒絕，不將MEDICAL_STAFF加入角色白名單，也不刪Development gate來假裝正式接入。

共同契約已明確的限制優先於舊草案：Grant不限時（Session可有期限）；只允許已綁定家長授權教師；家長僅看教師完成狀態；提交後原始答案不可改。其餘候選權限維持待確認。

授權邊界測試見 acceptance-cases.md 的 AZ01～AZ09；最少同時覆蓋 permission true/false × scope true/false、當前停用／改角色、跨院、同Case不同作者、撤銷競爭、DB故障拒絕及回傳DTO白名單。醫療scope與綁定撤銷要測真資料查詢，不能只用替身回false宣稱驗收。
