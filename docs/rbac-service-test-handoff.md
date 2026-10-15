# RBAC 服務測試與醫療端交接補充

本補充保留既有 RBAC 類別與未提交測試；第四批在既有檔案上增量補強，另新增 PermissionLookupException。
目前尚未接入 Program.cs；本次沒有新增 Seed、Migration、Controller 或業務流程變更。

## 新增測試

```powershell
dotnet run --project tests/PermissionServiceOfflineHarness.csproj
dotnet run --project tests/RbacOfflineHarness.csproj
```

PermissionServiceOfflineHarness 直接使用正式 PermissionService、ApplicationDbContext、Pomelo MySQL 查詢轉譯與資料實體化，沒有替換 IPermissionService。
測試以 DbConnectionInterceptor 攔截開啟連線，以 DbCommandInterceptor 提供固定 ADO 結果或拋出 DbException。
因此不開啟網路、不讀取設定或 secrets、不啟動 API、不執行 Migration；測試資料僅在記憶體。
命令攔截器對查詢形狀、userId/roleId 參數及唯讀路徑作檢查，但不是真正的 SQL 引擎；不驗證 MySQL 執行、外鍵、排序規則、交易隔離或並行撤權。

第四批更新為 34 項，涵蓋：ACTIVE、停用、不存在、狀態大小寫、缺少配置、ADMIN 無隱含全權、Permission Code 精確比對、去重排序、撤權與換角色重查、跨使用者隔離、取消及單一查詢的資料庫/逾時故障。
同時經正式 IAuthorizationService / Handler / Policy 驗證未登入、已登入無權限、有權限，以及 resource 內自行指定 userId 不能覆蓋 ClaimsPrincipal。
正式 DI 使用 ValidateScopes / ValidateOnBuild 驗證，確認服務與 DbContext 為 scoped、不同 scope 不共用、重複擴充註冊只有一個 Permission Handler，根容器無法取得 scoped 服務。
原有 RbacOfflineHarness 的 22 項測試保留，提供更多身分歧義與明確 Fail 行為驗證。

## 建議啟動註冊（尚未實施）

```csharp
using EarlyInterventionCare.Api.Services.Authorization;

// 沿用既有 Authentication 與 AddDbContext<ApplicationDbContext>。
builder.Services.AddPermissionAuthorization();
builder.Services.AddAuthorization(options =>
    options.AddPermissionPolicy("Permission:case.read", "case.read"));
```

上述 case.read 僅為候選範例，不是核准的權限配置。
AddPermissionAuthorization 已包含 AddPermissionServices，不需再手動註冊另一份服務或 Handler。
沿用 UseAuthentication → UseAuthorization → MapControllers；Program.cs 變更另待使用者核准。
正式註冊前需確認沒有其他 IPermissionService 替身覆蓋或被 TryAdd 保留。

## 給 07 的共用契約

- 使用 `[Authorize(Policy = "Permission:case.read")]`，或注入 IAuthorizationService 使用同名 Policy；不重寫 PermissionService，不直接以 JWT permission/role claim 放行。
- Handler 的 userId 只取自唯一已驗證 ClaimsIdentity 的唯一非空 Guid claim；不取 route、query、body 或 resource 的操作人 ID。
- IPermissionService 是內部查詢介面，仍接受 Guid；它本身不驗證身分。Controller 不得將前端任意 userId 當成操作人傳入。
- 每次授權重讀當前 User.RoleId 與權限配置，ACTIVE 以 Ordinal 精確比較；缺少帳號或配置即拒絕，ADMIN 無特例。
- 共用 Permission 不包含院所、個案關係、任務版本、欄位隱私或狀態轉移；07 必須保留資源範圍與業務檢查。
- 資料庫錯誤向上傳遞，不可轉成成功；需要在 Authorization Middleware 之外的安全例外邊界回應服務不可用，Controller ExceptionFilter 不足以處理這一階段。
- 缺少 Permission/RolePermission Seed 不代表實際資料庫必然無資料；若無配置，檢查會拒絕，不能以預設全權補救。

## 整合前待確認

角色矩陣與 Permission 命名、ADMIN 行為、院所/個案範圍、院所停用效果、權限配置交付方式、Cookie/Bearer 身分契約，以及安全 401/403/503 行為。
第四批已將帳號與權限合併為單一查詢；敏感寫入是否需交易內重查，仍須依即時撤權需求決定。
第四批 ADO 替身測試沒有驗證真正資料庫執行 EF SQL；完整的資料庫服務測試仍未完成。
仍需另行核准 Program/Controller 接入、HTTP 驗收與隔離資料庫整合測試；本批測試不代表上述項目已完成。
