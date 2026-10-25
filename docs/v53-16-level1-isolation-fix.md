# V5.3／16：Level 1 隔離修正紀錄（2026-10-22）

## 2026-10-25 Windows Terminal 手動驗收：16-11 通過，退出碼未取得

**16-11：隔離 API 啟動、健康檢查、合作式正常停止及清理驗收通過；退出碼未取得。**

證據來源：使用者於 Windows Terminal／PowerShell 親自操作後提供的真實結果。此節為最新結論；以下各節保留當時失敗、調查及操作規劃作為歷史，不覆寫本節。本次 agent 只更新文件及審查 Git 差異，沒有重啟 API或重新執行 Build／測試。

| 項目 | 使用者提供的實際證據 |
|---|---|
| Branch／基準 Commit | codex/v53-parent-core／ca3b540bdbaa09d42ab1fd6be67a7b5171acae47，加原 9 個未提交檔案 |
| API PID | 55228 |
| 啟動 UTC | 2026-10-25T09:01:00.9413685Z |
| 環境／監聽 | IsolatedTest／127.0.0.1:5186 |
| Build | 成功，0 errors／1 NU1900；弱點索引警告不代表弱點掃描已完成 |
| Health | HTTP 200；{"status":"alive","databaseChecked":false} |
| Data Protection | 日誌顯示 EphemeralDataProtectionProvider |
| Ctrl+C | 使用者親自按鍵，ISOLATED_TEST_CTRL_C_RECEIVED 有出現 |
| Host 停止事件 | ISOLATED_TEST_APPLICATION_STOPPING、ISOLATED_TEST_APPLICATION_STOPPED 均有出現 |
| 終端返回 | PowerShell 正常返回提示符號 |
| 程序／監聽釋放 | Get-Process PID 55228 無輸出；Get-NetTCPConnection Listen 5186 無輸出 |
| 清理 | ENV_RESTORED=True；PRIVATE_SETTINGS_REMOVED=True |
| API_EXIT_CODE | 空白，未取得明確退出碼；**不得記為 0** |
| MySQL80 | Running；使用者回報未做 Migration 或 MySQL 資料寫入 |
| 業務 API | 未呼叫登入、問卷、Audit 或其他業務 API |

合作式正常停止的結論依據是按鍵事件、ApplicationStopping／Stopped、返回提示符號及 PID／監聽消失的組合，不以空白退出碼推定成功代碼。先前工具終端未能觀察到訊號，與此次真正互動終端成功有差異；足以確認此手動路徑可正常停止，但不證明所有工具終端的訊號機制或根因均已釐清。

### 驗收範圍與待辦

- 本階段是 loopback API 啟動、health、合作式停止及清理，不是資料庫 readiness；databaseChecked=false 明確表示 health 沒有檢查 DB。
- **真正 MySQL、資料持久化、FK／唯一鍵／交易／併發、備份還原均未驗收。**Level 2 未開始，正式 Adapter、登入／問卷端到端及正式部署也不因本結果完成。
- 本次沒有提供明確程序退出碼，不補填；沒有連續網路追蹤或資料庫稽核，不宣稱已證明完全零資料庫連線。
- IsolatedTest 的記憶體 token 重啟失效；精確預熱服務篩選依賴框架 internal type，Runtime 升級須重跑註冊測試。
- IsolatedTest 的 PID／Ctrl+C／停止訊息保留作為必要驗收觀測，只輸出程序 metadata、不改 Cancel、不啟用 shutdown API；一般環境不註冊，無需為清理而改動已驗收程式。
- 目前只準備本機 Commit 提案，未暫存／Commit／Push／Merge；下一步 MySQL實例、帳號、Migration、合成資料、寫入及復原操作均須精確計畫與另行核准。

## 2026-10-25 停止專項調查（本批未開 HTTP／API）

16-11 維持「啟動與健康檢查通過，正常停止未驗收」。本節不是第四次 API 啟動。原 9 個未提交檔案均保留；本批只補測與文件，沒有更動 Program.cs、LocalIsolationRuntime 或啟動腳本。

### 已確認與未確認

- 啟動腳本／前次命令為工具終端內 PowerShell 前景執行 `dotnet <本 Worktree DLL>`，沒有 dotnet-run 中間程序；預期樹為工具終端 → powershell.exe → dotnet.exe。前次未保存 ParentProcessId，因此不能宣稱還原出完整歷史程序樹；本批沒有重新啟動 API 取得它。
- 真實 Program 的 DI 擷取測試確認 **恰好一個 IHostLifetime，為 ConsoleLifetime**，IsolatedTest ShutdownTimeout=5 秒；沒有 UseWindowsService／其他自訂 Lifetime 註冊或衝突。
- Program 的 Console.CancelKeyPress 只寫訊息，不改 Cancel；ApplicationStopping／ApplicationStopped 也只寫訊息，不等待、不存取 DB。
- PID 58404 前次健康成功、Ctrl+C 後仍存在並監聽；沒有 CTRL_C_RECEIVED／STOPPING／STOPPED 訊息，最後核對路徑與啟動時間後強制停止、exit=-1。
- 工具的 write_stdin 操作傳入 U+0003，不等於已證實 Windows CTRL_C_EVENT 被原生 Console 收到。不能宣稱所有 Codex 終端均不支援 Ctrl+C，但此路徑目前沒有可靠停止證據。
- 最可能的環節是終端／PTY → PowerShell／原生 Console 訊號傳遞，仍屬假設。沒有證據支持「已收到 Ctrl+C，但 ConsoleLifetime 不工作」或「已開始停止但卡死」。不因此修改 API lifetime 或延長 timeout。
- PowerShell finally 的執行是終止 API 後清理證據，不能當成 Ctrl+C 已送到 API 的證據。程序樹／共享 console／process group 的實際細節仍未直接觀測。

### 非 HTTP 測試

在現有 LocalIsolationSafety/Program.cs 增加 `--stop-lifecycle` 模式，使用 Generic Host 與單一合成 IHostedService；不建 Kestrel、DbContext、Controller 或業務服務，不發送 OS 訊號、不終止程序。從實際 IsolatedTest helper 取 HostOptions 配置。

13 項通過：StopApplication 觸發 ApplicationStopping；單獨呼叫不會自行執行 service.StopAsync；WaitForShutdownAsync 會把停止請求接到 StopAsync；直接 StopAsync 也可完成；ApplicationStopped 觸發；合作式服務可收到 timeout 取消權杖；不配合取消的合成服務不會被 timeout 強制結束，測後明確 release 並等待清理。測試用 100ms timeout 僅存在測試容器，不改應用程式的 5 秒。

真實 Program 無 HTTP 註冊擷取增加「IHostLifetime 數量=1」斷言，本次 15 項通過（前次 14 項加 1）。仍在 HostBuilt 中止，未啟動 Web HostedService。不是完整 API 正常停止驗收。

```powershell
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile -- --stop-lifecycle
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile -- --full-registration
```

測試 Build 出現既有 NU1900 警告，沒有修改 Runtime、Execution Policy、系統權限、既有 key ring或 MySQL。真正 Ctrl+C 及完整 API stop 本批均未測。

### 正常停止方案比較

| 方法 | 可證明範圍 | 變更與風險 | 建議 |
|---|---|---|---|
| A 真正 Windows Terminal／互動 PowerShell，鍵盤 Ctrl+C | 同一份獨立 API DLL、原生 ConsoleLifetime、實際停止及清理 | 不需改 API／腳本；需要操作人員在該視窗按鍵，不以工具 stdin 代替 | 優先 |
| B 同程序受控 runner 捕捉真實 Host，呼叫 StopApplication | 原 Program Run 等待迴圈、Host合作式停止；不驗證 OS Ctrl+C | 未實作的專用測試模式，需核准後才增加／執行；不加 HTTP shutdown API，不用 IPC | A 受終端限制時的獨立驗證 |
| C Windows Ctrl+Break | 另一種 Console 訊號與 Host 停止 | 目前訊息只統稱 Ctrl+C，採用前須區分 SpecialKey；不能冒充 Ctrl+C 通過 | 次選、另行核准 |
| Windows Service／taskkill／Stop-Process／Console 廣播 | 前者不符合目前 console app；其餘不能證明正常關閉 | 安裝服務／權限或誤傷共享 console 風險，不適用本次 | 不採用 |

StopApplication 是**同一個程序內的服務呼叫**，不能在另一個 PowerShell 建一個 Lifetime 物件便聲稱停止 API。B 的受控 runner 必須讓真正 Program 跑在同一程序，於 HostBuilt 擷取引用但不終止 EntryPoint，僅 health 後對該引用請求停止並等待 Run 返回；HTTP 仍須先核准。相較 A，它有 runner／entry assembly 差異，不能代替獨立 DLL 的鍵盤驗收。本批沒有實作／執行 B。

### 下一次精確操作方案 A（僅計畫，須另行核准）

由操作人員在真正 Windows Terminal 的 PowerShell 視窗 T1，工作目錄為本 Worktree，不執行 ps1 或 Bypass。先確認 HEAD、未提交檔案及 5186 空閒，Build：

```powershell
Set-Location 'C:\Users\Rache\source\repos\ashley191919\Smart-Early-Intervention-Care-System\.codex-worktrees\v53-parent-core'
git -c safe.directory='*' rev-parse HEAD
git -c safe.directory='*' status --short
netstat -ano -p tcp | Select-String ':5186\s'
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore
```

Build 成功且埠空閒後，直接貼上以下互動命令（不要存成受 Restricted 阻擋的 ps1）：

```powershell
$stopTrialFile=Join-Path ([IO.Path]::GetTempPath()) ('appsettings.v53-stop-' + [Guid]::NewGuid().ToString('N') + '.Local.json')
$stopTrialRng=[Security.Cryptography.RandomNumberGenerator]::Create()
$stopTrialBytes=New-Object byte[] 48
$stopTrialRng.GetBytes($stopTrialBytes)
$stopTrialKey=[Convert]::ToBase64String($stopTrialBytes)
$stopTrialRng.GetBytes($stopTrialBytes)
$stopTrialPassword=[Convert]::ToBase64String($stopTrialBytes)
$stopTrialRng.Dispose()
$stopTrialConfig=@{
    ConnectionStrings=@{DefaultConnection="Server=127.0.0.1;Port=33326;Database=earlycare_v53_synthetic;User=earlycare_v53_app;Password=$stopTrialPassword;DateTimeKind=Utc;AllowLoadLocalInfile=false"}
    Jwt=@{SigningKey=$stopTrialKey;Issuer='earlycare-v53-loopback';Audience='earlycare-v53-synthetic';ExpirationMinutes=30}
    StaffSession=@{TrustedOrigins=@()}
    Logging=@{LogLevel=@{Default='Information';'Microsoft.AspNetCore'='Warning';'Microsoft.AspNetCore.DataProtection'='Information'}}
}
[IO.File]::WriteAllText($stopTrialFile,($stopTrialConfig|ConvertTo-Json -Depth 6))
$stopTrialOldAsp=$env:ASPNETCORE_ENVIRONMENT
$stopTrialOldDot=$env:DOTNET_ENVIRONMENT
$stopTrialOldSettings=$env:EARLYCARE_LOCAL_SETTINGS
Push-Location './EarlyInterventionCare.Api'
try {
    $env:ASPNETCORE_ENVIRONMENT='IsolatedTest'
    $env:DOTNET_ENVIRONMENT='IsolatedTest'
    $env:EARLYCARE_LOCAL_SETTINGS=$stopTrialFile
    dotnet ./bin/Debug/net8.0/EarlyInterventionCare.Api.dll
    Write-Output ('API_EXIT_CODE=' + $LASTEXITCODE)
} finally {
    $env:ASPNETCORE_ENVIRONMENT=$stopTrialOldAsp
    $env:DOTNET_ENVIRONMENT=$stopTrialOldDot
    $env:EARLYCARE_LOCAL_SETTINGS=$stopTrialOldSettings
    Pop-Location
    Remove-Item -LiteralPath $stopTrialFile
    Write-Output ('PRIVATE_SETTINGS_REMOVED=' + (-not(Test-Path -LiteralPath $stopTrialFile)))
    Write-Output ('ENV_RESTORED=' + (($env:ASPNETCORE_ENVIRONMENT -eq $stopTrialOldAsp) -and ($env:DOTNET_ENVIRONMENT -eq $stopTrialOldDot) -and ($env:EARLYCARE_LOCAL_SETTINGS -eq $stopTrialOldSettings)))
}
```

視窗 T2 只執行 netstat 與 GET health，不嘗試登入；只在監聽確為 127.0.0.1 時發請求：

```powershell
netstat -ano -p tcp | Select-String ':5186\s' | ForEach-Object { $_.Line }
Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5186/health/local' -TimeoutSec 5
```

記下 T1 本次 PID、StartTime、T2 的 Get-Process -Id <本次PID> 路徑，並對該 PID 取樣 TCP；不得沿用歷史 PID 58404。只在 health 200／alive／databaseChecked=false且無安全異常後，將焦點移回 T1，**實際按鍵 Ctrl+C 一次**。成功需觀察 CTRL_C_RECEIVED、APPLICATION_STOPPING、APPLICATION_STOPPED、正常退出（預期 code 0）、T2 確認該 PID 消失／5186 無監聽及 finally 清理 True；不強制三種訊息跨執行緒的列印先後。
等待至 10 秒仍未完成就記失敗，不反覆發訊號、不改 timeout。分別記「無 CancelKeyPress」、「有 CancelKeyPress 無 Stopping」、「有 Stopping 無 Stopped／退出」。必要清理前核對**本次** PID、完整路徑、StartTime及對應 DLL 命令；不能核對就先回報，不以名稱停止 dotnet／MySQL。強制終止永不列正常停止通過。
測後確認私人檔案刪除、環境還原、MySQL80 原狀、key metadata 原狀及 Git 除預期紀錄無差異。網路取樣不是完整零連線證明。此流程與任何 B／C 方案的實際執行都須另行核准。

## 2026-10-25 第三次實際 Level 1：啟動／health 通過，停止驗收未通過

本節為最新結果，優先於下方歷史紀錄。HEAD 仍為 ca3b540bdbaa09d42ab1fd6be67a7b5171acae47、branch codex/v53-parent-core，使用原 9 個未提交檔案；未 Commit／Push／Merge。

### 差異審查與啟動前證據

- helper 只在精確 IsolatedTest 生效，只移除 service type=IHostedService 且 implementation full name=Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService 的描述，未移除全部 HostedService。
- Configure 位於所有服務註冊之後、Build 之前。Development／Production 的 Logging／DP／HostedService 回歸保持不變。
- Cookie／CSRF／JWT／RBAC／Scope、五份 Migration／Snapshot及教師核心無本批差異。未加入真實密碼／JWT Key／醫療資料；probe 的 synthetic-unregistered-probe 是未註冊的虛構值。
- 本次在 Program 的 IsolatedTest 區塊加入 Console.CancelKeyPress、ApplicationStopping、ApplicationStopped 訊息；只觀測，不更動 Cancel、不主動要求停止，其他環境不註冊。
- 5186／33326 啟動前無監聽。MySQL80 Running，3306／33060 原 PID 40620，未連線／修改。
- API Build 成功，0 errors／1 NU1900（無法取得 NuGet 弱點索引），另有預覽 SDK 提示。
- 本次重跑 FullRegistrationProbe 修正後 14 PASS、根因重現 11 PASS、隔離／其他環境 20 PASS。三者均不開 HTTP／DB，臨時設定及環境清理成功。

### 實際命令與設定

```powershell
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile -- --full-registration
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile -- --full-registration --reproduce-warmup
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile
```

啟動用互動 PowerShell，未執行 ps1／ExecutionPolicy Bypass。Temp 下新建唯一 appsettings.v53-third-<UUID>.Local.json。RandomNumberGenerator 分別產生兩組 48-byte 隨機值，作未建立帳號的假 DB 密碼與獨立 Base64 JWT Key，未輸出內容。
設定精確為 Server=127.0.0.1、Port=33326、Database=earlycare_v53_synthetic、User=earlycare_v53_app、AllowLoadLocalInfile=false、DateTimeKind=Utc。Jwt Issuer=earlycare-v53-loopback、Audience=earlycare-v53-synthetic、ExpirationMinutes=30；StaffSession TrustedOrigins 空。Console Default=Information、Microsoft.AspNetCore=Warning、Microsoft.AspNetCore.DataProtection=Information，便於觀測且沒有 body／機密記錄。

於 EarlyInterventionCare.Api 目錄執行以下流程（變數值為本次產生的路徑，不是追蹤檔案）：

```powershell
$level1OldAsp=$env:ASPNETCORE_ENVIRONMENT
$level1OldDot=$env:DOTNET_ENVIRONMENT
$level1OldSettings=$env:EARLYCARE_LOCAL_SETTINGS
try {
    $env:ASPNETCORE_ENVIRONMENT='IsolatedTest'
    $env:DOTNET_ENVIRONMENT='IsolatedTest'
    $env:EARLYCARE_LOCAL_SETTINGS=$level1File
    dotnet ./bin/Debug/net8.0/EarlyInterventionCare.Api.dll
    Write-Output ('API_EXIT_CODE=' + $LASTEXITCODE)
} finally {
    $env:ASPNETCORE_ENVIRONMENT=$level1OldAsp
    $env:DOTNET_ENVIRONMENT=$level1OldDot
    $env:EARLYCARE_LOCAL_SETTINGS=$level1OldSettings
    Remove-Item -LiteralPath $level1File
}
```

另一終端只對健康端點發一次 GET：

```powershell
Get-Process -Id 58404
netstat -ano -p tcp | Select-String '\s58404$' | ForEach-Object { $_.Line }
$level1Response=Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5186/health/local' -TimeoutSec 5
```

### 觀測結果

| 項目 | 實際結果 |
|---|---|
| PID／路徑 | 58404／C:\Program Files\dotnet\dotnet.exe |
| 啟動 UTC | 2026-10-25T02:30:27.0883143Z |
| 環境 | Hosting environment: IsolatedTest |
| 監聽 | 框架日誌與 netstat 均確認只有本 PID 的 127.0.0.1:5186 LISTENING |
| Health | HTTP 200，{"status":"alive","databaseChecked":false} |
| DP | Using ephemeral data protection provider；未見 DPAPI 解密、key-ring 寫入、Event Log 或其他初始化錯誤 |
| 業務 API | 未呼叫登入／問卷／Audit；未建 DB、未 Migration／Seed／EnsureCreated |
| Ctrl+C | 透過測試終端送出一次；未觀察到 ISOLATED_TEST_CTRL_C_RECEIVED |
| Host 停止事件 | 未觀察到 ISOLATED_TEST_APPLICATION_STOPPING／STOPPED |
| 正常退出 | 未發生；2026-10-25T10:31:12+08:00 仍存在且監聽 |

送 Ctrl+C 後逾 5 秒仍存活；沒有事件證據能確認訊號送達，不能宣稱是 Host 死鎖。為停止本次測試，Get-Process 核對 PID=58404、Path=C:\Program Files\dotnet\dotnet.exe、StartTime UTC 精確等於 2026-10-25T02:30:27.0883143Z 後，才執行 Stop-Process -Id 58404；沒有停止其他程序。API_EXIT_CODE=-1，不列正常關閉通過。

清理輸出 ENV_RESTORED=True、PRIVATE_SETTINGS_REMOVED=True；結束後 PID 消失、5186／33326 無監聽、Temp 無本次設定檔。MySQL80 仍 Running，原 PID 40620 不變。既有兩個 key 檔 metadata 未變，未見新 key 檔；未讀內容。Execution Policy 仍 Restricted，未動系統權限或網路。

### 安全證據界線及 16-11

netstat 多次取樣未觀察到本 PID 非預期 DB／外部連線；不是連續封包或系統追蹤，不能證實完全沒有瞬間連線。Get-NetTCPConnection 查詢未取得結果，因此以 netstat 與框架 listen 日誌為證據，不宣稱完成所有網路稽核。
沒有測正式 Cookie 登入、跨來源、HTTPS、真 MySQL 或資料持久化。記憶體 provider／啟動成功也不能推論未來任意新增 IKeyManager 使用者均安全。
**16-11：啟動與健康檢查通過，停止驗收未通過；Level 1 尚未全部完成。**尚需處理／驗證終端 Ctrl+C 訊號與正常停止證據，未進 Level 2。本次不擴大停止機制、不繼續改系統或重啟。

## 2026-10-25 第三批根因調查（未做第三次 Level 1）

### 可重現的初始化鏈

新增 `tests/LocalIsolationSafety/FullRegistrationProbe.cs`。透過框架 Hosting DiagnosticListener 執行真實 API EntryPoint，在 `HostBuilding` 注入測試 Repository，於 `HostBuilt` 擷取真實 Host 並以測試例外中止 EntryPoint，早於 app.Run／Kestrel 啟動。不是複製 Program 註冊，也不是第二個應用程式 ServiceProvider。只手動啟動 Data Protection 預熱服務，從未啟動 GenericWebHostService 或連線 MySQL。

安全攔截：PostConfigure<KeyManagementOptions> 將 XmlRepository 換成 TrapRepository 並禁止自動生成（僅測試）。GetAllElements 回空集合並記錄堆疊，StoreElement 直接拒絕，無檔案存取。測試設定為 Temp 唯一 JSON、獨立隨機 JWT／未註冊假 DB 密碼，finally 刪檔及還原環境。這不是把停用產生金鑰套用到正式程式。

修正前真實註冊：環境為 IsolatedTest、IDataProtectionProvider 為 EphemeralDataProtectionProvider，仍有兩個 IHostedService：DataProtectionHostedService 與 GenericWebHostService。Build 本身 Repository 讀寫為 0；只啟動前者即捕捉：

註冊階段快照另確認：CreateBuilder 後 IDataProtectionProvider=0／DP HostedService=0；AddAntiforgery 後兩者各為 1。Data Protection 不是 CreateBuilder 單獨建立即有，而是後續 Antiforgery／AddDataProtection 帶入；CreateBuilder 的 Logging defaults 與此不同，勿混為一談。

```text
DataProtectionHostedService.StartAsync
  -> KeyRingProvider.GetCurrentKeyRingCore
  -> KeyRingProvider.CreateCacheableKeyRingCore
  -> XmlKeyManager.GetAllKeys
  -> TrapRepository.GetAllElements
```

因此已確認觸發服務是框架獨立的 **DataProtectionHostedService**，不是 Ephemeral Provider 被後續註冊覆蓋，也不需要 Cookie／Antiforgery 呼叫才能觸發。它預熱一般 key-ring provider，未沿用已替換的 IDataProtectionProvider。預設無明確 XmlRepository 時落到使用者 key ring（此前框架診斷已確認）；金鑰解密失敗／嘗試新建金鑰符合前次實際堆疊。但既有 DPAPI 為何無法解密，仍未調查金鑰內容／身分，不能確定。

程式全面搜尋沒有 PersistKeysToFileSystem、ProtectKeysWithDpapi、自訂 Cookie Authentication、AddSession 或產品內 BuildServiceProvider。醫療 Cookie 裝 JWT，只設定 cookie 屬性，不是 AddCookie handler；JWT Bearer 不使用這條磁碟 key-ring 路徑。Antiforgery 使用 IDataProtectionProvider，本批以真實 Program 的 Antiforgery 初始化證明可走記憶體 Provider、Repository 讀寫為 0。沒有發現其他覆蓋或額外產品 ServiceProvider。

### 最小修正與風險比較

- 保留 Ephemeral Provider，沒有替換金鑰方案。僅 IsolatedTest 刪除完整名稱精確相符的 `Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService` 描述，不刪其他 IHostedService，不吞初始化例外。
- Program.cs 將 LocalIsolationRuntime.Configure 移到所有 MVC／Antiforgery 等註冊之後、builder.Build 之前；避免後續 AddDataProtection 重新加入預熱服務。此為必要的註冊順序修正。
- Development／Production 完全不走此 helper 設定；Logging、Provider 描述及 HostedService 描述保持不變。
- 風險：依賴 .NET 8 框架 internal type 名稱，升級 runtime 必須重跑真實註冊測試，不能只跑 Provider 型別檢查。仍有一般 key-management 服務的註冊，未來若新增直接使用 IKeyManager 的業務服務，需要另做隔離審查。
- 若改磁碟測試 Repository，會增加暫存路徑、ACL、加密、清理責任；若改為一般 in-memory XML Repository，會變更多個 key-management 選項。均較本次移除不適用預熱服務的範圍大，未採用。

### 本批實跑（無 HTTP／MySQL）

| 模式 | 結果 |
|---|---|
| 真實 Program 修正後註冊 | 14 checks PASS；保留 Web HostedService 不啟動；無磁碟預熱；CSRF Cookie Secure／Strict 不變；Antiforgery／Protect roundtrip；Trap 讀寫 0 |
| 真實 Program 加回舊預熱服務（僅測試） | 11 checks PASS；成功捕捉上述存取鏈；禁止產生金鑰，寫入 0 |
| 隔離／其他環境回歸 | 20 PASS（原 16，加 Development／Production HostedService及 Provider 描述不變 4 項） |
| API Build | 成功，0 errors／1 NU1900，另有預覽 SDK 提示 |
| ParentQuestionnaire | 126 PASS，Fake Store／Adapter |
| EfParentStore | 86 PASS，EF／Pomelo + ADO 替身 |
| Permission／RBAC Offline／Policy | 34／22／43 PASS |

```powershell
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile -- --full-registration
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile -- --full-registration --reproduce-warmup
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile
```

第一個模式測最終註冊；第二個模式僅在測試注入舊服務，讓根因可重現。兩者皆於 HostBuilt 中止，不走完整 request pipeline／HTTP server；不能聲稱第三次 Level 1 通過。

### 停止問題：證據界線

- 真實最終 DI 為 ConsoleLifetime，ShutdownTimeout=5 秒；Generic Host 的程式化 StopAsync 先前已測通過。
- PID 7256 為 direct dotnet DLL，沒有 dotnet-run 中間程序，仍在 Ctrl+C 後存活，故多一層 dotnet-run 不是充分解釋。
- 前次只確認程序存活，未取得成功監聽或停止事件的證據；不能寫成「啟動失敗後仍持續監聽」。對該 PID 核對路徑、啟動時間後終止，exit=-1，不是正常關閉證據。
- Data Protection 預熱在本次測試遇到空 key ring 仍會返回（框架處理初始化錯誤），所以這類錯誤不必然阻止整個 Host 後續啟動；與訊號送達是不同問題。
- 尚無 Console.CancelKeyPress／ApplicationStopping 的前次觀測，無法區分 PTY 是否轉送 Ctrl+C、ConsoleLifetime 是否收到或是否卡在後續停止階段；本批不重演終端或啟動程序來推論。

下一次 Level 1 前：審查本批差異、確認上述三模式通過、重新核對版本／5186 空閒、設定假帳密與記憶體金鑰；獲明確核准後只 health。需記錄正常停止事件與程序退出，若仍無法正常停止，不得視為驗收成功。必要終止仍須確認 PID／路徑／啟動時間與命令列，不碰其他服務。
本批無 HTTP listener／MySQL／Migration／Seed／系統環境修改，臨時設定均清理。既有 8 檔保留，加 1 檔 FullRegistrationProbe.cs，未 Commit。16-11 仍待實際驗收。

## 2026-10-25 第二次實際驗收：未通過（本節優先於下方歷史敘述）

Worktree `.codex-worktrees/v53-parent-core`，branch `codex/v53-parent-core`，HEAD ca3b540bdbaa09d42ab1fd6be67a7b5171acae47 加本批未提交 8 個檔案。未 Commit／Push／Merge。

審查：runtime helper 僅精確 IsolatedTest 生效；Development／Production DI 回歸未變。Cookie／CSRF／JWT／RBAC／Scope、五份 Migration／Snapshot／教師核心無差異；候選檔案沒有加入實際帳密或醫療資料。注意：DI 的 ephemeral provider 通過不等於完整啟動不會初始化預設 key ring；本次實際結果推翻先前「不碰既有 key ring」的完整啟動推論。

實際命令（PowerShell 互動命令，未使用 ps1／Bypass）：

```powershell
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile
# 另開專用終端，於 EarlyInterventionCare.Api 目錄：
# 使用 RandomNumberGenerator 產生兩組 48-byte 隨機值；只寫入 Temp 下本次唯一 appsettings.v53-second-<UUID>.Local.json。
# DB=127.0.0.1:33326 / earlycare_v53_synthetic / earlycare_v53_app；密碼為未建立帳號的隨機假密碼。
# JWT 為本次獨立 Base64 key；不輸出或保留內容。
# try 前保存三個環境變數；try 中設定：
$env:ASPNETCORE_ENVIRONMENT='IsolatedTest'
$env:DOTNET_ENVIRONMENT='IsolatedTest'
$env:EARLYCARE_LOCAL_SETTINGS='<本次 Temp 私人設定絕對路徑>'
dotnet ./bin/Debug/net8.0/EarlyInterventionCare.Api.dll
# finally：還原三個環境變數，只 Remove-Item 本次精確設定檔。
```

- Build：成功 0 errors／1 NU1900（弱點索引不可取得），另有預覽 SDK 提示。
- LocalIsolationSafety：16 passed／0 failed，Generic Host／DI／CSRF，無 HTTP／DB；此測試未覆蓋完整 Web Host 啟動初始化。
- 實際 API：PID 7256，dotnet.exe，startedUtc=2026-10-25T02:17:03.8577383Z。目標 127.0.0.1:5186；因啟動錯誤立即停止，未取得該 PID 的監聽成功證據，不可宣稱 bind 驗收通過。
- 再現 DpapiXmlDecryptor／XmlKeyManager 的 CryptographicException，並有 FileSystemXmlRepository.StoreElementCore 寫入既有目錄被拒；表示仍有 key-ring 初始化路徑。具體觸發服務尚待調查，不擅自歸因為特定 HostedService。
- 本次輸出未見 Event Log 權限錯誤，但不足以宣稱全情境均無此問題。
- 未呼叫 /health/local，沒有 health response；未呼叫任何業務 API。
- Ctrl+C 後程序仍存在；核對 PID、dotnet 完整執行路徑及與啟動輸出一致的 StartTime，再 Stop-Process -Id 7256。這不是正常關閉通過；API exit=-1。
- finally 回報 ENV_RESTORED=True、PRIVATE_SETTINGS_REMOVED=True。結束後 PID 消失、5186／33326 無監聽，MySQL80 仍 Running、原 3306／33060 PID 40620 不變。
- TCP 取樣未見 PID 7256 對外／DB 連線；不是連續追蹤，不能證實完全沒有瞬間連線。
- 既有兩份 key 檔修改時間未變，沒有新可見 key 檔；只查 metadata，未讀內容。寫入遭拒不代表曾獲准修改，不能把這次啟動描述成完全未接觸舊金鑰。
- 未建庫、未連 MySQL、未執行 Migration／Seed／EnsureCreated，未修改防火牆／Execution Policy（仍 Restricted）。

16-11：第二次 Level 1 未通過，Health／正常停止仍未完成。異常後只停止、清理及記錄，沒有繼續修改程式或再啟動。下一批須先補足完整初始化路徑的離線測試，審查後再取得實際啟動核准；不進 Level 2。

基準 codex/v53-parent-core／ca3b540bdbaa09d42ab1fd6be67a7b5171acae47。本批未 Commit、未啟動 API／HTTP、未連 MySQL、未修改系統權限或既有金鑰。16-11 仍待第二次 Level 1 核准與驗收。

## 問題證據與未確認事項

首次啟動 PID 53960 曾監聽 127.0.0.1:5186；DPAPI 解密 CryptographicException，接著 Windows Event Log `.NET Runtime` 存取被拒。未完成 health，Ctrl+C 未正常結束，核對程序路徑後停止。

本批基準 DI 顯示 WebApplication.CreateBuilder 註冊 Console、Debug、EventSource、EventLog providers。程式沒有自訂 Data Protection，AddControllers／AddAntiforgery 沿用框架預設。框架診斷確認 `%LOCALAPPDATA%/ASP.NET/DataProtection-Keys` 與 Windows DPAPI；未讀金鑰內容，未呼叫預設 provider 的 Protect／Unprotect。DPAPI 問題可能涉及執行身分／保護範圍或金鑰狀態，但無足夠證據確定根因，不修復舊 key、不重現系統寫入。

Ctrl+C 可能涉及 PTY 訊號及 dotnet-run 中間程序，尚未證實。不能把 DPAPI 錯誤直接視為停止失敗原因。

## 最小修正

- LocalIsolationRuntime 僅在環境名稱精確為 IsolatedTest 時套用。
- ClearProviders + AddConsole，不需要 Event Log 權限。
- UseEphemeralDataProtectionProvider：只在記憶體持有金鑰，不使用既有 key ring／DPAPI，不建立金鑰檔。程序結束後 token 失效，重啟須重新取得 CSRF token；不適合正式持久登入。
- Cookie Secure／SameSite、CSRF middleware、JWT、RBAC／Scope 不放寬，其他環境保持原設定。
- ShutdownTimeout=5 秒，是合作式時限，非強制殺程序，也不保證 Ctrl+C 送達。
- 啟動腳本直接 dotnet 執行已建置 DLL，避免 dotnet-run 額外程序；IsolatedTest 輸出 PID／啟動 UTC，不含秘密。

## 測試先行及實跑證據

基準 4 passed／3 failed：Console-only、ephemeral provider、停止時限均不符預期。修正後新增測試最終 16 passed／0 failed，無 HTTP／DB。
CSRF 拒絕測試一度重用同一 HttpContext，命中每 request 驗證快取；改成新 request 後通過，沒有修改正式驗證規則。

| 項目 | 本批結果 | 範圍 |
|---|---|---|
| LocalIsolationSafety | 16 PASS | DI、記憶體加解密、跨 provider／purpose 拒絕、CSRF 正反例、Generic Host 合作停止；無 Kestrel |
| API Build | 0 errors／1 NU1900 | 弱點索引不可取得；另有預覽 SDK 提示 |
| ParentQuestionnaire | 126 PASS | Fake Store／Adapter |
| EfParentStore | 86 PASS | EF／Pomelo + ADO 攔截替身，不是 MySQL |
| PermissionServiceOffline | 34 PASS | 離線 ADO |
| RbacOffline | 22 PASS | Handler／Policy |
| RbacPolicy | 43 PASS | Policy／DI |
| 教師 Phase22 proposal mode | 158 PASS | 模型提案模式，非正式 schema gate |
| 教師 Phase21 | PASS／Exit 0 | 離線指派及完成度 |
| RoundCompletion | 37 PASS | 純計算 |
| PowerShell AST | 0 errors | 只解析，未執行腳本 |

本批不執行 RbacHttp／ScopedReadSafety HTTP 測試，不將過往 HTTP 結果計為本次通過。Model／Snapshot pending gate 未解決，Migration／Snapshot 未改。

```powershell
dotnet run --project tests/LocalIsolationSafety/LocalIsolationSafety.csproj --no-restore --no-launch-profile
```

## 下一次驗收（待核准）

只用 loopback、repo 外私人 JSON、隨機假 DB 密碼及獨立 JWT，只呼叫 health。核對 PID、啟動時間、完整路徑、監聽及連線取樣。Ctrl+C 後確認 PID／5186 消失；逾時不能宣稱正常停止。
必要終止前須同時核對 PID、執行路徑、啟動時間與 command line 指向本 Worktree DLL（direct dotnet 的路徑為 dotnet.exe）。無法核對就回報，不以名稱停止所有 dotnet／MySQL。
私人設定在程序結束後僅刪除本次確認的精確檔案；不遞迴刪目錄。記憶體 provider 無金鑰檔可清理，但不宣稱能保證 OS pagefile／crash dump 記憶體抹除，不產生此類 dump。
第二次 health／終端訊號／實際 API 正常停止均未驗證；Level 1、真 MySQL、Level 2、正式部署仍未完成。
