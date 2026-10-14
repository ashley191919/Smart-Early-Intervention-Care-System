最新進度見 [11號後端完成範圍](11-parent-backend-completion.md)，包括本輪OTP介接架構與真實JWT隔離驗證；下方保留先前分工核對。

# 第二階段 11 號分工完整核對

核對日期：2026-10-13。分支：feature/case-guardian-management（由 feature/11-case-guardian 更名，修改完整保留，未推送）。

依據：工作區「第二階段分工(新).md」及本分支實際程式。程式實作、測試通過、MySQL 驗收及跨組整合分別列出，不以界面有顯示或離線測試通過推定正式流程完成。

## 六項分工核對

| 分工 | 已有 | 缺漏／驗收條件 |
| --- | --- | --- |
| 1. Case 核心 | 沿用 CaseRecord；新增／列表／詳細／修改 API，Guid 主鍵、院所範圍、revision、初始 NEW | 與 7 的畫面對齊欄位及命名。nationalId、address 已加入必填保存欄位，gender 對應 sex 需轉換；尚未接 MySQL 完整流程 |
| 2. Guardian / Contact | 姓名、手機、關係、主要聯絡標记；家長帳號對應 Case；邀請及撤銷 API | email 已加入必填模型／DTO。重複照顧者合併、主要聯絡人唯一性、換手機流程需共同定案；邀請不限時，已確認 |
| 3. 家長登入前端 | 手機驗證畫面、成功失敗提示、導向工作台、右上角登出 | parent-login.js 仍使用瀏覽器本地生成碼，未呼叫正式 OTP／帳號建立 API、未取得正式 JWT。使用者已同意延期 SMS。登入結果到 account 模式仍待正式介接 |
| 4. 家長首頁 | account 模式讀取已綁定孩子、個案與聯絡資料、教師完成狀態、家長任務 | 已補 CaseStatus、本人照顧者關係、API 待辦摘要及正確問卷數量；待填入口仍待 16 提供正式填表路徑 |
| 5. 家長／兒童基本資料 | 已綁定孩子基本資料讀取；本人聯絡姓名可修改，有 revision 檢查；孩子由醫療端修改 | 最終可修改欄位需定案；身分證、地址、Email 已有 API 支援；姓名更新遇 409 已提供重新載入最新資料，停止重送舊 revision |
| 6. 正式 Case／Guardian 權限 | DB 有效帳號與院所／角色重查；有效綁定；CanReadAsync／CanWriteParentTaskAsync；教師回應只有完成狀態 | 16 的家長 Draft／Submit 尚未呼叫本權限接口；Guardian 新 schema 未由 16 產生及套用 migration；缺真實 MySQL + JWT + 瀏覽器串接驗收 |

## 11 接下來可自己補齊

1. 與 7 的實際欄位契約對齊：nationalId、address、email 的必要性、長度、驗證及可見角色；不能讓畫面接受資料但後端丟棄。欄位確認後由 11 修改 Entity／DTO，再交 16 統一 migration。
2. 已完成：家長 account 首頁個案狀態、關係、待填摘要及 API 數量；載入失敗顯示未取得資料，不使用展示任務。
3. 已完成：基本資料 409 衝突提供重新載入，禁止用舊 revision 持續重送；獨立瀏覽器/API 測試通過。
4. 已完成：parent-session.js 同時供登入頁與工作台載入，驗證登入回應角色／token、保存伺服器回傳 token 並導向 account。正式 OTP API 本身仍延期，展示驗證不會呼叫此正式入口。
5. 正式 OTP 到位後，確認登入成功／失敗、逾期、登出及重登的瀏覽器驗收；目前登出僅清除分頁 JWT，若需立即廢止已發出的 token，需另訂後端會話／撤銷方案。

## 需要合作或已同意延期

- 16：Guardian schema migration、舊 Case.organization_id 正確回填；家長 Draft／Submit 權限接口介接；填表路徑／taskId 契約；正式教師授權及同意查核。
- 7：醫療 Case 頁面接 API；醫療欄位與權限確認；CaseStatus／Workflow。不把這些工作重做進 11 模組。
- 使用者已延期：SMS 服務及正式 OTP；Google Cloud 連線已完成，migration 尚待 16。後端綁定要求可信的 phone_verified_at_utc，不能為測試而把所有既有手機批次標成驗證。
- 跨院所家長：現有 User.OrganizationId 必填，本輪僅支援同院所。新家長在首次登入尚未綁定時如何決定院所／建立帳號，需與身分及邀請流程定案，不接受前端任意 organizationId。

## 目前驗收證據的範圍

101 項 CaseGuardian relational/TestServer 檢查：SQLite，含權限、邀請併發、跨連線保存、API 狀態與最小資料回應。問卷 Phase 2-1／完成摘要與 Phase 2-2 的 schema-handoff 模式、26 項 JS 測試（含 7 項新增家長客戶端檢查）已驗證。

不等於 MySQL transaction／索引／migration 或真實 JWT 全流程驗收完成。Phase 2-2 預設 schema 一致性檢查仍會阻止未產生 migration 的部署；交接模式只允許既定 Guardian 模型變更。

## 建議優先順序

欄位契約核對 → 11 補模型與家長資料 UI → 交付 16 migration／填表契約 → 本機 MySQL 串接驗收 → 正式 OTP（服務申請後） → 完整端到端验收。雲端連線已準備完成，待 16 統整 migration 後再驗收。


## 本輪補齊結果

首頁資料、關係 DTO、問卷數量、資料衝突重新載入及共用登入結果处理已補齊。尚待共同確認的項目另列 [團隊討論清單](phase2-team-decisions.md)。沒有新增 migration、OTP 或正式填答流程。


最新使用者決定：三項資料必填；每孩子單一主要聯絡人；照顧者聯絡資料共用但逐個案綁定；邀請不限時；全部輪次摘要。已實作，剩餘政策與 16 的最新回覆以 phase2-team-decisions.md 為準。
