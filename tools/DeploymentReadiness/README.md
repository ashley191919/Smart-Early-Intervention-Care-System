# 離線部署設定預檢（16）

基準：`a366d43f5be51e2b4998f3bb9756be523b194d76`。僅用 Node 內建模組，無套件安裝。

從 Worktree 根目錄執行（皆不啟動服務）：

```powershell
node tools/DeploymentReadiness/check.mjs health
node tools/DeploymentReadiness/check.mjs staff
node --check tools/DeploymentReadiness/check.mjs
node --check tools/DeploymentReadiness/offline.test.mjs
node --test --test-isolation=none tools/DeploymentReadiness/offline.test.mjs
```

CLI 僅讀取本目錄兩份合成 `*.example.json`，不接受任意路徑，不讀 appsettings、環境變數、User Secrets 或私人設定。不要把秘密貼入描述檔。可審查／修改獨立的非機密描述值；`check(config)` 亦可供離線測試呼叫，沒有業務接線。

`requiredSettings` 的四個 boolean 是**合成配置聲明**，不是機器上真的存在設定。即使全為 true，也不驗證簽章長度、密碼、連線字串、設定優先序或權限；未知、未核對時填 false。描述檔採精確欄位白名單；訊息僅輸出固定代碼及說明，不回顯輸入、檔案內容或例外。

## Profile 與結果

- `health`：重用 [LocalHostProbe](../LocalHostProbe/README.md) 的固定 `127.0.0.1:5187 → 127.0.0.1:5186/health/local`；必須為 IsolatedTest 且不宣告醫療登入。工具不建立代理、不送 HTTP，也不擴大現有路由。
- `staff`：符合現有程式限制的 **Development 同源 HTTPS 候選配置**。不是已部署方案。localhost 與 127.0.0.1、不同 Port 均非同源；跨來源醫療登入一律 FAIL，不建議放寬 CORS／SameSite／CSRF。若將來需要跨來源，須另行共同設計。
- `PASS`：描述的個別條件符合固定基準，不等於實際主機準備完成。
- `WARN`：尚未檢查／驗收的執行條件，兩份正向範例都會保留 WARN；沒有「部署已完成」總結。
- `FAIL`：明確不符合檢查契約。CLI exit 1；無 FAIL 為 exit 0，仍可能有 WARN。

檢查環境名稱一致、URL／Port 格式、本機來源、必要設定聲明、HTTPS、同源、可信來源、固定醫療 Cookie、same-origin credentials、CSRF。拒絕 Web 使用 3306／33326。**不探測任何 Port、程序或資料庫**。

同源 Base URL 前綴只檢查字串安全格式，實際路由／反向代理尚未驗證；目前 16 畫面多為根相對路徑，不能因前綴檢查通過就認為所有畫面能搬移。只支援本機測試設定；Production、其他裝置及院內部署超出本工具範圍。

## 原始碼依據與界線

- [LocalIsolation](../LocalIsolation/README.md)：沿用現有隔離啟動規則，本工具不執行啟動腳本。
- [StaffSessionOptions](../../EarlyInterventionCare.Api/Options/StaffSessionOptions.cs)：HTTPS 可信 Origin。
- [CookieService](../../EarlyInterventionCare.Api/Services/Authentication/StaffSessionCookieService.cs)：`__Host-earlycare-staff`、Secure、HttpOnly、Strict、Path `/`、無 Domain。
- [Program.cs](../../EarlyInterventionCare.Api/Program.cs)：IsolatedTest 停用醫療 Cookie；CSRF 標頭為 `X-EarlyCare-CSRF`。
- [16-14 操作手冊](../../docs/v53-16-local-operations.md) 與 [HTTP 歷史驗收](../../docs/v53-16-local-http-acceptance-20261030.md)：執行證據另列，不能由本工具取代。

未驗證實際 Cookie／CSRF Token、憑證信任、CORS、中介軟體順序、JWT、登入、Scope、HTTP、資料保存、Docker 或院內部署。未讀真實秘密，也不驗證真實配置和描述是否一致。

07／16 尚需共同確認同源 HTTPS 入口、前綴路由、靜態資源與錯誤契約；11／16 尚需 Guardian／Consent／資格與交易協定；三人需固定共同 Schema。現有 EF 模型未完成增量 Migration 前不可連接未升級舊庫。工具不解除任何限制。

測試使用合成正反例，明確比對 FAIL 代碼與輸出遮蔽；在 import 前封鎖常用 HTTP／TCP／TLS／UDP／fetch 入口。未執行 API、前端、MySQL、Migration 或正式登入驗收。
