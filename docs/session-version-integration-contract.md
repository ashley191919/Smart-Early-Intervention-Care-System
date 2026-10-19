# 醫療端 SessionVersion 驗證／所有登入撤銷契約（未啟用）

## 本階段交付及限制

- 沿用 `ISessionSecurityStateStore`、`SessionSecurityState`、`SessionVersionValidator`，不建立第二套版本來源。
- 新增 `SessionVersionClaims`、`ISessionPrincipalValidator`／`SessionPrincipalValidator`、
  `ISessionVersionRevocationStore`、`ISessionRevocationService`／`SessionVersionRevocationService` 與固定結果型別。
- 沒有正式 Store、DI 註冊、JWT 核發修改、事件註冊、Controller 修改或 HTTP 例外轉換 Middleware。
- 現有 JWT 沒有 `sv`，正式登出仍只刪除 Cookie。**本次不修復正式環境的舊 Cookie 重送；不能宣稱撤銷已啟用。**
- Fake Store 只存在測試 assembly；其 lock/CAS 並發測試不證明 MySQL 交易、跨程序或多台部署安全。

## 給 16 號確認的共用資料契約

| 項目 | 提案／必要保證 |
|---|---|
| User 欄位 | `SessionVersion`，C# `long`／Int64 |
| MySQL mapping | 建議 `session_version`、signed BIGINT、NOT NULL；由 16 號確認及建立 Migration |
| 初始值 | 新帳號與既有資料初始化為 1；不是查詢失敗、缺 Claim 或 NULL 時的替代值 |
| 有效範圍 | 1 到 Int64.MaxValue；不可遞減、歸零、溢位循環或重新啟用時重設為 1 |
| 安全狀態快照 | 同一筆 User 的 `UserId: Guid`、`Status: string`、`SessionVersion: long`；不含密碼或 Hash |
| 權威讀取 | 每次驗證讀最新 committed 狀態；不從落後 replica、process cache 或錯誤 fallback 取得版本 |
| 原子更新 | 同一使用者、精確 ACTIVE、目前版本等於 expectedVersion 時，原子且持久地增加 1；否則不修改 |
| 寫入結果 | commit 完成才回 true；條件不符才回 false；連線／逾時／未知 commit 結果必須丟例外 |
| 共同安全異動 | 修改／重設密碼、停用及依團隊決策的角色異動，需與版本增加同一交易；重啟不得復原舊憑證 |

讀與寫介面必須使用同一份權威資料。未來可由單一 scoped EF Store 同時實作兩個介面，DI 解析到同一個實例；
不是各自註冊一個記憶體版本。正式 Store 的帳號型別、更新權限及交易實作由雙方共同確認。

## JWT Claim

- 名稱：`sv`；JSON 型別：整數 number；語意：正的 signed Int64。初始版本 1。
- `SessionVersionClaims.Create(persistedVersion)` 產生 `ClaimValueTypes.Integer64`；呼叫端必須傳入已持久化版本。
- 驗證接受 handler 表示為 Integer32／Integer64／Integer 的正整數，因小型 JSON 整數可能被框架標成 Integer32。
- 拒絕缺失、重複、字串型 JSON、浮點、正負號、前導零、空白、非 ASCII 數字、非正整數及溢位。
- `userId` 必須恰一個非空、標準 D 格式 Guid。若有 `sub`，須恰一個且與 userId 相同。
- Principal 須恰一個已驗證 Identity。`IsAuthenticated` 不是密碼學證明；呼叫者只能使用框架完成
  簽章、演算法、Issuer、Audience、Lifetime 驗證後的 Principal，不能從 Request Body 或自行解碼的 JWT 建立。
- 不提供任何 legacy-token 預設版本或依角色／Cookie／Bearer 跳過的路徑。

## 共用驗證及 HTTP 邊界

Bearer Header 優先、固定 Staff Cookie fallback 及原本所有 JWT 檢查保持不變。未來只在共用的
`OnTokenValidated` 呼叫 `ISessionPrincipalValidator.ValidateAsync`，內部沿用 `ISessionVersionValidator`。
不要各做一份 Cookie 及 Bearer 版本邏輯，也不要讓失敗的 Bearer 回退使用 Cookie。

| 核心結果 | 未來 HTTP 整合要求 |
|---|---|
| Valid | 才能建立有效身分／標記成功的 Bearer 來源，繼續原有 CSRF／Authorization |
| InvalidCredential | 認證失敗 401；不暴露帳號是否存在、狀態、目前／輸入版本 |
| SecurityStateUnavailable | 終止請求並安全回 503，不能繼續執行 Endpoint 或使用舊快取 |
| 呼叫者取消 | 傳遞取消；不是成功，不承諾固定 HTTP 狀態 |

**只有 `context.Fail` 並不足以實作 503。** 後續需明確的 exception／請求狀態與外層 guard，
確保狀態來源故障時包含 AllowAnonymous 路徑也不能默默繼續；本階段沒有此 HTTP guard。
若採 Middleware，需包在 Authentication 外層，保持 Authentication → Cookie CSRF → Authorization 的相對順序。
不可在版本失敗時先標記 Bearer 已驗證，造成 Cookie CSRF 跳過。

## 所有登入登出流程

1. 未來 Endpoint 先通過正常 JWT＋版本驗證；Cookie 寫入仍先通過 CSRF／HTTPS Origin。
2. 把可信 Principal 傳給 `RevokeAllAsync`；不接受 Body 內的操作者 userId、版本或角色宣稱。
3. Store 以該 Token 的版本作 expectedVersion，執行上述原子條件更新（不是先讀再無條件加一）。
4. 成功 commit：回 Revoked。CAS 未命中：重新讀 committed 快照以分辨下列結果，**不以新版本自動重試寫入**。
5. 已經大於 expectedVersion 的 ACTIVE 帳號：AlreadyRevoked；不存在／停用／Token 版本超前：InvalidCredential；
   資料毀損、讀取失敗或 CAS 宣稱失敗但 ACTIVE 版本仍相同：SecurityStateUnavailable。
6. Revoked／AlreadyRevoked 才能聲稱撤銷完成並清 Cookie、回 204。InvalidCredential 對應 401；Unavailable 對應 503，
   即使選擇刪除本機 Cookie，也不得假裝伺服器撤銷成功。

已進入處理的同版本並行登出最多只增加一次；後續舊版本重試不會反覆踢掉新登入。
在正式驗證已拒絕舊 Token 的情況下，後來才抵達的重試會先回 401；AlreadyRevoked 主要處理先前已驗證的並行請求。
新版本重新登入後再次登出，可以再增加一次。這是**所有登入**撤銷，會影響同帳號其他裝置及 Bearer；不是單一裝置登出。

成功更新的 commit 是撤銷線性化點。已在該點之前通過驗證的在途業務請求仍可能完成；若要求嚴格寫入隔離，
業務交易提交前也需重新檢查或適當鎖定。核心不宣稱能中斷所有在途請求。
逾時／取消可能發生在 commit 後；不能假設 rollback、盲目重試或回報成功。Int64 到頂時拒絕再更新，需維運處理而非重設。

## 登入核發的競態

未來加入 sv 時，密碼 Hash／Status／RoleId／SessionVersion 必須取自一致的權威帳號快照。
例如：以版本 1 的 Hash 驗證完密碼後若發生密碼重設，不可再單獨讀到版本 2 並用舊密碼的驗證結果核發版本 2。
應核發當初驗證快照的版本（之後版本檢查會拒絕），或在交易／並發檢查下確認 Hash、狀態和版本均未改變。
僅在密碼驗證完成後額外呼叫 Store 讀「最新版本」並不自動安全。

## 正式啟用順序（尚未執行）

1. 16 號確認共用欄位／signed 型別／初始化、原子更新及安全異動交易契約，提供 Migration。
2. 07 實作並測試 EF 權威 Store；至少兩個獨立 DbContext／程序競爭，確認 CAS、commit 後讀取及故障語意。
3. 協調所有共用 JwtBearer scheme 的 JWT 核發入口（包含非醫療端）。新核發 Token 一律帶真實 sv；
   嚴格驗證啟用時舊 Token 全部重新登入，不依不可信角色 Claim 或來源建立免檢洞。
4. 同批啟用 claim 核發、共用驗證、Unavailable→503 guard 和登出更新；缺 Store／設定／Schema 應啟動失敗或安全拒絕，
   不得只改登出或使用版本 1／記憶體 Store 假裝上線。
5. 保留現有 Cookie 屬性、Origin、CSRF、Rate Limiting、角色與 DenyAll 個案 Scope，不改共用 RBAC。
6. 本機 HTTPS＋MySQL 重驗舊 Cookie／Bearer 登出後皆 401、重新登入新版本 200、停用／重啟／改密碼及跨程序測試。

SessionVersion 不取代 RBAC；角色與功能權限仍需使用 16 號的最新權威資料，Permission 不等於個案 Scope。

## 離線驗證指令

一般停止 API 後可使用 `dotnet run --project tests/SessionRevocationHarness.csproj`。
若既有 API 正在執行而鎖住預設輸出，可不停止 API、改用獨立且被 Git 忽略的建置目錄：

```powershell
dotnet restore tests/SessionRevocationHarness.csproj -p:NuGetAudit=false --ignore-failed-sources
dotnet build tests/SessionRevocationHarness.csproj --no-restore -p:OutputPath=bin/SessionRevocation/ -p:UseAppHost=false
dotnet tests/bin/SessionRevocation/SessionRevocationHarness.dll
```

不需新的 NuGet 套件。`NuGetAudit=false` 僅針對本次離線還原命令，不修改專案的正式套件或稽核設定。
Harness 透過 in-process 真正 JwtBearerHandler 測試兩種傳輸；沒有 HTTP Listener、MySQL、正式 JWT/登出啟用或資料庫並發驗收。
