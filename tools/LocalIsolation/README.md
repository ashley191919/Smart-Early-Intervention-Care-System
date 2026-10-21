# V5.3 loopback 隔離測試環境（未啟用）

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
