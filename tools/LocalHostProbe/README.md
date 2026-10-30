# 16：獨立本機前後端通訊測試包與 MySQL 審查草案

基準：`codex/v53-parent-core`／`bf8f1e8cb76c53f104cad6565791871fdb7980e5`。原始批次只有新增檔與靜態檢查；後續依使用者核准補強上游錯誤處理並執行離線替身測試。沒有開啟 Node HTTP listener／API／MySQL，沒有瀏覽器實測；以下實際啟動、HTTP正反例與停止流程全部 **NOT_RUN，須另行核准**。不是07／11正式前端、CORS／登入或DB驗收。

## 檔案與固定邊界

| 檔案 | 用途 |
|---|---|
| [server.mjs](server.mjs) | 僅Node內建node:http／node:fs／node:url；無套件、環境載入、任意代理或設定覆寫 |
| [offline.test.mjs](offline.test.mjs) | Node內建測試工具＋EventEmitter／虛擬時鐘；實際網路方法一律攔截拒絕 |
| [index.html](index.html)、[probe.js](probe.js) | 無個案資料、Cookie、storage或登入的獨立技術測試頁；按鈕才發一次health請求 |
| [初始化草案](mysql/initialize.cnf.draft.txt) | 全註解，僅初始化時路徑／case設定；不是可載入設定 |
| [執行草案](mysql/runtime.cnf.draft.txt) | 全註解，未來獨立實例設定；不是可載入設定 |

固定瀏覽器入口 `http://127.0.0.1:5187/` → GET `/health/local` → 固定 `http://127.0.0.1:5186/health/local`。只有 `/`、`/probe.js` 是靜態白名單，沒有檔案目錄服務。監聽限IPv4 loopback；任何額外命令列參數直接拒絕，無host／port／target選項。

Host必須精確為127.0.0.1:5187；Origin若有必須相同；Sec-Fetch-Site若有只接受same-origin／none，remoteAddress也限127.0.0.1。不開CORS。拒絕非GET、Cookie、Authorization／Proxy-Authorization及請求body；raw path必須完全相同，所以query、absolute URL、編碼路徑及其他/api路由都不代理。CONNECT／upgrade直接關閉。

上游headers從零建立，不複製任何客戶端header／query／body；不轉Cookie／認證、不重試、不跟redirect。上游限5秒、4096 bytes，只接受HTTP200＋JSON且精確兩個欄位status=alive、databaseChecked=false，不接受Set-Cookie／壓縮。通過才回傳實際body；其餘回安全502／504，不回上游錯誤內容。未提供帳密，未讀API私人JSON，未增加API端點。

## 本批可執行的靜態與離線檢查

在本Worktree根目錄：

```powershell
node --check tools/LocalHostProbe/server.mjs
node --check tools/LocalHostProbe/probe.js
node --check tools/LocalHostProbe/offline.test.mjs
node --test --test-isolation=none tools/LocalHostProbe/offline.test.mjs
```

`--check`只解析，不執行模組，不listen、不送HTTP。`server.mjs`現在只有直接CLI執行才會進入start；測試import只取得createRequestHandler，不建立HTTP server，也不註冊停止訊號。此函式的request／schedule／cancel注入僅供同程序替身，不提供CLI／環境自訂URL，實際target與路由仍固定。不要直接執行server.mjs或使用其他會啟動listener的測試來代替本批檢查。

離線測試採Node v24.11.1內建node:test、同程序`--test-isolation=none`，不安裝套件。import之前即攔截HTTP／HTTPS request、HTTP server建立、TCP listen／connect、UDP socket及fetch；任何嘗試都拋NETWORK_FORBIDDEN並累計，最後要求計數為0。上游request／response與下游response都是EventEmitter替身，時間以虛擬時鐘推進，沒有真socket或5秒等待。

2026-10-30實跑：**43 tests PASS／0 FAIL，exit0**。涵蓋非200／redirect／標頭早拒絕時destroy同步觸發error與aborted、上游錯誤／中斷／不完整close、兩種timeout、非預期JSON、4096邊界／超限、下游斷線、同步例外及重複完成事件；另驗拒絕路由／認證metadata不建立上游、固定target及新建headers。首次為42 PASS／1 FAIL：替身在response尚未交付前就發response error，與Node生命週期不符；修正替身後通過，不以吞例外掩蓋。這些結果只證明替身事件下的處理，不代表真HTTP、Ctrl+C或網路故障驗收。

上游response的error／aborted／close處理器位於所有早拒絕分支之前；terminal旗標在寫回／destroy之前設定，失敗／斷線後釋放buffer與timer，忽略晚到data／end／error，不移除必要error處理器。MySQL草案只做文字檢查，不呼叫mysqld驗設定、不初始化。離線／語法通過不代表HTTP、瀏覽器或資料庫設定實際通過。

## 未來啟動與停止（全部待核准）

1. 核對Worktree、SHA、差異與操作範圍，確認Node版本；不切換／清理其他Worktree。唯讀查埠：`netstat -ano -p tcp | Select-String ':(5186|5187)\s'`。已有不明程序即停，不改埠、不殺既有程序。
2. API沿用 [16-14操作手冊](../../docs/v53-16-local-operations.md) 的IsolatedTest、repo外一次性私人設定及互動DLL啟動，另核准後執行。不使用Development、真DB憑證、登入／問卷API或MySQL。
3. 在另一個真正Windows Terminal／PowerShell中，於本Worktree根目錄執行：

   ```powershell
   # 未執行；僅供下次核准後操作。
   node tools/LocalHostProbe/server.mjs
   ```

4. 記錄PROBE_READY pid、Node路徑／啟動時間／命令列；netstat須顯示該PID只有127.0.0.1:5187預期listener，API PID只有127.0.0.1:5186。不符就停止此次驗收。瀏覽器使用精確127.0.0.1網址，不用localhost或file://。
5. 開啟頁面，按一次「檢查 API 連線」。預期實際HTTP200及API JSON，databaseChecked=false。頁面成功不代表DB／身分／保存／跨來源已驗收。無API／連線拒絕應回502 UPSTREAM_UNAVAILABLE；逾時504；格式／redirect／過大／非200回502 UPSTREAM_RESPONSE_REJECTED。瀏覽器本身連線失敗顯示「未取得可用回應」，不保留舊成功。
6. 原Node終端親按Ctrl+C一次，預期PROBE_STOPPING／PROBE_STOPPED，程序退出、5187釋放；記實際退出碼，不預填0。5秒關閉逾時會關閉本server連線並exitCode=1，不當正常停止通過。API另依原手冊停止與清理，Node停止不會替你關閉API。

本工具沒有背景服務、設定檔或機密需要刪除。不以`taskkill /IM node.exe`或程序名稱批次終止。Ctrl+C／停止逾時、訊號未到、監聽未釋放時記失敗，核對本次PID／路徑／時間／命令列，再提出精確清理核准。

## 未來HTTP負向檢查清單（NOT_RUN）

可用curl.exe或瀏覽器DevTools檢查，均須另行核准Node／API執行。只用合成header值，不放真token／cookie。

| 請求 | 預期 | 上游存取 |
|---|---|---|
| GET /health/local | API正確時200且實際body；不可用時502／504 | 僅固定5186 health |
| POST /health/local | 405 METHOD_REJECTED | 無 |
| GET /api/cases、/任意檔名、/health/local?target=其他URL | 404 ROUTE_REJECTED | 無 |
| 任意absolute URL、編碼替代path | 404或HTTP parser拒絕 | 無 |
| GET health＋合成Cookie／Authorization | 400 REQUEST_METADATA_REJECTED | 無 |
| 非固定Host、其他Origin、cross-site metadata | 403 REQUEST_ORIGIN_REJECTED | 無 |
| GET含Content-Length非0／Transfer-Encoding | 400或HTTP parser拒絕 | 無 |
| CONNECT／WebSocket upgrade | 斷線 | 無 |

先驗無API失敗，再驗實際API成功；不另開模擬上游冒充API。若要做timeout／異常body注入，需另定隔離測試方案。只有靜態檢查的本批不回填任何HTTP PASS。

## MySQL 8.0.46草案審查

僅共享既有`C:/Program Files/MySQL/MySQL Server 8.0`唯讀程式；所有可寫候選路徑都在`C:/Users/Rache/EarlyCareV53Lab/mysql/`，該目錄本批沒有建立。候選結構為data、tmp、logs及runtime.pid，與既有`C:/ProgramData/MySQL/MySQL Server 8.0/Data`不同。不複製現有datadir、auto.cnf、my.ini或憑證，不設include／init-file／外部InnoDB目錄；defaults-file必須明確指定專用檔。

| 審查項目 | 初始化草案 | 正常執行草案 |
|---|---|---|
| 功能 | 供未來secure --initialize建立全新系統資料 | 供之後前景執行同一專用實例 |
| 路徑 | 相同新basedir／datadir／tmp，獨立initialize.err | 同datadir／tmp，runtime.err／runtime.pid |
| 通訊 | --initialize不是長駐網路服務，不把runtime網路參數混入初始化 | bind127.0.0.1、port33326、mysqlx=0；named pipe／shared memory停用 |
| 共同不變 | lower-case-table-names=1，必須在初始化前確認，日後不能任意變更 | 相同case設定；不載persisted globals |
| 狀態 | 全註解、未核准、未執行 | 全註解、未核准、未啟動 |

正常草案另列UTC、utf8mb4、InnoDB、候選REPEATABLE-READ、每次commit flush；不開general／slow／binary log，不宣稱PITR。記憶體大小、ACL、磁碟預算、精確啟停命令及帳號授權待核准。全註解草案**不可直接當--defaults-file使用**：缺乏生效設定會有讀取預設路徑的風險。核准後才能產生repo外有效設定，審查SHA256與每一個有效鍵，再分別核准初始化及啟動；不得只移除副檔名後啟動。

初始化若部分失敗即保留現場、不清空重試；初始化日誌可能有臨時root密碼，先核准受限目錄／ACL才可執行，不輸出／提交日誌。不使用--initialize-insecure。不建Windows Service、不改MySQL80／防火牆、不連3306或33060。路徑字串不同不等於已證明無junction／ACL風險；建立前仍須核對最終解析路徑、現有目錄及權限。

現API guard仍只接受`earlycare_v53_app`／`earlycare_v53_synthetic`／33326；草案不建帳號、不給權限、不改guard。正式Schema、Guardian／Consent及Controller仍依賴07／11；本包health不需要它們。

官方核對來源（2026-10-30查閱）：

- [Windows多實例](https://dev.mysql.com/doc/refman/8.0/en/multiple-windows-command-line-servers.html)：各實例獨立option file／data／port，defaults-file指定及依port停止。
- [初始化資料目錄](https://dev.mysql.com/doc/refman/8.0/en/data-directory-initialization.html)：先決定新datadir，secure initialize及初始化時設定，不能拿既有資料目錄重建。
- [停用X Plugin](https://dev.mysql.com/doc/refman/8.0/en/x-plugin-disabling.html)：mysqlx=0可在啟動時停用。

官方語意與文字路徑核對不等於mysqld實測驗證；這批沒有執行MySQL命令，也沒有進行1440×900瀏覽器視覺驗收（禁止啟動服務）。後續核准HTTP階段再實測並記錄，測後還原臨時viewport。
