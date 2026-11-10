# V5.3／16-14：地端環境現況與 2026/11/11 會議展示清單

整理日期：2026/11/10。來源分支：`codex/v53-parent-core`；固定成果 Commit：`b4afb6e16b752e5f974b96e42c785673a3920240`。本文件是驗收現況與操作清單，不是啟用業務 API、變更資料庫或套用 Migration 的授權。文件整理時未啟停服務、連線 MySQL 或重跑測試。

## 一、可對老師說明的結論

已完成 Windows 桌機上「獨立前端程序 → 固定本機 Proxy → 獨立 ASP.NET Core API」的真實 HTTP 通訊。前端是 HTML／CSS／JavaScript，由 Node 提供；後端目標 net8.0。兩者不同程序、不同 Port，不依賴正式帳號或 MySQL 資料即可展示。

這不是完整醫療系統、正式跨來源 CORS、院內部署或資料庫驗收。`databaseChecked=false` 明確表示沒有檢查資料庫，不可解讀成連線成功。醫療登入、Case／Task／Completion 與家長正式入口均不在此通訊模式內。

## 二、證據分級與工作現況

- **已實測**：桌機程序或真實 HTTP／瀏覽器已有紀錄；只代表該次、該範圍。
- **離線 PASS**：編譯、合成資料或替身測試成功，不代表完整 HTTP 或 MySQL 驗收。
- **規劃**：已有文件／操作草案，但未執行。
- **BLOCKED**：有相依或核准尚未完成，不可記為成功。

| 工作 | 狀態 | 已有證據 | 未涵蓋／阻擋 |
|---|---|---|---|
| 16-09 地端環境檢查 | 部分完成 | 前輪唯讀查得 Windows NT 10.0.26300、X64；.NET／ASP.NET Core Runtime 8.0.22、SDK 9.0.308／10 RC、Node 24.11.1、MySQL 8.0.46 執行檔 | Windows edition 與完整 listener 對照受查詢權限限制；未見 .NET 8 SDK，不代表無法建置 net8.0；環境變更後須重核 |
| 16-10 隔離 MySQL | 部分完成 | 使用者先前回報已建置 127.0.0.1:33326、earlycare_v53_synthetic 及 CRUD 帳號 earlycare_app | 本輪未登入核實實例、Schema、權限；Guard 要求 earlycare_v53_app，名稱不一致，BLOCKED |
| 16-11 API 啟動 | 已驗收，限通訊 | 前輪 API health、前端首頁、前端 Proxy health 均真實 HTTP 200；一般 Edge 手動 JS 呼叫已由使用者確認 | 不是正式醫療 HTTPS Host、認證、Permission／Scope 或業務 API 驗收 |
| 16-12 資料保存 | 尚未驗收 | 有離線核心與 EF 替身測試、驗收案例規劃 | 沒有正式真 MySQL 寫入後以新 DbContext 重讀證據；BLOCKED |
| 16-13 重啟／備份還原 | 尚未驗收 | 有操作草案；通訊程序獨立啟停已有歷史紀錄 | 未完成帶資料 API／MySQL 重啟持久化、備份及獨立還原；通訊重啟不等於資料保存 |
| 16-14 地端操作文件 | 本批文件整理完成，待審查 | 通訊 README 提供啟動／停止／故障處理，本文件提供現況與會議清單 | 真 MySQL 流程仍待核准及實測，不能將文件完成記為其驗收成功 |

前輪 2026/11/10 唯讀觀測：API PID 14928、Node 前端 PID 25768 存在；兩個 health 回應皆為 `{"status":"alive","databaseChecked":false}`。PID 是歷史觀測值，不是啟動參數，也不保證 11/11 當天仍相同。本文件編寫期間未重新發送請求或確認程序存活。

離線證據引用而非本輪重跑：通訊 README 記錄 API build 0 警告／0 錯誤、Node Proxy 43 項通過；共同 Schema Harness README 記錄 177 PASS、0 FAIL、28 BLOCKED（退出碼 2）。這些結果不能取代 Schema 升級或 MySQL 引擎驗收。

## 三、11/11 最小展示操作單

前提：桌機一般 Edge、兩個已知用途的啟動視窗，僅使用 127.0.0.1。詳細啟動與故障處理見 [通訊 README](../../frontend/local-communication/README.md)。**已在執行就不重新啟動、不重新建置、不停止程序。** 若已停止，依 README 在兩個獨立 PowerShell 視窗啟動；埠占用或模式不符就停止本次展示操作，不殺不明程序。

| 順序 | 人工操作 | 通過條件／記錄 |
|---|---|---|
| 1 | 核對兩個視窗 Ready 訊息及實際 PID | API 為 --local-communication／127.0.0.1:5186；Node 為 127.0.0.1:5187。可用 Get-Process 核對；監聽查詢無權時如實註記，不宣稱完整 OS socket 驗收 |
| 2 | Edge 開 http://127.0.0.1:5187/ | 前端頁面可載入；不是其他 wwwroot Demo 頁 |
| 3 | F12 → Network，點擊 Health 查詢 | 新 GET /health/local 指向 5187，取得真實 HTTP 200 |
| 4 | 比對頁面與 Response | status=alive、databaseChecked=false；若 502／逾時／其他內容，記失敗，不回退靜態成功 |
| 5 | 向老師說明請求路徑 | 瀏覽器 → Node 5187 → 固定 Proxy → API 5186；不同程序，可分別維護；未使用正式認證或資料庫 |
| 6 | 保存本次安全證據 | 記時間、Commit、兩個 PID、狀態與最小 JSON；可截頁面／Network。此表尚非 11/11 已執行紀錄，不預填成功 |

本次不演練停止／重啟、不開業務頁登入、不查 MySQL；不為展示修改 Cookie、CSRF、Scope、Permission 或防火牆。HTTP 200 health 不是完整前後端業務串接。

## 四、通訊模式安全界線

- [LocalCommunicationHost](../../EarlyInterventionCare.Api/Options/LocalCommunicationHost.cs) 使用獨立 Host，只註冊必要通訊元件；不載私人設定，不註冊 DbContext、業務 Controller 或正式認證。
- 前端只代理固定 health GET；不轉送 Cookie／Authorization，不開任意上游或任意 Origin。
- 前後端固定 loopback；不是區網或外網服務。既有 wwwroot、正式登入、家長 Deny Adapter 不變。
- 未操作 3306／33326、Migration、Snapshot、Schema 或真實病患資料。原有醫療 UI 的 Demo 資料不是本次 API 證據。

## 五、後續阻擋與責任

| 項目 | 未完成內容 | 責任／前置條件 |
|---|---|---|
| 共同 HTTPS Host | 可信來源、憑證、Secure Cookie、CSRF、Middleware／DI 完整 HTTP 驗收 | 07＋16 整合，11 契約 review；另案核准，不能將本機 HTTP 改為登入替代品 |
| 正式醫療 Task／Completion | GET 專用 Task Permission、可信 Case Scope Adapter、錯誤映射及共同 Host 註冊 | 07 交付固定 Adapter；11 提供 CaseAccess 相依；16 保留原角色／Development Gate、POST Assign 不變並實作 GET Guard |
| 共同 Schema | Runtime Model 與歷史基準有候選差異，增量 Migration／Snapshot 未完成 | 11／16 統一模型；07 核對 Workflow 需求；保留五筆歷史 Migration，不重複建立 History |
| Case／Guardian／Consent 政策 | Case 舊資料、Guardian 唯一性、Consent 再同意／歷史、資格世代與共同鎖序 | 三人共同確認；涉及資料保存與授權政策由老師／院方核准，不自動回填 |
| 隔離帳號與實例核實 | earlycare_app 為使用者回報；[Guard](../../EarlyInterventionCare.Api/Options/LocalIsolationSettings.cs) 精確要求 earlycare_v53_app，庫名 earlycare_v53_synthetic、port 33326 | 先另行授權唯讀身分／權限驗收；不猜密碼、不直接改帳號或放寬 Guard。帳號名僅記錄技術規劃，不含密碼 |
| 保存與復原 | 真 engine FK／唯一鍵、寫入重讀、故障回滾、API／DB 重啟、備份及獨立還原 | Schema 與合成 fixture 核准後，分階段授權實測；DDL 不交由 CRUD runtime 帳號執行 |

16 現在可獨立完成：通訊演練、證據整理、文件審查與安全故障說明。不需為 health 展示等待 Consent。正式醫療查詢、資料保存與復原需依上表接線及授權，不以離線 PASS 提前宣稱完成。

## 六、文件使用順序

1. 本文件：現況、11/11 展示與阻擋。
2. [通訊 README](../../frontend/local-communication/README.md)：本次安全模式的實際啟動／停止及故障處理。
3. [既有地端操作手冊](../v53-16-local-operations.md)、[LocalIsolation README](../../tools/LocalIsolation/README.md)：不同的 IsolatedTest 路徑與未來資料庫驗收規劃，不能混用同一 Port 啟動兩個 Host。舊文件含歷史狀態／候選名稱，不代表目前已驗收。
4. [共同 Schema Harness](../../tests/CommonSchemaReadiness/README.md)：離線模型證據及政策 BLOCKED，不能授權 Migration。

異常時保留安全錯誤碼、時間與模式，不貼秘密設定或原始內部例外；停止受影響展示操作不等於自行終止既有服務。下一步先審查本文件並照清單演練，資料庫操作另案安排。
