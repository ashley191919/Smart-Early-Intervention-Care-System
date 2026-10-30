# V5.3／16-14：地端測試環境操作手冊

文件日期：2026-10-30。文件狀態：操作流程整理完成，待團隊審閱；不是本次啟動或資料庫驗收紀錄。
程式與文件核對基準：`codex/v53-parent-core`／`78ace47bb24502878d42e2b844241dbf47d697e5`，專用 Worktree `.codex-worktrees/v53-parent-core`。未來操作前必須重新核對版本及授權；本手冊不構成啟動、建庫或 Migration 的核准。

## 1. 先選擇操作層級與證據

| 層級／工作 | 目前可引用的結果 | 不包含的保證 |
|---|---|---|
| Level 1／16-11 | 2026-10-25 使用者在 Windows Terminal／PowerShell 手動啟動獨立 DLL、loopback 監聽、health、親按 Ctrl+C、合作式停止與清理通過；退出碼未取得 | 不等於目前 HEAD 重新實跑，不等於所有工具終端／腳本均通過，也不是 DB readiness |
| 離線測試 | LocalIsolationSafety 的註冊／停止生命週期；家長 Gateway Fake Store／Adapter；EF／Pomelo＋ADO 替身；詳見既有驗證紀錄 | 不證明真 Guardian／Consent、MySQL FK／唯一鍵／鎖、磁碟持久化或正式 HTTP 登入 |
| Level 2／16-08、16-12、16-13 | **測試準備完成，實際尚未驗收**；隔離 MySQL、共同 Schema、保存、重啟、備份還原均待核准及執行 | 不能以 health 200、Fake／ADO PASS 或舊開發 MySQL 紀錄代替 |
| Level 3 | 私人 HTTPS、跨網路、雲端前端、正式部署未執行 | 不在本階段交付內 |

Level 1 實測基準是 `ca3b540bdbaa09d42ab1fd6be67a7b5171acae47` 加當時 9 個未提交檔案；歷史 PID 55228、啟動 UTC `2026-10-25T09:01:00.9413685Z` 只用於辨識證據，不得拿來操作新程序。最新結論見 [Level 1 紀錄首節](v53-16-level1-isolation-fix.md) 與 [驗證紀錄首節](v53-16-verification.md)。同文件下方的失敗／待核准說明為歷史，不能當作最新結果；退出碼空白不可補成 0。TCP 取樣也不能證明完全零瞬間 DB 連線。

## 2. 環境需求與現況核對

| 項目 | 可確認的依據／操作前需求 |
|---|---|
| Windows／終端 | 已驗證路徑是 Windows Terminal 的互動 PowerShell；OS／SDK 歷史盤點見 [工具說明](../tools/LocalIsolation/README.md)。不是所有機器的相容性保證 |
| .NET／ASP.NET Core | [API 專案](../EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj) 為 Microsoft.NET.Sdk.Web、target net8.0；需能建置 net8.0 的核准 SDK 及相容的 Microsoft.NETCore.App／Microsoft.AspNetCore.App 8.0 runtime。歷史裝有 SDK 9.0.308／10.0.100-rc.2、runtime 8.0.22，不是新安裝版本建議或目前盤點 |
| 套件 | csproj 固定 EF Core Design 8.0.13、Pomelo 8.0.3、JwtBearer 8.0.31；MySqlConnector 實際解析版本由核准套件清單／assets 核對，不自行推定。還原所需來源／網路另行確認 |
| MySQL | Level 1 health 不需要建立或啟動 DB。Program 明確使用 MySqlServerVersion(8.0.46)，這是 SQL 轉譯設定，沒有 AutoDetect，不證明已安裝或核定該版本。Level 2 實例精確版本與套件相容性待核准 |
| 埠／磁碟 | API 僅 127.0.0.1:5186；DB 候選僅 127.0.0.1:33326。Level 2 需獨立 datadir／tmp／error log／PID／備份與還原空間，路徑、容量及 ACL 待核准 |

未來可先在專用 Worktree 唯讀執行 `dotnet --info`、`dotnet --list-sdks`、`dotnet --list-runtimes`，記錄實際版本；不自動安裝或切換 SDK。Build／restore 會產生檔案，須列入該次核准範圍；本次未執行。NU1900 表示弱點索引未取得，Build 成功不代表漏洞查核完成。

## 3. IsolatedTest 安全界線與設定

實際程式依據：[Program](../EarlyInterventionCare.Api/Program.cs)、[設定 guard](../EarlyInterventionCare.Api/Options/LocalIsolationSettings.cs)、[執行期隔離](../EarlyInterventionCare.Api/Options/LocalIsolationRuntime.cs)。

- `ASPNETCORE_ENVIRONMENT` 與 `DOTNET_ENVIRONMENT` 都設為精確 `IsolatedTest`；`EARLYCARE_LOCAL_SETTINGS` 指向存在、完整絕對路徑的私人 JSON。此環境不載入 Development User Secrets；不得改成 Development 來繞過隔離。
- 程式固定 URL 為 `http://127.0.0.1:5186`，拒絕 Kestrel:Endpoints override；不加 URL／Kestrel 命令列覆寫，不開 Swagger 或 Development 教師入口。一般 Controller／靜態資源仍存在，所以 Level 1 必須只請求 health，不能假定其他路由全被停用。
- 連線 guard 精確接受 host `127.0.0.1`、port `33326`、庫 `earlycare_v53_synthetic`、user `earlycare_v53_app`；密碼非空且不以 REPLACE 起頭，AllowLoadLocalInfile／AllowUserVariables 均不得啟用。guard 只解析設定，沒有證明實例身分或帳號已建立。
- 私人模板見 [settings.example.json](../tools/LocalIsolation/settings.example.json)，含不可使用的占位符。Level 1 歷史流程使用兩組獨立隨機值：未註冊帳號的假 DB 密碼、獨立 Base64 JWT key；不拿真 DB 密碼來測 health，也不建立對應帳號。JWT key 解碼至少 32 bytes，Issuer／Audience 不空、ExpirationMinutes=30；歷史值與產生方式見 Level 1 操作紀錄，不在本手冊放可用秘密。
- IsolatedTest 採 Console-only logging、記憶體 Data Protection；移除精確預熱服務而保留 Web Host。runtime 升級須重驗 internal type 名稱依賴。重啟後舊保護 token 失效，需重取測試 CSRF token；不適用正式持久登入。
- Cookie Secure／SameSite、CSRF、JWT、RBAC 不放寬。loopback HTTP health 不能驗證 Secure Cookie 登入。IsolatedTest 使用空 TrustedOrigins，不能據此宣稱跨來源業務流程可用。
- 只使用明確合成 fixture；不用真病患、手機、病歷、院所資料或 production 脫敏副本。不送真 Email／SMS；身分服務、核准測試帳號及通知替身須由 07／11 另定。合成 Consent 不是本人同意。

## 4. Level 1 操作順序（未來另行核准後使用）

### 啟動前

1. 記錄操作人、核准範圍、日期、Branch／完整 SHA、工作區差異；只在自己的專用 Worktree 執行。差異不符核准版本就停止，不清理別人的檔案。
2. Git 如需擁有者例外，只用單次命令的正斜線完整路徑，不用 `safe.directory=*` 或 global 設定。舊紀錄命令中的星號不可直接沿用。例如於本機原路徑：

   ```powershell
   git -c safe.directory=C:/Users/Rache/source/repos/ashley191919/Smart-Early-Intervention-Care-System -c safe.directory=C:/Users/Rache/source/repos/ashley191919/Smart-Early-Intervention-Care-System/.codex-worktrees/v53-parent-core rev-parse --show-toplevel
   git -c safe.directory=C:/Users/Rache/source/repos/ashley191919/Smart-Early-Intervention-Care-System -c safe.directory=C:/Users/Rache/source/repos/ashley191919/Smart-Early-Intervention-Care-System/.codex-worktrees/v53-parent-core status --short
   ```

3. 唯讀檢查 5186 所有監聽位址及 PID，例如 `netstat -ano -p tcp | Select-String ':5186\s'`。有既存 listener、查詢失敗或無法辨識時停止，不搶埠、不改埠、不殺其他程序。Level 1 不啟動 MySQL80 或新實例。
4. 核對已建置 DLL 屬於核准版本；需要 Build 時，依核准範圍使用既有 `dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore`。缺依賴就停止，另列 restore 需求，不自動下載。
5. 依下一節準備本次唯一私人 JSON，保存三個環境變數及原工作目錄，使用 try/finally。檢查不得輸出設定內容或連線字串。

### 啟動、監聽與 health

已實測的路徑是互動終端 T1 前景直接執行 DLL；完整的隨機設定建立、環境保存／還原及清理範例在 [Level 1 紀錄「下一次精確操作方案 A」](v53-16-level1-isolation-fix.md)。引用時遵守本手冊的精確 safe.directory、最新基準及退出碼限制，不整段盲貼歷史命令。

設定三個環境變數、在 API 目錄後的啟動命令為：

```powershell
# 僅於另行核准、私人設定與環境已備妥後使用；本批未執行。
dotnet ./bin/Debug/net8.0/EarlyInterventionCare.Api.dll
```

記錄 `ISOLATED_TEST_PROCESS` 的本次 PID／startedUtc、dotnet.exe 完整路徑與命令列對應的本 Worktree DLL。日誌應為 IsolatedTest／ephemeral provider；T2 以 netstat 對照本次 PID，確認它只有 `127.0.0.1:5186` 的預期監聽，沒有 0.0.0.0、[::] 或其他非預期入口。確認前不發 HTTP 請求。

T2 只發一次以下 health 請求，記錄 HTTP status 及原始最小 JSON：

```powershell
# 未來已核准的 Level 1 觀測，不用來觸發登入或資料庫查詢。
Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5186/health/local' -TimeoutSec 5
```

預期 HTTP 200、`{"status":"alive","databaseChecked":false}`。這只表示存活，不檢查 MySQL／Schema／認證／資料保存；不嘗試登入、問卷、Audit 或工具模式 `--verify-db-read`／`--create-dev-user`。後兩者在 IsolatedTest 明確拒絕。

[Start-LocalTest.ps1](../tools/LocalIsolation/Start-LocalTest.ps1) 是現有啟動工具：`-CheckOnly` 只查 dotnet、project 及其埠檢查，不讀私人設定／連 DB／開 API，也不驗 DLL、SDK 相容性、ACL 或完整網路安全。歷史 `-CheckOnly` 受 Execution Policy 阻擋，只有 AST 語法解析，不能標記為實跑通過。正常模式需絕對 SettingsPath、檔名符合 appsettings.*.Local.json 及既有 DLL，finally 還原三個環境變數，但**不刪私人檔案**。只有在政策原本允許、另核准後才使用；不加 Bypass 或修改 ExecutionPolicy。腳本不能取代人工完整監聽／清理檢核。

### 正常停止與判定

在 T1 **親按鍵盤 Ctrl+C 一次**，觀察 `ISOLATED_TEST_CTRL_C_RECEIVED`、`ISOLATED_TEST_APPLICATION_STOPPING`、`ISOLATED_TEST_APPLICATION_STOPPED`，不要求跨執行緒訊息嚴格先後；記錄提示符號返回。T2 核對本次 PID 消失且 Listen 5186 無輸出，再確認環境還原、私人設定清理。

ShutdownTimeout=5 秒是合作式權杖時限，不是自動強制終止。依既有方案等待 10 秒仍未完成即記失敗：區分未收到 Ctrl+C、有按鍵無 Stopping、有 Stopping 無 Stopped／程序仍存在；不反覆送訊號、不改 timeout。工具 stdin 傳 U+0003 不等於原生 Console 已收到 Ctrl+C。

盡可能在 native process 返回後立刻記錄實際 `$LASTEXITCODE`；空白寫「未取得」，不得推定 0。歷史手動停止通過依據為事件、提示符號與程序／監聽釋放，不以空白退出碼推導成功。強制終止不列正常停止通過；需要終止時先核對本次 PID、路徑、StartTime、DLL 命令列並取得對應核准，不能按程序名稱批次停止 dotnet／MySQL。身分無法核對就回報。

## 5. 臨時設定保護與清理

私人設定置於 repo 外、操作人專用且已核准存取範圍的目錄，使用本次唯一 `appsettings.<識別>.Local.json`；實際路徑／ACL 待核准。Git ignore 不是存取控制，現有腳本也不建立／驗證 ACL；不可把 Temp 天然視為符合機密保護。只核對必要 metadata，不把金鑰／密碼寫入終端、截圖、操作紀錄或 Git。

保存／還原 process 級 ASPNETCORE_ENVIRONMENT、DOTNET_ENVIRONMENT、EARLYCARE_LOCAL_SETTINGS 及工作目錄；不改 Machine／User 級環境。程序確實結束後，只刪本次已核對的精確設定檔，不遞迴刪目錄。使用既有啟動腳本者亦須由操作者另行完成精確清理。若 finally 未執行，先確認程序已停，再在原會話依保存值還原並核對；不猜原值或清空其他會話設定。

結束紀錄應含 `ENV_RESTORED`、`PRIVATE_SETTINGS_REMOVED` 的實際結果；失敗則保留未清理路徑的 metadata、限制存取並回報，不另複製秘密作證據。記憶體 provider 沒有本次磁碟 key ring 可刪；不動既有使用者 key ring，不聲稱 pagefile／crash dump 已抹除。

## 6. 常見錯誤與安全排除

| 訊號／錯誤 | 安全處理 |
|---|---|
| DOTNET_REQUIRED／PROJECT_NOT_FOUND／BUILD_API_FIRST | 檢查專用路徑、核准 SDK／runtime、DLL版本；缺項另報需求，不自行安裝或切其他 Worktree |
| LOCAL_API_PORT_BUSY／LISTENER_CHECK_UNAVAILABLE | 停止啟動、保留 listener metadata；不搶埠、不修改工具預檢 |
| PRIVATE_SETTINGS_FILE_REQUIRED／USE_GIT_IGNORED_LOCAL_SETTINGS_FILENAME／LOCAL_ISOLATION_SETTINGS_REQUIRED | 核對絕對路徑、存在性、檔名及環境變數；不顯示內容、不退回 Development User Secrets |
| LOCAL_ISOLATION_CONFIGURATION_REJECTED | 比對本手冊的精確 guard；常見為占位符密碼、候選帳號名不相符或禁止選項。不要改 guard、改接3306或使用管理員帳號解決 |
| LOCAL_ISOLATION_ENDPOINT_OVERRIDE_REJECTED | 停止，查核准設定來源是否含 Kestrel:Endpoints；不臨時刪設定／放寬監聽後繼續 |
| JWT設定失敗／API_PROCESS_FAILED | 保存安全錯誤碼、實際退出碼及版本；在私密環境核對 Base64／長度／30分鐘設定，不輸出key、不新增假成功 |
| Execution Policy 拒絕 ps1 | 不 Bypass、不改政策；使用已核准的互動 DLL 路徑或停下提出操作方案，不能把 AST PASS 當腳本通過 |
| DPAPI／key-ring／Event Log 初始化錯誤 | 停止本次驗收，記環境、版本與脫敏訊息；重查隔離註冊／runtime依賴，不修舊金鑰、不提高系統權限、不刪 key ring |
| health失敗／非預期監聽／非預期外部連線 | 不改呼叫業務API驗證；記錄本次程序證據，轉停止流程；必要終止另核准 |
| PARENT_ACCESS_NOT_INTEGRATED／scope拒絕 | 預設拒絕是整合邊界，不繞過；交11真Adapter或07真scope處理，health通過不會解除限制 |
| MODEL_SNAPSHOT／FK／唯一鍵／DB故障 | Level 1不處理；Level 2停止寫入並保留證據，依下一節復原決策，不自動Migration／Down／修資料 |

## 7. Level 2 啟用前置條件（全部待核准）

具體規格重用 [16-10／12／13 方案](v53-16-preparation/mysql-plan.md)、[Schema 差異計畫](v53-16-schema-plan.md)、[驗收案例](v53-16-preparation/acceptance-cases.md)，不在本手冊再建一套 SQL／Migration。

- [ ] 07 提供正式醫療 Case／Task scope、Permission需求、有效輪次決策及固定交接SHA；Permission成功仍須scope成功。
- [ ] 11 提供 Case／Guardian／Consent Entity／Configuration、可信Actor／交易Adapter、同意用途／版本／撤回政策及核定PARENT問卷。鎖定／撤銷writers與Gateway共用交易協定，不能另開context先查一次即放行。
- [ ] 共同整合SHA、增量Migration IDs／Snapshot、生成SQL及SHA256經統一審查；保留既有五份Migration。六項作者模型仍為提案；不以proposal mode、EnsureCreated或手改Snapshot繞過gate。
- [ ] 專用實例精確版本、server_uuid、port33326、datadir／tmp／log／PID與MySQL80完全隔離；僅loopback，X protocol等版本相關設定待核准，不重用3306／33060／earlycare_dev。
- [ ] Runtime／Migration逐表最小權限及SHOW GRANTS驗收方案已確認；DDL不放Runtime。現guard固定`earlycare_v53_app`，規劃文件的`v53_runtime`及`v53_migration`是候選名，不能直接套用；名稱統一與任何必要程式變更另交核准，本批不修改。
- [ ] 合成fixture、初始化順序、版本snapshot、測試帳號與通知替身已核准；無真醫療資料；不靠關閉FK檢查完成seed。
- [ ] 各操作獨立列明執行帳號、精確目標、SQL／工具參數、檔案路徑、預期影響與復原步驟，取得建立／啟停實例、建庫／帳號、Migration／寫入／備份／還原的明確核准。

候選設定與命令只供審查：[實例草案](v53-16-preparation/drafts/instance.cnf.draft.txt)、[SQL草案](v53-16-preparation/drafts/operations.sql.draft.txt)、[備份還原草案](v53-16-preparation/drafts/backup-restore.ps1.draft.txt)。隔離層級、耐久設定、保存14天及RTO60分鐘均未核定／演練，不可當SLA。

## 8. Level 2 未來驗收檢核表（預設 NOT_RUN）

| 順序／案例 | 完成條件及證據 |
|---|---|
| 身分與Schema gate | 每次連線前核對授權目標，連線後核對server_uuid／port／datadir／版本／CURRENT_USER／庫；核准SQL與hash及history一致。不符立即停，不切換目標重試 |
| 保存與重讀 DB01、RV01～03 | 正式Adapter與Controller可用後寫合成草稿，新scope／DbContext重讀；revision0→1、舊revision衝突、原內容不被覆蓋；不是localStorage或tracker快取 |
| 提交與安全 ID01～04、TX01～04、UK01、FK01、AZ案例 | 固定完整payload與原key；一Task一Response、Task提交時間一致、重送同responseId、未授權拒絕、FK／唯一鍵由真engine執行；受控故障全回滾、並行與撤銷依鎖協定序列化 |
| API重啟 DB02 | 先正常停止API並記錄，再用核准同版本／同實例啟動，新scope重讀revision／responseId／hash；獨立記錄，不冒充DB重啟 |
| MySQL重啟 DB02 | 另經核准對精確專用程序正常停啟，核對同server_uuid／datadir，重讀資料並原key replay；不停止MySQL80。崩潰復原需另准故障注入，不能由正常重啟推定 |
| 備份 BK01 | 核定版本／權限／dump選項／受限輸出路徑；單交易一致性備份期間禁止DDL；記退出碼、SHA256、schema/history、合成筆數與hash，不把秘密放參數／log |
| 獨立還原 BK01～02 | 先驗備份hash及無來源庫USE／CREATE DATABASE／跨庫寫入，再以無來源庫寫權限帳號還原至`earlycare_v53_restorecheck`；核對schema、FK／index、history、筆數/hash、revision、receipt；原庫不變 |
| 結束 | 記實際RPO／RTO、所有未執行／阻塞項、程序與設定清理、原服務狀態；沒有完整證據不標已驗收 |

重要限制：現有API guard拒絕restorecheck庫，不能只把DefaultConnection換庫後啟動Gateway驗還原。還原後的資料層查核可列入核准計畫；若要執行Gateway重讀／replay，須先交付並核准專用驗證途徑，不改guard、不用來源庫代替還原庫。備份帳號／還原帳號及匯入編碼／命令尚待版本與SQL固定，本手冊不提供可直接套用的建庫或匯入指令。

## 9. 失敗時停止、留證與復原決策

1. 目標、版本、資料、授權或監聽不符即停止受影響操作。不得改正式權限、關閉FK、使用真資料、公開入口或修改其他模組來「讓測試過」。共同規則不明就記BLOCKED，轉07／11或院方確認。
2. 保存UTC時點、核准編號、SHA、案例ID、程序metadata、安全錯誤碼、實際退出碼、已完成步驟及清理結果。DB層另存schema/history、SQL hash與交易前後合成資料hash；不存password、JWT、OTP、整份私人JSON或醫療答案。原始敏感錯誤只留已核准受限位置，不貼聊天／Git。
3. Level 1 依停止判準關閉本次程序、還原環境及精確清理；不以強制終止回填正常通過。不要動既有服務、key ring或系統政策。
4. Level 2 commit結果未知時保留原key，新scope查回後再決策；不假定rollback、不換key重送。deadlock／timeout記錄錯誤並結束整筆交易，重試策略另依核准上限，不在失敗transaction續跑。
5. Migration部分DDL失敗、持久化不一致或備份損壞時停止寫入、保留隔離現場；不自動Down／DROP／重跑／修原答案。先驗可用備份並在獨立庫驗還原，才提出專用合成庫重建或切換的具體核准申請；不對原庫直接還原。

每次可使用下列最小紀錄格式；歷史PASS不得直接貼為本次結果：

| 欄位 | 本次填寫值 |
|---|---|
| Run ID／操作者／UTC／核准範圍 | 待執行 |
| Worktree／Branch／完整SHA／差異 | 待執行 |
| Level／正式工作編號／案例ID | 待執行 |
| 模式 | 未執行／離線Fake／EF ADO／實際API／真MySQL，擇實際項目 |
| 版本／目標metadata／Migration IDs／SQL hash | 待執行，不含秘密 |
| 預期／實際／證據位置／退出碼 | NOT_RUN，退出碼不可預填0 |
| 結論／阻塞／停止與清理／下步決策 | NOT_RUN；執行後才填PASS／FAIL／BLOCKED |

## 10. 契約與責任查閱入口

本手冊只統整操作順序，不改既有實作或正式政策。共用核心、錯誤碼及07／11責任見 [Gateway交接](v53-16-preparation/gateway-handoff.md) 與 [家長核心交接](v53-16-parent-core-handoff.md)；正式權限與拒絕邊界見 [RBAC矩陣](v53-16-preparation/rbac-matrix.md)；八份準備文件與正式工作編號見 [交付索引](v53-16-preparation/README.md)。舊交接中「本目錄所在主Worktree」是原始產出背景，現副本位置以索引README及本手冊為準，不能據該舊措辭切回主Worktree操作。

正式整合仍依賴07的醫療scope／API接線與Git整合、11的Guardian／Consent／可信作者Adapter與專用Controller，以及共同Schema／增量Migration審查。預設拒絕維持；本機手冊與Level 1通過不代表家長正式流程或資料庫驗收完成。
