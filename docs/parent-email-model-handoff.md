# 給 16：家長 Email 身分與邀請模型交接

日期：2026-10-19。使用者已確認改 Email OTP、已自行完成 Gmail SMTP 驗證與收信測試。本文件描述本機未提交的實作；Commit 待保存後提供。不表示已合併或已部署。

## 本輪完成

- 沿用同一個 User，不另建第二套帳號。新增可為 null 的 Email、NormalizedEmail、EmailVerifiedAtUtc。
- ParentEmailAuthenticationService：OTP 證明後查唯一既有 PARENT 帳號與有效院所，重新確認帳號／角色／院所及信箱再記驗證時間並發 JWT。未知、停用、跨院所重複信箱拒絕；不自行註冊、不從前端挑院所。
- CaseActor 保留 Phone／PhoneVerified 供既有呼叫相容，新增 Email／EmailVerified；所有家長 Scope 改以 EmailVerified 判定，手機驗證不能取得 Email Scope。Actor 每次查目前 DB 身分，信箱與查找欄位不一致時拒絕家長資格。
- 邀請新增 TargetEmailNormalized，兌換比對已驗證 Email 與指定 Guardian／孩子／院所。保留 TargetPhone 作既有資料相容，不作新邀請的身分依據。
- 兌換內重新確認 User、院所及 PARENT 角色；沿用邀請一次性消耗與交易。這不是問卷 Submit／Guardian 撤銷的最終共用鎖定協議，該部分仍待整合。
- Guardian 聯絡 Email 更新不會改 User.Email 或自動轉移帳號。已綁定的 Guardian 不允許直接更改 Email；先保留需要重新核准／驗證的拒絕行為，完整換信箱／跨帳號轉移流程仍待實作。
- 新增 ParentEmailAuthenticationService 的 DI，但尚無公開 Email 登入路由；原手機 adapter 仍為不可用，既有手機驗證不會開放 Email Scope。前端尚未切換。

## Schema 差異（請由 16 統整 Migration）

| 表／欄位 | 新模型 | 意義 |
| --- | --- | --- |
| users.email | nullable varchar(254) | 核准的登入信箱 |
| users.normalized_email | nullable varchar(254)，utf8mb4_bin | 應用程式以完整地址 ToLowerInvariant 查找；不刪點號／加號 |
| users.email_verified_at_utc | nullable datetime(6) | Email OTP 證明成功的 UTC 時間 |
| ix_users_normalized_email | 非唯一索引 | 查找用；重複 PARENT 帳號先拒絕登入，唯一性／跨院所帳號模型仍可共同調整 |
| parent_binding_invitations.target_email_normalized | nullable varchar(254)，utf8mb4_bin | 邀請綁定的指定信箱查找值 |

未刪除 phone／phone_verified_at_utc／target_phone，未新增 Migration 或改 ModelSnapshot，未使用 EnsureCreated 或手寫 SQL 改真實 DB。

**新模型不能直接套在舊結構驗收。** 新 User 欄位會被 EF 實體查詢讀取，包含共用 Authentication 使用 User 的路徑；16 完成統一 Migration／回填前，勿把此模型直接當成可部署網站版本，否則可能出現缺欄位錯誤。隔離測試使用獨立 SQLite，不碰現有 earlycare_dev。

## 舊資料與開通規則

1. 保留既有 User／Guardian／CaseGuardian IDs、綁定與作者識別，不重新建立或轉移帳號。
2. 舊手機驗證時間不能複製成 email_verified_at_utc；初值應為 null。
3. User.Email 由核准流程確認後填入，NormalizedEmail 使用同一 EmailAddressIdentity.LookupKey。Guardian.Email 是聯絡資料，不得自動回填為「已驗證」登入信箱。任何信箱異動必須同時清除 EmailVerifiedAtUtc，重新驗證；不得只改兩個信箱欄位卻保留舊驗證時間。
4. 舊手機邀請的 TargetEmailNormalized 保持 null，因此不接受 Email 兌換。醫護核對後明確撤銷舊邀請並重新發行，不讓本輪 Migration 自動消耗或改寫歷史邀請。
5. 既有已綁定 Guardian 可在原 User 具有效 Email 證明後取得原 Case Scope；不要求重新轉移孩子，也不因此開放別人的草稿／答案。
6. 目前只接受唯一既有家長帳號。首次帳號與院所核准來源、跨院所一信箱多帳號及信箱轉移規則仍未定案；維持拒絕，不自動選一筆。

## 隔離驗收

```powershell
dotnet run --project tests/ParentEmailHarness.csproj --no-launch-profile
dotnet run --project tests/CaseGuardianHarness.csproj --no-launch-profile
```

- Email 寄信與 OTP 核心：60 項離線檢查。
- Case／Guardian／Email：118 項關聯／HTTP／JWT／模型檢查，涵蓋原有 101 項與 Email 新增情境。既有手機登入的隔離測試保留，但改驗證其不能取得 Email Scope。
- Email 新增情境：可信 OTP 對應核准帳號、未知／非家長／停用／跨院所重複帳號拒絕、一次性 proof、驗證不自動綁孩子、錯信箱不消耗邀請、舊手機邀請拒絕、手機證明不標記 Email、信箱欄位不一致失去 Scope。
- 問卷 Phase 2-2 handoff 模式：79 項通過，允許清單只增加已宣告的 Email 欄位與索引；預設部署檢查仍會拒絕尚未產生 Migration 的模型差異，沒有略過檢查或修改 Snapshot。

上述沒有正式 Gmail OTP 登入 API、沒有真實 MySQL／Migration 驗收；不得將此結果當作正式端到端登入完成。

## 接續順序

核對此模型及資料回填 → 16 統一 Migration → 接 Email request／verify API、IP 限流與前端 → 登入、孩子綁定、重啟與撤銷完整驗收。單程序 OTP 只適用單程序環境，部署多程序前須共用狀態儲存。
