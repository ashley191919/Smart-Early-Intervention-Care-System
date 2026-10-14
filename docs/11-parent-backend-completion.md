# 11 號後端本輪完成範圍與整合驗收

日期：2026-10-15。分支 feature/case-guardian-management，基底 main a68ed6d。

## 已完成

- Case 新增/列表/詳細/修改，維持 CaseRecord；同院所讀寫、revision 衝突、出生日期及身分證檢查碼、地址驗證。
- Guardian 共用聯絡資料、多位照顧者、最多一位有效主要聯絡人；逐孩子獨立的 CaseGuardian，主要聯絡唯一鍵涵蓋 ACTIVE AND is_primary_contact=1。
- 指定手機的不限時一次性邀請碼、hash 保存、交易兌換、撤銷。重核准關係仍要重新邀請綁定。
- Parent scope：可信 JWT userId、DB ACTIVE 帳號/院所/角色、已驗證手機與有效已綁定關係；任務讀寫 scope 分開；撤銷保留原始資料並阻擋存取。
- 家長 account 模式 API 首頁、孩子、聯絡資料修改、綁定入口、教師完成狀態、任務清單；多頁孩子清單重查 scope，登出清除分頁 token 並返回 account 登入入口。
- 手機登入 API/客戶端/簡訊介面架構完成。實際 SMS 發送依使用者本輪確認尚未申請，預設 ParentSmsUnavailable 回503，不寫 PhoneVerifiedAtUtc、不發 JWT、不提供萬用碼。
- 真正 JWT 簽章的隔離 HTTP 測試包含錯誤 key/issuer/audience、到期、偽造 role、停用帳號、手機未驗證。SMS 測試替身只存在測試程式，未註冊到網站。

## OTP 介接契約

- POST /api/parent/auth/otp/request：body { phone }；成功只回 challengeId、expiresAtUtc、resendAfterSeconds，無OTP明碼。
- POST /api/parent/auth/otp/verify：body { challengeId, code }；6位數；provider 驗證成功後以其回傳的手機查可信既有 PARENT 帳號，回 userId、role=PARENT、accessToken、expiresAt。
- 手機/帳號不能由 verify body 的 phone/userId/organizationId 指定。
- 正式登入頁 /?role=parent&mode=account；登入後 /parent-workspace.html?mode=account。原不帶 account 的瀏覽器展示流程保留獨立，不能呼叫正式帳號驗證入口。
- IParentSmsVerification 的正式 adapter 必須綁定原手機、過期、重送/嘗試限制、一次性消耗，且不在API或日誌回傳明碼。API另有IP每分鐘5次request與10次verify限制。
- 目前只接受一筆唯一、有效同院所既有 PARENT 帳號；不存在、重複手機帳號或已停用均阻擋。首次家長帳號建立/院所歸屬規則仍須定案，不自動猜測院所或把 Guardian 當成已驗證帳號。
- 單一分頁JWT登出目前只清除本機憑證，不等於立即撤銷已發出的JWT；token期限30分鐘，API重查帳號與scope。伺服器會話/即時token撤銷若需要，須與7的身分模組統一設計。

## 給 7

Case/Guardian 欄位及建立步驟見 11-case-api-for-7.md。醫療角色暫限同院所，細分 assigned case/科別與角色允許動作需7確認；CaseStatus/歷程不由11修改。Guardian與Case建立是兩個步驟，重試聯絡人不應重建Case。

## 給 16

模型與scope見11-case-guardian-handoff.md。schema新增三表及cases/users欄位，本輪沒有新增migration或修改snapshot。無EnsureCreated對真實DB建表、無seed、無雲端schema變更。

務必處理旧個案院所/必填資料的正確回填。原四份migration尚未涵蓋此模型，不能直接用本分支對舊DB做正式驗收。權限helper是前置檢查；家長Draft/Submit的交易內還需要再次鎖定並檢查有效綁定，避免與撤銷競爭。Response閱讀另外核對可信提交者userId，不以姓名或現在Guardian推定作者。

家長問卷填寫/提交/結果閱讀、正式TeacherGrant與同意檢查仍由16整合，不重新建立第二套表單系統。未定正式填表路徑前，account任務列表不導向 mode=demo。

## 可立即執行的隔離驗收

本輪驗證：101項Case/Guardian/JWT/OTP介接檢查、77項問卷交接檢查、Phase 2-1規則測試、26項JS測試；建置0警告/0錯誤。

在專案根目錄：

```powershell
dotnet run --project tests/CaseGuardianHarness.csproj --no-launch-profile
dotnet run --project tests/QuestionnairePhase21Harness.csproj --no-launch-profile
dotnet run --project tests/QuestionnairePhase22Harness.csproj --no-launch-profile -- --case-guardian-schema-handoff
node --test tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs tests/ParentAccountClient.test.cjs tests/ParentOtpClient.test.cjs
```

MySQL 8.4.11 僅做Pomelo模型檢查；關聯資料與HTTP使用SQLite/TestServer。實際雲端交易、generated unique index、MySQL FK及schema仍待16工具/人工檢查，不能拿這些結果當真實MySQL驗收。

瀏覽器驗收使用獨立SQLite fixture（僅測試程式）1440×900：本人孩子、共享姓名修改、待填/填寫中/提交統計、對話框邊界、登出、正式OTP未設定回503、不生成瀏覽器驗證碼。測試server驗收後關閉。

## 剩下真正需要的整合

1. 7 review醫療範圍與Case頁API串接。
2. 16 review模型、資料回填、產生migration；以CloudMigration preflight/人工審查通過後統一套用。
3. 16整合家長Draft/Submit/Response作者與teacher consent/grant。
4. 決定家長首次帳號建立及院所歸屬；申請SMS後實作真實provider、端到端登入。
5. 三人使用虛構資料驗收MySQL+JWT：Case→Guardian→手機驗證→一次性綁定→任務→草稿/提交→醫療結果；撤銷後禁止存取、保留歷史。

雲端連線時每人仍用自己的SSH通道及日常MySQL帳號；root/migration帳號不作網站runtime。現有雲端max_connections=30，runtime連線池建議每個程序上限5，避免多人測試占滿。此分支沒有保存或改寫任何真實連線字串。
