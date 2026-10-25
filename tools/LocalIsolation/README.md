# V5.3 loopback 隔離測試環境（Level 1 已驗收，Level 2 未開始）

## 最新結果：2026-10-25 Windows Terminal 手動驗收

**16-11：隔離 API 啟動、健康檢查、合作式正常停止及清理驗收通過；退出碼未取得。**

來源為使用者親自 Windows Terminal／PowerShell 操作回報，不是 agent 本次重跑。基準 ca3b540bdbaa09d42ab1fd6be67a7b5171acae47 加 9 個未提交檔案；PID 55228，startedUtc=2026-10-25T09:01:00.9413685Z，IsolatedTest／127.0.0.1:5186。Build 0 errors／1 NU1900，health HTTP 200、alive／databaseChecked=false，DP 使用 EphemeralDataProtectionProvider。

親自 Ctrl+C 後收到 CTRL_C_RECEIVED、APPLICATION_STOPPING、APPLICATION_STOPPED；提示符號返回，PID與 Listen 5186 查詢無輸出。ENV_RESTORED=True、PRIVATE_SETTINGS_REMOVED=True。**API_EXIT_CODE 空白，未取得明確退出碼，不記為 0。**MySQL80仍 Running；未做 Migration、MySQL寫入或業務 API。資料庫保存、備份還原及正式部署不在此次驗收內。

下方失敗／待核准敘述均保留為歷史，當前 16-11 以本節為準。完整證據見 `docs/v53-16-level1-isolation-fix.md` 最新節。不要因此自行建立 DB、套用 Migration、啟動外部入口或進入 Level 2；仍需另外核准。

## 歷史操作與調查紀錄

## 停止專項補充（本批未開 API）

非 HTTP 停止測試 13 PASS、真實 Program DI 15 PASS：僅一個 ConsoleLifetime，IsolatedTest 停止權杖時限 5 秒；不是強制殺程序時限。工具傳入 U+0003 未能證明 Windows Ctrl+C 訊號到達。先建議在真正 Windows Terminal 的 PowerShell 直接啟動 DLL、實際按鍵 Ctrl+C；不需修改 API lifetime或本腳本。若改受控同程序 StopApplication runner，需另外實作及核准，不加 shutdown HTTP API。完整互動命令、清理及判準見 `docs/v53-16-level1-isolation-fix.md` 最新節，仍待核准，16-11 尚未全通過。

## 2026-10-25 第三次實際驗收（最新）

IsolatedTest 的磁碟 key-ring 預熱修正後，API PID 58404 已成功僅監聽 127.0.0.1:5186；GET /health/local 回 HTTP 200、alive／databaseChecked=false，未見 DPAPI／Event Log 初始化錯誤。
本次加入只在 IsolatedTest 的 CTRL_C_RECEIVED／APPLICATION_STOPPING／APPLICATION_STOPPED 訊息。透過測試終端送 Ctrl+C 後未見這些訊息，PID 仍監聽；核對 PID、dotnet 路徑與啟動時間後才終止。私人設定及環境已清理，5186 無監聽。
**啟動與健康檢查通過，停止驗收未通過；Level 1 尚未全部完成。**不要將強制終止等同正常停止，仍需另行核准後驗證訊號傳遞；不自行改權限、不進 Level 2。精確命令／PID／時間／證據限制見 `docs/v53-16-level1-isolation-fix.md` 最新節。

## 2026-10-25 第三批調查補充（優先於下方歷史說明）

已用真實 Program 的 HostBuilt 攔截確認 DataProtectionHostedService 獨立預熱磁碟 key ring。IsolatedTest helper 已移至所有註冊最後，僅移除此預熱服務，記憶體 Provider及其他環境不變。實跑無 HTTP 的真實註冊／重現／回歸 14／11／20 項通過，仍未重做 Level 1；internal type 名稱依賴須隨 runtime 升級重驗。詳細證據、風險與 Ctrl+C 未決事項見 `docs/v53-16-level1-isolation-fix.md` 最新節。再次啟動仍需明確核准。

## 2026-10-25 第二次驗收狀態（優先於歷史說明）

Level 1 再次未通過：Build／16 項離線測試成功，但完整 API PID 7256 仍初始化既有 key ring，發生 DPAPI 解密與寫入權限錯誤；未執行 health、未取得監聽成功證據。Ctrl+C 未正常退出，核對 PID／路徑／啟動時間後終止；設定及環境已還原，5186 無監聽。未見本次 Event Log 錯誤不等於全面驗證成功。**記憶體 provider 的 DI 測試尚不足以保證完整啟動隔離，不要依下方舊說明直接重跑。**
實際命令、PID／時間、清理及資料庫連線取樣限制見 `docs/v53-16-level1-isolation-fix.md` 最新節。先補完整初始化路徑的測試與修正並審查，再取得下一次啟動核准；不進 Level 2。

## 2026-10-22 修正補充（優先於下方歷史描述）

首次 Level 1 因 DPAPI／Event Log 錯誤未通過；本批不重跑 API。詳見 `docs/v53-16-level1-isolation-fix.md`。
IsolatedTest 使用記憶體 Data Protection 與 Console-only logging；不碰既有 key ring，重啟須重新取得測試 CSRF token。
腳本改為直接執行建置 DLL；只解析語法，未繞過 Execution Policy。5 秒 ShutdownTimeout 不保證 Ctrl+C 送達。
未來核准的互動啟動先設定兩個 environment 為 IsolatedTest、EARLYCARE_LOCAL_SETTINGS 為私人設定絕對路徑，再於 API 目錄執行 `dotnet ./bin/Debug/net8.0/EarlyInterventionCare.Api.dll`，finally 還原環境與工作目錄。
Level 1 僅安全啟停／health；保存、重讀、重啟及備份還原歸 Level 2。下方 D4 混合保存的舊 Level 定義不作本次驗收標準。

## D1 本次唯讀結果

- Windows NT 10.0.26200（OSVersion），CIM 無法取得可確認的 edition。
- SDK 9.0.308／10.0.100-rc.2；預設 10 RC；專案 target net8.0，ASP.NET／NET Runtime 8.0.22 存在。本批不安裝／更改 SDK。
- MySQL80 Running／Automatic；既有 3306、33060 綁 0.0.0.0。這不是公開可達證明，且本批不更動。
- API launch profile 原 localhost:5177／7127；本次未見這兩個 port 監聽。
- Firewall／OS CIM 查詢未取得結果，不能宣稱已檢查規則或未開放。未修改任何規則／路由器。
- 未發現 tailscale CLI／服務；不代表所有網路工具都不存在。未設定私人網路／Tunnel。
- tracked appsettings.json 無 DefaultConnection；僅列環境變數名稱，未檢視秘密值；User Secrets／既有實際 DB 目標仍未知，未連線。

## D2 專用環境規格

- 新隔離 MySQL 實例（須核准建立／啟動），只 bind 127.0.0.1:33326；不能重用目前未知內容的 3306。
- DB：`earlycare_v53_synthetic`；還原驗證庫 `earlycare_v53_restorecheck`，均只有合成資料。
- App user：`earlycare_v53_app`，僅本機、僅主測試庫 SELECT/INSERT/UPDATE；不授權 DDL、FILE、GRANT 或跨庫操作。DELETE 如測試需要須另審，不預先給。
- Migration user：`earlycare_v53_migrator`，限此合成庫所需 DDL／DML，不能全域權限；實際 SQL 固定後列精確權限。
- 合成 Case／Guardian／User／院所由領域 fixture 明確宣告；11 Consent 模型與核定 PARENT snapshot 未交付前不自創 seed 或臨床答案。
- 獨立 JWT key、獨立本機 JSON，檔名 `appsettings.V53.Local.json`（Git ignore），存 repo 外且限制 Windows ACL；模板不含可用密鑰。
- 日誌僅本機受限目錄，禁止 body／答案／連線字串／JWT／OTP；保留期間由三人固定。本批不建立持久 log 或修改 ACL。

## D3 已提供工具

`settings.example.json`：無機密模板，不可直接啟動。

`Start-LocalTest.ps1 -CheckOnly`：只確認 dotnet、project、5186 未用，不讀機密，不連 DB，不啟動 API。
本機政策阻止 ps1 執行，本批只做 PowerShell AST 語法解析；沒有 Bypass／變更 ExecutionPolicy。

核准並準備私有設定後，操作人員可用：

```powershell
./tools/LocalIsolation/Start-LocalTest.ps1 -SettingsPath C:\PrivateEarlyCare\appsettings.V53.Local.json
```

這是未執行的未來操作，不是目前已獲 D4 核准。需先以本機允許的腳本管理方式審查使用，不能為跑腳本任意放寬系統政策。
腳本不建庫、不 Migration／Seed、不防火牆、不 Tunnel。Ctrl+C 停止；finally 恢復 process 環境變數。
IsolatedTest profile 不載 Development User Secrets，明確載指定 JSON；強制 URL `http://127.0.0.1:5186` 並拒絕 Kestrel endpoint override。
DB guard 僅接受精確隔離端點／庫／app user，拒絕空／模板密碼與 local infile；不自動探測 DB。
`GET /health/local` 只回 alive 和 databaseChecked=false，不回帳號／環境／版本／連線資訊，不代表 readiness。
此環境不是 Development，因此不開教師 dev API／Swagger；沒有註冊家長真 Adapter／Controller。
醫療 Secure Cookie 需要 HTTPS，loopback HTTP 階段不降低既有 Cookie 保護；先以核准的合成 Bearer 測必要 API。Level3 HTTPS 另議。

## D4 待核准的精確階段

1. 建新實例／專用庫／帳號；核對 server_uuid／port 與現有實例完全隔離。
2. 固定 11 模型、整合 SHA、Migration、生成 SQL／hash，提交 `docs/v53-16-schema-plan.md` 所列核准計畫。
3. 在合成庫套用／seed／使用帳密，啟動上述 API；Level1 驗保存與重讀，不以 health 代替。
4. 關閉再啟動同一 API／同一合成庫，驗重讀；才算重啟證據。
5. 核准備份：精確來源同 port／DB，以互動方式提供專用帳密，使用一致性邏輯備份，輸出至 repo 外受限路徑；記錄 SHA256、schema/history 與合成筆數。
6. 核准還原：只到新 `earlycare_v53_restorecheck`，核對 hash／Migration history／合成資料；不覆蓋原庫。不自動 DROP；還原與刪除需分開明確核准。完成後才有 Level2 證據。
7. Level3 私人網路／HTTPS／雲端來源，另交精確連線及同來源／CSRF 計畫；不開 MySQL 公開 port。

本次 Level1、2、3 均未完成；個人電腦不是核准的院內正式系統。
