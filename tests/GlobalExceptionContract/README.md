# V5.3 第六批：未註冊的全域例外候選與離線契約

基準：`codex/v53-parent-core`／`3d5b01cb221bda9b54e9c04a4b8ca6332233a164`。

正式來源只新增[GlobalExceptionMiddleware](../../EarlyInterventionCare.Api/Middleware/GlobalExceptionMiddleware.cs)，命名空間`EarlyInterventionCare.Api.Middleware`。沒有修改Program、DI、Controller、Filter、既有Permission Middleware或其他業務程式；沒有註冊正式Pipeline。

## 執行

```powershell
dotnet restore tests/GlobalExceptionContract/GlobalExceptionContractHarness.csproj --source tests/GlobalExceptionContract -p:NuGetAudit=false
dotnet build tests/GlobalExceptionContract/GlobalExceptionContractHarness.csproj --no-restore -p:NuGetAudit=false
dotnet tests/GlobalExceptionContract/bin/Debug/net8.0/GlobalExceptionContractHarness.dll
$LASTEXITCODE
```

使用現有本機套件快取，不新增套件或網路來源；缺少依賴時停止。實跑**26 PASS、0 FAIL、4 BLOCKED，退出碼2**；建置0警告、0錯誤。FAIL退出碼1；有BLOCKED不改成0。

## 候選行為與責任

- 正常RequestDelegate原樣執行一次，既有狀態／header／body不修改。
- 一般未知例外且回應未開始、RequestAborted未取消時，Clear未開始的回應，回HTTP500、`application/json; charset=utf-8`及`Cache-Control: no-store`。
- 固定JSON為`{ code, message, traceId }`。本批候選code為`INTERNAL_ERROR`，訊息為「系統發生錯誤，請稍後再試。」；500方向已確認，但字面值及訊息仍應在正式註冊前共同審查。
- traceId沿用伺服器HttpContext.TraceIdentifier，不從HTTP header、body、Exception或Idempotency-Key取得。若空白，產生N格式Guid並回寫同一HttpContext，讓回應與該請求一致；不產生資格Stamp。
- 不序列化例外、內層訊息、stack trace或內部路徑。沒有新增例外內容日誌或讀取任何私人設定；正式追蹤／安全日誌關聯政策待審查。
- 不自動把TimeoutException等類型判為503。既有Filter與特定Middleware應在下游先處理已知分類；若只有未分類TimeoutException冒泡到本邊界，回500。
- 已處理的401／403／404／409／503回應維持原樣，不重新封裝。真實PermissionLookupExceptionMiddleware的503與QuestionnaireErrors Filter的409／503分支均有離線組合測試。
- RequestAborted已取消時，所有例外保留傳遞；OperationCanceledException／TaskCanceledException不轉500。沒有請求取消訊號的OperationCanceledException仍視為一般未知錯誤；這不等於替其他業務逾時定義503。
- Response.HasStarted時不Clear、不改狀態、不補JSON，原例外向上傳遞。回應寫入失敗或寫入中取消也直接傳遞，不進行第二次錯誤回應。

## 26項PASS實際涵蓋

正常流程；同步／非同步未知例外；精確安全JSON／Content-Type；秘密標記、內層錯誤及合成路徑不外洩；清除尚未開始的殘留body／header；兩組缺少traceId回補；不信任client trace header；五種已處理狀態不變；既有Permission Middleware；兩組真實Filter分支；未分類timeout；已開始回應；兩種請求取消例外；取消但正常返回；取消後一般例外；非請求取消的OperationCanceledException；寫入失敗；寫入中取消；只有RequestDelegate相依且正式Program沒有註冊引用。

所有RequestDelegate、DefaultHttpContext、IHttpResponseFeature與Stream均為記憶體測試。HasStarted由明示替身提供，沒有開HTTP listener、Kestrel、Socket、API、Docker或DbContext；沒有資料庫連線字串、SQL、Migration、真實帳號或病患資料。

Filter測試使用真實QuestionnaireErrorsAttribute產生ObjectResult，再由合成delegate寫出結果；不宣稱實際MVC Filter順序／formatter已驗收。特定Permission Middleware直接使用正式類別，沒有更改其分類或授權決策。

## 四項BLOCKED與正式註冊前條件

1. **Pipeline接線與HTTP驗收**：候選未註冊，尚需核定外層位置，使已知Filter／特定Middleware在下游先處理錯誤；驗證Authentication／Authorization／MVC及Development／IsolatedTest／Production行為，不能讓開發詳細錯誤頁成為外部回應。
2. **共用字面契約與追蹤政策**：共同確認INTERNAL_ERROR、固定訊息、traceId來源／fallback及安全日誌關聯。未知500與明確不可用503方向不變，不把traceId作為資格或重送key。
3. **舊回應格式差異**：既有Permission Middleware及部分業務Filter仍可能沒有traceId，本批刻意不補改，不能宣稱所有錯誤已統一格式。需各負責人另批協調；不應由全域Middleware改寫正常返回的403或503。
4. **真實取消／部分回應**：需另行授權驗證Kestrel回應已開始、取消／斷線、client取消與寫入失敗。離線替身PASS不等於真實HTTP已驗收。

本成果適合以「未註冊候選＋離線測試」獨立保存；不能據此啟用家長Gateway、放寬Deny／Case Scope、移除既有Schema Gate或連接未升級資料庫。
