# 共用 Permission 授權契約（16 號第二批）

## 狀態與責任

沿用 User.Id、Role.Id、Permission.Id 的 Guid，以及單一 User.RoleId；沒有 UserRoles。
第一批 IPermissionService / PermissionService / AddPermissionServices 保留。
本批新增 PermissionRequirement、PermissionAuthorizationHandler 與註冊擴充。
尚未修改 Program.cs、登入服務、Controller、Migration 或 Snapshot，因此現有 API 不會自動取得這層保護。
下列設定須經使用者確認後，在後續整合階段加入；本文件不是已啟用的授權配置。

## 07、11 的接入方式

經確認後，由共用啟動設定註冊（需要已配置的 scoped ApplicationDbContext 與身分驗證）：

```csharp
using EarlyInterventionCare.Api.Services.Authorization;

builder.Services.AddPermissionAuthorization();
builder.Services.AddAuthorization(options =>
    options.AddPermissionPolicy("Permission:case.read", "case.read"));
```

經權限矩陣核准的 Controller / Action 可使用：

```csharp
[Authorize(Policy = "Permission:case.read")]
```

也可由已註冊的 IAuthorizationService 呼叫
`AuthorizeAsync(User, resource: null, policyName: "Permission:case.read")`。
這裡的 case.read 僅為接入範例，不代表團隊已核准角色配置；註冊 Policy 不會新增 Permission 或角色授權資料。
使用明確 Policy 名稱，拼字必須一致。本批未提供動態 PolicyProvider、通配權限或全域 FallbackPolicy。
沒有標記 Permission Policy 的 API 不會自動套用此檢查；整合者必須逐項盤點端點，不可把普通 [Authorize] 當成 Permission 檢查。
不可在受保護 Action 加上 AllowAnonymous，也不可加入以角色或 claim 略過此 Handler 的替代授權路徑。

07 負責提供已驗證的一般帳號身分；11 的一般帳號 API 可使用相同 Policy。
教師 Token 並非一般帳號 RBAC 身分，仍走既有教師指定個案／任務授權，不能藉由加入 userId claim 直接轉成一般帳號。

## 身分與拒絕規則

- Handler 只接受一個已驗證 ClaimsIdentity，其中必須有且只有一個有效、非空 Guid 的 userId claim。
- 不從未驗證的 identity 取得 userId，不合併多個已驗證 identity；缺漏、重複、格式錯誤均明確 Fail。
- 每次評估呼叫 IPermissionService.HasPermissionAsync，從資料庫當前 User.RoleId / RolePermission 取得授權，不依 JWT role 或 permission claim 放行。
- PermissionService 僅允許狀態精確為 ACTIVE 的既有帳號；不存在、停用、缺角色／權限或映射均不通過。ADMIN 沒有特例。
- 權限字串採 Ordinal 大小寫精確比對；空白或前後有空白的 Requirement 在註冊時即拒絕。
- 沒有命中授權即 context.Fail；同一授權評估內其他 Handler 的 Succeed 不能抵銷明確失敗。
- 不快取權限。同一 JWT 在下一次評估會重新查詢，因此撤權／換角色／停用後，後續查詢可見變更時即拒絕。
- 查詢錯誤向上傳遞，絕不轉成成功；API 邊界應使用既有安全錯誤處理回應服務不可用，不能回傳例外或憑證。錯誤不等同一般 403。
- 第四批：帳號、角色與權限以單一 EF 查詢讀取，保留 .NET Ordinal ACTIVE 與 Permission 比對；不代表授權與業務操作同一交易。
- 第四批：DbException、TimeoutException 及 Provider 以 InvalidOperationException 包裝的這兩類故障轉成固定訊息 PermissionLookupException，沒有原始 InnerException。Handler 對任何查詢例外先 Fail 再拋出；取消不視為正常拒絕。
- AddPermissionPolicy 拒絕已有 Policy 名稱（依 AuthorizationOptions 的名稱比較規則）及前後空白，避免本擴充覆寫既有定義。其他程式若在之後直接呼叫框架 AddPolicy，仍能覆寫，因此必須集中管理註冊。
- 未登入通常由 ASP.NET Core 驗證方案 Challenge 為 401；已登入而無權限通常 Forbid 為 403，實際 HTTP 行為仍需後續整合測試。

權限核對與業務寫入不是同一交易；本批不保證已在執行中的請求可被即時撤回。
Handler 也不負責個案綁定、院所、資料所有權、教師任務、家長同意或欄位隱私。
07、11 在操作前仍須完成對應資源範圍檢查，避免只因具備 case.read 就讀取所有個案。

## 離線驗收

```powershell
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore
dotnet run --project tests/RbacOfflineHarness.csproj
```

測試透過真正的 IAuthorizationService / Policy / Handler 與 DI scope 執行，以現有 User、Role、Permission、RolePermission 模型建立可變動的 IPermissionService 測試替身。
涵蓋允許、缺權、停用、不存在、撤權重查、使用者隔離、ADMIN、偽造 claim、身分歧義、停用與換角色、查詢故障及預設拒絕。
測試不啟動 API、不讀 secrets、不連線資料庫、不執行 Migration；不宣稱已驗證正式 PermissionService 的 EF SQL／MySQL 執行或 HTTP 401／403。
第一批實際資料查詢已靜態核對與現有單一 RoleId 模型相容，資料庫查詢整合驗證留待另行授權。

## 第四批驗證與全域例外處理提案（未接入）

PermissionServiceOfflineHarness 執行正式服務與 Pomelo EF 轉譯，但以 ADO 命令結果替身取代資料庫。
沒有驗證真正資料庫執行 EF SQL，不能宣稱正式資料庫服務測試已完成。
本機套件快取沒有 Microsoft.EntityFrameworkCore.InMemory 或 Microsoft.EntityFrameworkCore.Sqlite；本批沒有安裝新套件。
若採隔離 SQLite，需先核准相容 EF Core 8.0.13 的 Microsoft.EntityFrameworkCore.Sqlite 與原生相依套件，並處理 MySQL 專屬型別/模型映射；SQLite 仍無法驗證 MySQL 排序規則及交易語意。
既有 CoreDatabaseHarness 依賴本機設定與受管理資料庫，不在本批執行。

最小全域錯誤整合方案，待使用者另行核准 Program.cs 與 Middleware 變更：

1. 註冊 ASP.NET Core 8 IExceptionHandler，僅對 PermissionLookupException 回應 503 與固定 JSON：`{"code":"AUTHORIZATION_UNAVAILABLE","message":"授權服務暫時無法使用。"}`，加上 Cache-Control: no-store。
2. UseExceptionHandler 位於 UseAuthentication / UseAuthorization 之前，以捕捉授權階段例外；Controller ExceptionFilter 無法取代這個邊界。
3. 不把正常 false/Fail 或 Challenge/Forbid 轉成例外，保留未登入 401 與缺權 403；未註冊 Policy 屬設定錯誤，不歸類為資料庫故障。
4. 請求取消不轉 503；Response.HasStarted 時不重寫回應。其他登入/OTP/教師流程例外交回其既有處理，不能一律包成授權故障。
5. 回應只寫固定 DTO，絕不序列化例外、SQL、連線資訊或堆疊。記錄固定事件碼/traceId，不傳入原始 exception；另檢查 EF/Provider 日誌設定，核心包裝不會消除 Provider 已記錄的 SQL 日誌。

整合測試建議：真實 Authorization Middleware 下分別測試未登入 401、缺權 403、具權成功、PermissionLookupException 503 及固定內容；以合成敏感字串確認回應與日誌不洩漏。
另驗證登入/OTP匿名入口、教師Token、Cookie/Bearer及取消請求行為不變；此 HTTP 套件/Host 測試本批未執行。
