最新完整交接及剩餘範圍見 [11號後端完成範圍](11-parent-backend-completion.md)。

# 11 號第二階段：Case／Guardian／家長介接交接

日期：2026-10-13。工作分支：feature/case-guardian-management。基底：main a68ed6d。

## 已實作及驗證

- 沿用 CaseRecord；新增、分頁列表、詳細、基本資料修改。CaseCode、CaseId、NEW 初始狀態由伺服器產生；不接受前端決定院所或狀態。revision 防止舊資料覆寫。
- Guardian 聯絡資料與 CaseGuardian 關係，醫療端可新增、讀取、修改、撤銷關係。已綁定帳號的手機不可直接修改；未綁定聯絡手機修改會撤銷舊邀請。
- 家長綁定邀請：128-bit 隨機碼，只保存 SHA-256 雜湊，原碼僅建立時回傳。不限時（expires_at_utc=NULL），與不限時的教師授權分開。指定手機、Case、Guardian；可撤銷；交易內一次性消耗及綁定，並行兌換只有一筆成功。
- 家長取得自己的個案、聯絡資料，修改本人聯絡姓名；不能修改孩子核心資料。姓名修改同樣檢查 revision。
- CaseAccessService 提供給 16 的共用判斷；每次重查有效帳號、角色、院所及綁定，不相信前端 userId、角色，也不只依 JWT 舊角色判斷。
- 家長教師完成度為四個欄位的專用 DTO：taskId、questionnaireTitle、assignmentRound、isCompleted。沒有逐題答案、補充觀察、分數、草稿。
- 16 現有問卷指派／任務／完成摘要 API 已改用院所範圍的 CaseAccessService，移除僅固定 Development Case 的授權捷徑。原本 ADMIN／CASE_MANAGER 角色限制保留，MEDICAL_STAFF 是否能指派仍需 7、16 確認。
- 家長畫面的 account 模式讀取 API：自己的孩子、聯絡姓名、邀請綁定、教師完成狀態、家長任務清單。沒有憑證時顯示請登入，不回退到展示資料。

## 尚未完成，不能宣稱第二階段全部上線

1. 16 統一產生、檢查並套用 MySQL migration。此次沒有產生 migration、建表 SQL、修改現有資料庫或連雲端。
2. 套 migration 後的真實 MySQL／JWT HTTP 驗收。當前關聯及 HTTP 測試用獨立 SQLite／TestServer，並檢查 Pomelo 模型，不能取代 MySQL 實測。
3. 家長正式 SMS／首次帳號建立：使用者确认尚無 SMS 服務，真實發送延期；本輪已有OTP API/介接，預設503。不能把前端顯示的驗證碼或任何前端成功訊息寫成 PhoneVerifiedAtUtc。
4. 16 將家長填答入口、Draft／Submit／Response 與 CanWriteParentTaskAsync 整合。此次只有安全的家長任務清單，不另外建立第二套填答模組。
5. 7 將自己的 Case 頁面串接 Case API，Workflow／CaseStatus 由 7 維護。
6. 正式 TeacherGrant 由 16 介接家長綁定與同意檢查；現有教師開發路徑未變成正式家長授權。

## 給 16：schema 交接（唯一 migration 出口）

新增表：

| 表 | 主鍵及用途 |
| --- | --- |
| guardians | guardian_id Guid；院所、照顧者姓名／手機、可空 parent_user_id、revision、UTC 時間 |
| case_guardians | case_id + guardian_id 複合主鍵；bound_at_utc、關係、ACTIVE／REVOKED；active_case_id generated 唯一索引限制一位有效主要聯絡人 |
| parent_binding_invitations | invitation_id Guid；CaseGuardian 複合外鍵、唯一 code_hash、指定手機、發出者、到期／消耗／撤銷 UTC、消耗帳號 |

既有表欄位：

- cases.organization_id：可空 Guid，organizations 外鍵。舊資料先保留 NULL；NULL 院所的個案不能經正式 Case API 存取，16 應依已確認歸屬回填，不能全部自動歸到某一家院所。
- cases.revision：uint，初始 0，並行更新檢查。
- users.phone_verified_at_utc：可空 UTC。舊資料預設 NULL，不自動將已有 Phone 視為驗證。
- 所有新外鍵 Restrict，沒有 Cascade 刪除個案、照顧者、邀請或帳號。

模型變動前不要用此分支重啟已接舊 schema 的登入／資料庫驗收：User 查詢需要新欄位，尚未 migration 會出現服務暫時無法使用。main 未修改。

16 先合入 Entity／Configuration，review 既有資料回填計畫，再依團隊的獨立 migration 工具產生／套用；不要用 EnsureCreated 對現有 earlycare_dev 建表。

## API 契約

所有 Id 為 Guid；JSON camelCase；時間 UTC Z。需要有效 Bearer JWT。讀取不存在或不屬於自己的個案回 404，避免洩漏其他個案是否存在；問卷模組仍依原契約回 403。資料库未就緒回 503，不回傳 SQL 或連線秘密。

| API | 用途／權限 |
| --- | --- |
| POST /api/cases | 醫療帳號建立本院所 Case |
| GET /api/cases?page=1&pageSize=20 | 醫療帳號本院所；家長只查有效綁定 |
| GET /api/cases/{caseId} | 相同範圍的詳細 |
| PUT /api/cases/{caseId} | 醫療修改基本資料；需 revision，不改 CaseStatus |
| GET/POST /api/cases/{caseId}/guardians | 醫療讀取／新增照顧者 |
| PUT /api/cases/{caseId}/guardians/{guardianId} | 醫療修改姓名／聯絡／關係，需 revision |
| POST /api/cases/{caseId}/guardians/{guardianId}/invitations | 醫療產生一次性邀請碼，回 invitationId、invitationCode、expiresAtUtc |
| POST /api/parent-binding-invitations/{invitationId}/revoke | 醫療撤銷尚未使用邀請 |
| POST /api/cases/{caseId}/guardians/{guardianId}/revoke | 醫療撤銷該個案與照顧者關係 |
| GET /api/parent/cases | 已完成手機驗證家長的孩子清單 |
| POST /api/parent/bindings | body: invitationCode；手機匹配，完成一次性綁定 |
| GET /api/parent/profile | 本人各 Guardian 聯絡記錄陣列 |
| PUT /api/parent/profile/{guardianId} | body: displayName、revision；只改本人姓名 |
| GET /api/parent/cases/{caseId}/tasks | 本人個案的 PARENT 任務清單，排除 CANCELLED；不含答案 |
| GET /api/parent/cases/{caseId}/teacher-completion | 本人個案的教師完成狀態，排除 CANCELLED |

Case 建立／修改：childName、birthDate（yyyy-MM-dd）、sex（MALE／FEMALE／UNKNOWN）、nationalId（國民身分證格式及檢查碼，與 7 的前端一致）、address（1–300 字元）全部必填；修改另外傳 revision。
Guardian：displayName、phone（台灣手機）、email（必填，最長 254）、relationship（FATHER／MOTHER／GRANDPARENT／RELATIVE／GUARDIAN／CAREGIVER／OTHER）、isPrimaryContact；修改另外傳 revision。

## 給 16：權限接口

注入 CaseAccessService：

```csharp
await caseAccess.CanReadAsync(User, caseId, cancellationToken);
await caseAccess.CanWriteParentTaskAsync(User, taskId, cancellationToken);
```

第二項僅允許 DB 中為 PARENT、phone_verified_at_utc 非空、有效 CaseGuardian 關係、同院所、PARENT 任務且 PENDING／IN_PROGRESS。每次 Draft 寫入／Submit 前必須檢查，提交後結果讀取需使用獨立閱讀權限，不拿寫入檢查代替讀取。

現有 User.OrganizationId 必填，因此本輪帳號只能對應同院所個案；跨院所家長登入、重複手機、手機變更與主要聯絡人已確認每名孩子只有一位，其他規則需後續共同確認。帳號建立不能從前端任意提交 organizationId，OTP 模組需由已核准的邀請／院所流程決定。

## 本機程式驗收（現在可執行，不需 MySQL 或 User Secrets）

```powershell
dotnet run --project tests/CaseGuardianHarness.csproj --no-launch-profile
dotnet run --project tests/QuestionnairePhase21Harness.csproj --no-launch-profile
dotnet run --project tests/QuestionnairePhase22Harness.csproj --no-launch-profile -- --case-guardian-schema-handoff
node --test tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs
```

CaseGuardianHarness：101 項檢查，包含實際 HTTP 路由、401／403／404、兩個独立連線競爭兌換邀請、跨連線持久化與教師資料最小回應。
Phase 2-2 的 handoff 參數會確認只有上述三張新表及 cases/users 宣告變更，未變動 Questionnaire schema。預設不帶參數仍要求沒有 pending model changes；16 合入 migration 後才應執行預設部署檢查。

## 家長前端接口

正式資料模式網址：/parent-workspace.html?mode=account。
未實作正式 OTP 前，不提供假 JWT、萬用登入碼或自行開放權限的前端按鈕。登入頁與工作台共用 parent-session.js。正式登入 API 回傳 accessToken 及 role=PARENT 後，呼叫 EarlyCareParentSession.signIn(response)，保存伺服器回傳 token 並導向 account 模式。展示驗證不會呼叫此方法。此 token 僅放当前分頁，登出移除；伺服器仍逐次驗證 JWT 與資料庫權限。原前端展示流程不會產生正式 token。

家長資料頁已接 API，正式登入後的完整瀏覽器流程待 OTP 與 migration 到位後驗收。正式填表導航由 16 提供並接入任務，不把展示版本／taskId 當正式資料。


## 2026-10-13 本輪可解決項目補齊

- GET /api/parent/cases 增加 relationships 陣列，僅含登入家長自己的 guardianId、relationship、isPrimaryContact，不包含其他照顧者資料。
- account 首頁顯示個案狀態、與孩子關係，根據實際 PARENT 任務計算待填、填寫中、已提交與側欄未完成數量；現在明確統計全部輪次。
- 每次頁面切換重查 Case 任務權限，拒絕撤銷後存取；切換孩子時阻止較舊的非同步結果蓋回新畫面。
- 姓名修改遇 409 保留輸入、停止重送，提供重新載入最新資料；經 SQLite API／瀏覽器驗收重新確認後保存成功。
- 共用 parent-session.js／parent-account-client.js 不產生 OTP 或 JWT，拒絕非家長登入結果，過期清除 token；舊帳號晚到的 401 不清除新登入憑證。
- 額外 7 項 Node 測試，加既有 11 項共 18 項通過。
- 瀏覽器驗收使用 tests/CaseGuardianHarness.csproj 的 --serve-browser-fixture 模式：僅在 127.0.0.1:5182 啟動獨立 SQLite 假資料與測試身分，沒有連現有 MySQL，也不是正式登入；不屬於應用程式的 API。結束後停止此測試程序。
- 需要組員討論項目见 [團隊討論清單](phase2-team-decisions.md)。


## 本輪已定案規則與 16 介接

- Case 新增 national_id／address，Guardian 新增 email，必填且 API 會驗證。
- Guardian 的 OrganizationId+Phone 唯一，避免同一電話反覆建立重複聯絡資料。GET /api/guardians 是醫療帳號本院所分頁聯絡清單。
- POST /api/cases/{caseId}/guardians/{guardianId}/link，body relationship、isPrimaryContact（預設 true），連到既有共用 Guardian。若曾撤銷，医護可重新連結，但仍需新邀請完成該孩子綁定；不自動授權。
- 每名孩子只有一位有效主要聯絡人，active_case_id 為 stored generated column（ACTIVE 且 is_primary_contact=1 時 case_id，否則 NULL），以唯一索引支援舊撤銷歷史與並行限制。
- bound_at_utc 為該孩子與家長綁定完成時間。所有家長讀寫 scope 要同時檢查 ACTIVE 且 bound_at_utc 非空。
- 邀請 expires_at_utc 改為 nullable，建立新碼回 null，不限時；一次性、撤銷、手機匹配及 transaction 還在。撤銷 CaseGuardian 同時撤銷該孩子未使用邀請，不影響其他孩子的共用 Guardian。
- 照顧者姓名、手機、Email 共用；孩子地址保存在 Case，若地址也要共享需 7 再確認欄位歸屬。
- 畫面目前家長填表網址 /parent-questionnaire.html?mode=demo&task=... 仍是展示流程，未當作正式 endpoint 串接。
- 101 項關聯／API 檢查通過，含一年後兌換不限時碼、共享聯絡資料、逐案綁定及單一主要聯絡人限制；MySQL 验收仍需 16 migration。
- 親自提交答案閱讀／撤銷後紀錄政策使用者已定案，詳見團隊規則確認文件。QuestionnaireResponse 目前缺 Parent 提交人 ID，16 需要確認可信作者記錄方式。


答案政策補充：家長可讀取本人已提交原始答案，不可修改；教師仍只完成狀態。撤銷綁定保留原紀錄但立即禁止存取；醫護重新核准關係仍不自動放行，必須重新完成該個案邀請綁定。

新增 CanReadParentTaskAsync(User,taskId,ct)：家長 PARENT 任務閱讀 scope，包含 SUBMITTED；與 CanWriteParentTaskAsync 分開。此方法不等於 Response 作者檢查，16 的答案 API 還必須對照可信的提交人 userId。若重新綁定的是不同帳號，不可將舊家長答案當作新家長本人答案開放。


## 2026-10-15：本輪 ①～④ 審查補齊

- 基底已重新 fetch 核對 origin/main=a68ed6d；feature/case-guardian-management 已存在，原未提交修改完整保留，未合入其他組員新分支。
- 新增後端身分證檢查碼驗證，直接呼叫 API 也不能繞過 7 的前端驗證。
- 多位照顧者可保留，單一有效主要聯絡人限制只套在 ACTIVE 且 is_primary_contact=1 的關係。GuardianInput 及 GuardianLinkInput 的 isPrimaryContact 預設 true；若為其他照顧者，必須傳 false。目前允許尚未配置主要聯絡人的過渡狀態；不自動升格其他照顧者。
- 舊 revision 回 409；MySQL 唯一鍵衝突回 DATA_CONFLICT/409，不誤報 503。共用手機重複更新回 CONTACT_ALREADY_EXISTS。
- 101 項關聯/API 檢查、77 項 Phase 2-2 交接檢查、Phase 2-1 規則測試及 26 項 JS 測試通過。101 項使用獨立 SQLite/TestServer；8.4.11 僅檢查 Pomelo 模型，未連真實雲端 DB。
- 沒有新增 migration、修改 ModelSnapshot、執行 seed 或碰雲端 schema。Guardian 模型變更 pending 是預期結果；不能在舊 schema 上當成可直接部署的版本。
- 醫療角色目前 ADMIN/CASE_MANAGER/MEDICAL_STAFF 限同院所；跨院所禁止，NULL OrganizationId 舊個案不放行。assigned case/科別或更細權限仍需 7 確認。問卷指派沿用原 ADMIN/CASE_MANAGER，不自行新增醫師指派權限。

本輪補充：101項關聯/API/JWT/OTP介接檢查；26項JS測試。OTP沒有新增schema，僅在真實provider驗證後更新既有待交接users.phone_verified_at_utc。
