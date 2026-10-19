# 家長 Email 登入：寄信元件與接續工作

日期：2026-10-19。使用者已決定家長登入改用 Email 驗證碼，取代簡訊。舊文件的「手機 OTP 已定案」以本次指示為準；手機可保留為聯絡資料，不能把 Email 驗證當作手機已驗證。

## 本次已完成

- 新增 MailKit 4.18.1 寄信元件、IParentEmailSender 及 SMTP transport，註冊到網站 DI。
- 讀取既定 Email 設定，連線 587 強制 STARTTLS；465 使用 TLS on connect。保留平台憑證驗證，不回退明文。
- 寄送單一收件者的純文字登入驗證碼信件；不附孩子資料、答案或登入 Token。
- 設定不齊、SMTP 失敗回安全的 EMAIL_SERVICE_UNAVAILABLE，不附原始 SMTP 例外、密碼、驗證碼或收件人資訊；不自動重送。
- 沒有通用任意寄信 API、啟動自動寄信或測試萬用碼。已接 Email OTP request／verify 與前端，但預設關閉，等 Migration 與環境確認才啟用；寄信元件本身不負責驗證身分。
- 未修改 User／Guardian／邀請 Entity、ModelSnapshot、Migration、雲端資料庫或現有網站畫面。

## 第一步：在自己的電腦填寫設定

專案：`EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj`。

User Secrets ID：`33611af7-73e3-42c1-8fe1-0174168ed4a1`。

在 PowerShell 執行下列指令開啟設定檔（不會輸出秘密內容）：

```powershell
notepad (Join-Path $env:APPDATA 'Microsoft\UserSecrets\33611af7-73e3-42c1-8fe1-0174168ed4a1\secrets.json')
```

保留原有 ConnectionStrings／Jwt 等內容，在最外層新增 Email 區塊。這只是要合併進原檔的片段，不能以它取代整份檔案；前一個屬性或區塊後須有逗號：

```json
"Email": {
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": 587,
  "Username": "專題寄件Gmail地址",
  "Password": "Google應用程式密碼",
  "FromAddress": "與Username相同的Gmail地址",
  "FromName": "早療智慧動態照顧系統"
}
```

Username／FromAddress 使用寄信用帳號；收件者則是家長的信箱。Gmail 應用程式密碼去除顯示空格後保存。不要提供給 AI、貼到聊天、截圖、appsettings.json 或提交 Git。正式部署改由主機的秘密設定載入。

若找不到檔案，先確認目前 Windows 使用者及專案 ID，不另存成專案目錄內的 secrets.json。User Secrets 僅供本機 Development 使用，並非加密保管庫。

## 第二步：完整登入尚需實作

### 已完成的驗證碼核心（2026-10-19）

使用者已回報實際收到自寄測試信，SMTP 連線／驗證／此信箱收件已驗收。

新增 IParentEmailVerification／ParentEmailVerification，註冊為 singleton，已接受啟用設定保護的 Email API。伺服器安全亂數產生六位數驗證碼與 32-byte 隨機 challenge；回應只有 challengeId／到期時間／重寄秒數。驗證碼以單程序隨機秘密的 HMAC 保存，不保存驗證碼明碼；成功後原子消耗，錯五次即作廢。有效五分鐘，同信箱六十秒重寄，重寄使舊 challenge 失效；寄送失敗仍保留短暫冷卻，不自動重寄。

此階段 OTP 使用記憶體，網站重啟即失效，不適用多程序獨立驗證。儲存最多 4096 個 challenge／信箱冷卻項目並清理到期項目；API 已接 IP 限制，共同部署仍需確認儲存策略。Email domain 轉小寫、local part 保留；帳號另以完整地址不分大小寫查找，不合併點號／加號別名，不由信箱推定院所。

離線驗證：原寄信元件 28 項，加 Email OTP 核心 32 項，共 60 項通過。涵蓋一次性、到期邊界、錯誤次數、重寄冷卻與舊碼失效、同時驗證唯一成功、重啟失效、不回傳明碼、寄送失敗、儲存容量及到期清理。只使用測試 sender，沒有真實 Email／DB／JWT。

下列清單中「OTP 後端」的單程序核心已完成；完整登入仍須接帳號、範圍、正式儲存與 API，不等於可用 Email 登入。

### 帳號及綁定模型後續進度

已完成 User Email 模型、ParentEmailAuthenticationService、家長 Email Scope、指定信箱邀請、Email API 與前端的隔離驗證。Case／Guardian／Email 共 138 項通過；Schema、資料回填及部署限制見 [Email 模型交接](parent-email-model-handoff.md)，API 與啟用順序見 [Email 啟用及驗收](parent-email-api-acceptance.md)。真實 Migration 尚未完成。以下清單是整體工作範圍，不代表每項都尚未開始。

1. Email 身分規則：在 User 記錄獨立登入信箱、查找欄位與 EmailVerifiedAtUtc；不能直接把可編輯的 Guardian 聯絡 Email 當作可信帳號。信箱大小寫與正規化、唯一性及跨院所重複帳號規則須明確，遇歧義先拒絕，不能猜院所。不要任意移除信箱的點號或加號。
2. OTP 後端：使用安全亂數、短效 challenge、帶伺服器秘密的雜湊驗證碼、原信箱綁定、重寄與嘗試上限、一次性原子消耗、清理及容量限制。不把驗證碼存在前端或回傳 API。多程序／重啟行為需有明確儲存方式。
3. API：沿用 request／verify 分工，request 接 Email；verify 只接 challengeId／code，不接前端指定的可信 Email／userId／院所。發信成功也不等於帳號或孩子權限驗證成功；回應避免洩漏信箱是否已註冊。
4. 身分與綁定：Actor 及家長權限改讀 Email 驗證狀態；邀請改成經確認的 Email／帳號綁定規則，不再依賴 TargetPhone。既有手機邀請不能自動視為 Email 邀請，需訂出保留／撤銷／重新發行方式。
5. 首次帳號及院所：仍待明確核准來源；不能因持有某信箱即自動取得 Guardian 或孩子權限。換登入信箱要重新驗證，不自動轉移孩子或草稿作者。
6. 前端：改 Email 欄位、錯誤提示、重寄倒數、說明文件與測試；驗證後才發可信 JWT，維持原有樣式及登入／綁定分離。
7. 整合與實際驗收：11 完成程式、模型與測試交接，16 統整 Migration 及資料回填，之後才驗收真實資料庫與 Gmail 收信。不自行對現有 earlycare_dev 建表。

## 本次驗證

```powershell
dotnet run --project tests/ParentEmailHarness.csproj --no-launch-profile
```

28 項離線檢查涵蓋設定缺漏、信箱／標頭注入、錯誤驗證碼格式、單一收件者、前導零、有效時間、不洩漏秘密、SMTP 例外與不自動重送，以及取消操作。測試使用捕捉 transport，沒有連 Gmail、沒有真實郵件、沒有讀取 User Secrets、沒有資料庫。

### 本機 Gmail 連線檢查（不寄信）

使用者於 2026-10-19 依序回報本機設定存在、SMTP 連線／驗證兩項 PASS，以及實際收到自寄測試信。這證明寄件者帳號及該信箱收信可用，尚未代表真實資料庫上的 Email OTP 登入完成。

在 `EarlyInterventionCare.Api` 資料夾的 PowerShell 執行：

```powershell
dotnet run --project ..\tests\ParentEmailHarness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-email-check/ -- --check-smtp
```

此選項明確啟用真實 Gmail TLS 連線與帳號驗證，僅從 API 組件對應的 User Secrets 讀設定，不寄信、不啟動網站、不存取資料庫。為避免誤送憑證，只接受 smtp.gmail.com、587／465 及寄件者與帳號相同的設定。錯誤只輸出安全分類，不顯示原始 SMTP 內容。

兩項 PASS 分別代表加密連線與帳號驗證成功；不代表郵件已被送達、Email OTP 可登入或家長綁定已改好。真實寄信／收件尚待另外驗收。

### 實際收信測試（執行後會寄出一封信）

使用者已回報 `--check-smtp` 兩項 PASS。下一步由使用者自行明確執行：

```powershell
dotnet run --project ..\tests\ParentEmailHarness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-email-check/ -- --send-test-to-self
```

只有此旗標會寄信，且固定只寄到設定中與 Username 相同的 FromAddress，不接受任意收件者。寄件主旨為「早療智慧動態照顧系統：寄信連線測試」，純文字且沒有 OTP、孩子資訊或登入連結；不寫資料庫。預設離線測試及 `--check-smtp` 均不寄信。網路失敗可能發生在 SMTP 已接受後，結果不確定時先查收件匣／垃圾郵件，不自動重寄。

SMTP accepted 只代表 Gmail 接受郵件；使用者實際收到後才記為收信驗收成功。此步不代表 Email 登入已完成。

參考：[Google 應用程式密碼](https://support.google.com/mail/answer/185833)、[Gmail SMTP](https://developers.google.com/workspace/gmail/imap/imap-smtp)、[MailKit](https://www.nuget.org/packages/MailKit/4.18.1)。
