# 11/11 前後端通訊最小展示

基準：`codex/v53-parent-core`／`70912664a263fcdf98d2e4090380e763570bc306`。這是可運行的前後端**分程序通訊**，不是Case／Task或資料庫展示。原wwwroot與醫療端UI不變。

## 啟動：兩個獨立PowerShell視窗

先各自切到本Worktree根目錄。

**視窗A：ASP.NET Core後端，127.0.0.1:5186**

```powershell
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -p:NuGetAudit=false
dotnet EarlyInterventionCare.Api/bin/Debug/net8.0/EarlyInterventionCare.Api.dll --local-communication
```

應出現`COMMUNICATION_READY pid=... url=http://127.0.0.1:5186 database=not-registered`。建置失敗就停止；缺套件時不要盲目繼續。本模式不讀appsettings／User Secrets／私人JSON，不需JWT key或資料庫密碼。不使用Development／IsolatedTest profile，也不建立假帳號。

**視窗B：獨立HTML／CSS／JavaScript前端，127.0.0.1:5187**

```powershell
node frontend/local-communication/server.mjs
```

需要已安裝Node；本次使用v24.11.1，僅內建模組、不需npm install或前端build。應出現`PROBE_READY pid=... url=http://127.0.0.1:5187`。

瀏覽器開啟 **http://127.0.0.1:5187/**。頁面載入後自動請求一次，也可按「發送 GET /health/local」重新檢查。成功畫面顯示：

```text
HTTP 200
{"status":"alive","databaseChecked":false}
```

可開啟瀏覽器Network檢視GET／health/local。瀏覽器發往5187，固定開發Proxy再向5186發送同一個GET；不是瀏覽器直接跨來源CORS驗收。

## 停止與獨立性展示

兩個視窗都可輸入`stop`再按Enter，走合作式正常停止；也支援各自視窗Ctrl+C。看到`COMMUNICATION_STOPPED`／`PROBE_STOPPED`與提示符號返回後確認程序結束。不要把`stop`貼到其他終端、SQL或正式服務。

1. 保持API運作，停止前端：5186 health仍應200；前端停止不會控制API程序。
2. 重新啟動前端，保持前端運作，停止API：前端頁面仍200，重新檢查health會得到502／UPSTREAM_UNAVAILABLE，不能顯示為成功。
3. 重啟API可再次取得200。前端與後端都不由對方啟停。

若5186／5187已被其他程序佔用、listener啟動失敗或回應不符，停止本次展示，不改埠、不殺陌生程序、不改防火牆。只能使用精確127.0.0.1網址，不以localhost或區網IP取代Host。

## 安全設計與實際修改

- [Program分流](../../EarlyInterventionCare.Api/Program.cs)：EF設計模式判斷後、一般WebApplication.CreateBuilder之前，僅單一`--local-communication`參數可進入。其他參數組合拒絕。未傳此參數時原啟動／認證／權限流程保持原樣。
- [LocalCommunicationHost](../../EarlyInterventionCare.Api/Options/LocalCommunicationHost.cs)：CreateEmptyBuilder只加入Kestrel與Routing，固定單一loopback網址。**沒有DbContext、EF provider註冊、Controller、認證、業務Service、DataProtection磁碟key ring或設定檔載入**。唯一應用端點是GET health，其他業務路由不存在，不是移除既有API的授權。
- Node伺服器重用[LocalHostProbe](../../tools/LocalHostProbe/server.mjs)既有固定上游、來源／方法檢查、逾時、大小限制與錯誤處理；只增加可由啟動程式提供靜態白名單及合作式停止控制，不提供HTTP／CLI自訂proxy目標。
- 前端只能代理GET `/health/local`到127.0.0.1:5186；其他API、query、POST、Cookie／Authorization均拒絕。沒有CORS開放、登入憑證轉送或正式醫療API Client。
- [app.js](app.js)重用既有[probe.js](../../tools/LocalHostProbe/probe.js)，只做自動首次請求及本機viewport量測；無外部字體、統計或第三方請求。配色與字體沿用原登入頁風格。
- databaseChecked=false只表示沒有檢查資料庫。沒有Migration、Schema變更、MySQL帳號、寫入或讀取；33326與MySQL80均未操作。
- 通訊模式不啟用新增的全域例外Middleware、Parent Bridge或任何業務入口。不要拿它取代登入／Cookie／CSRF／Scope驗收。

## 2026/11/08實際證據

| 項目 | 實際結果 |
|---|---|
| API build | 0警告、0錯誤；既有SDK為.NET10 RC，目標net8.0 |
| Node語法與既有proxy測試 | node --check通過；原43項全部通過，沒有新增無關Harness |
| 兩個獨立程序 | API PID3096、前端PID21472／後續重啟29132；由各自Ready輸出與Get-Process確認 |
| 直連API health | 真實HTTP200，alive／databaseChecked=false |
| 前端頁面與proxy health | 真實HTTP200，Proxy回傳API原始安全JSON |
| 瀏覽器 | 全新臨時Profile的Edge無頭模式，真實執行頁面JS後DOM顯示HTTP200與後端JSON；不是靜態HTML假結果 |
| 1440×900 CSS內容區 | DOM量測確認、無橫向溢出，並已查看截圖；未變更既有使用者瀏覽器viewport |
| 停前端不影響API | 前端正常stop／PID消失後，API仍200 |
| 停API不影響前端 | API正常stop／PID消失後，前端頁面仍200，health Proxy回502 |
| 邊界 | API其他業務路由404、health POST405；前端其他API404、Authorization400、不可信Origin403、query404 |
| OS網路盤點界線 | 此執行環境的OS listener列表僅部分可見且曾顯示PID0；不宣稱完成完整PID／socket對照。API另外驗證Kestrel單一地址、Node固定IPv4 bind，實際HTTP均使用127.0.0.1 |

Edge沙箱內第一次因GPU子程序權限失敗；改以獨立臨時Profile在沙箱外完成，沒有停用瀏覽器安全機制或讀取既有登入資料。無頭瀏覽器實測不等於已在你的日常Chrome／Edge視窗手動演練。

### 使用者一般Edge手動確認（2026/11/08，使用者回報）

使用者已親自確認獨立前端可開啟、頁面JavaScript成功呼叫後端、顯示HTTP200及`{"status":"alive","databaseChecked":false}`。此為一般Edge手動演練證據，與上方無頭瀏覽器紀錄分開；不代表Case／Task、登入或資料庫驗收。保存本批成果時保留正在運行的前後端程序，不因編譯或Git操作停止服務。

星期三可以展示：兩個視窗／程序、不同Port、前端畫面、GET請求與真實200／JSON，以及API停用時的502。不能展示：醫療登入成功、Case／Task／Completion、正式CORS／跨裝置、MySQL保存、家長資格或院內部署。原wwwroot中既有Case／Dashboard展示資料與本頁沒有連動，不作API成功證據。

建議下一步：你在Windows桌面以兩個PowerShell視窗與日常瀏覽器照本文件演練一次，確認會議接受health通訊層展示；不用先建資料表或等待Consent。正式業務整合另待07／11固定契約及共同Schema。
