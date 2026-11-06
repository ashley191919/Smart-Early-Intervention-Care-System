# 新版Parent Gateway Bridge離線契約驗證

固定11：`d268b7d70be33e04d44bdfa044b13339448815f6`；16基準：`4fe7b6a8359a0856d4b5ef09281ba81bea8a1826`。

## 執行與結果

從16 Worktree根目錄執行：

```powershell
dotnet restore tools/ParentGatewayBridge/OfflineCurrent/ParentGatewayCurrentHarness.csproj --source tools/ParentGatewayBridge/OfflineCurrent -p:NuGetAudit=false
dotnet build tools/ParentGatewayBridge/OfflineCurrent/ParentGatewayCurrentHarness.csproj --no-restore -p:NuGetAudit=false
dotnet tools/ParentGatewayBridge/OfflineCurrent/bin/Debug/net8.0/ParentGatewayCurrentHarness.dll
$LASTEXITCODE
```

只使用已快取套件與本機還原來源；缺少快取時停止，不自動下載。編譯產物在忽略的bin／obj，不應提交。

本批責任分層後實跑：**142 PASS、0 FAIL、5 BLOCKED，退出碼2**；編譯0警告、0錯誤。FAIL優先於BLOCKED（只有BLOCKED時退出碼2）。歷史結果73 PASS／1 FAIL／4 BLOCKED的單一FAIL來自將Pipeline責任放在Filter；並非已完成安全修復。安全要求移至獨立PIPELINE_SAFETY阻擋項，不得移除或視為PASS。

**責任分層：**11的`ParentModuleErrors.OnException`只處理其已知分支。兩種一般非預期例外（InvalidOperationException／ArgumentException）必須保留原Exception物件、ExceptionHandled=false、Result=null，不改寫HTTP狀態、不開始回應或誤套已知錯誤碼。這是交由上層處理的Filter契約，不是已經安全處理整個請求。

[PipelineSafetyReadiness.cs](PipelineSafetyReadiness.cs)唯讀核對16 `4fe7b6a`的Program與PermissionLookupExceptionMiddleware固定blob，識別未明確註冊UseExceptionHandler／AddExceptionHandler／AddProblemDetails，以及權限Middleware只捕捉PermissionLookupException。PASS只代表來源盤點與基準一致，**不代表Pipeline安全驗收**。若來源漂移，基準檢查FAIL並需重新審查；即使未來出現這些API名稱，PIPELINE_SAFETY也仍保持BLOCKED，不靠字串存在自動放行。工具不啟動WebApplication、不建造或執行HTTP Pipeline。

目前缺少通用安全回應處理。不能由Filter返回推論完整Pipeline必定洩漏，也不能宣稱已統一回503。一般未知錯誤用500或503、錯誤碼與格式尚待11／16確認；已知核心錯誤的503對應維持原契約。可以作為「分層契約測試＋明確就緒度阻擋」獨立保存，不能當作安全修復或正式接線授權。

## 來源與編譯隔離

[source-manifest.json](source-manifest.json)記錄五份完整來源路徑、固定Git blob、snapshot、編譯檔、擷取起點／長度與前綴。`blob`是固定11 Commit的**原始Git物件ID**，不是另外產生的正規化雜湊；已逐份核對五個原始物件均為嚴格UTF-8、LF且無BOM。行尾相容規則見下節。擷取位置以原始LF文字的.NET UTF-16字元索引計算；只補manifest明列的using／namespace前綴。

- ParentQuestionnaireContracts：只擷取ParentGatewayContract、八欄Context、Request／Response DTO及四方法介面；不編譯後面的資格Access實作或Unavailable Gateway。
- ParentCorePayloadMapping、ParentCoreHttpErrors：編譯固定版本完整原文。
- ParentModuleRegistration：只擷取真實ParentModuleErrors類別；**不編譯或執行業務註冊**。
- CaseAccessService：只擷取真實CaseAccessException；不編譯CaseActor或業務Service。

重用[既有Mapper](../../ParentBridgeMapper/ParentBridgeDtoMapper.cs)及[拒絕骨架](../ParentGatewayBridgeSkeleton.cs)，不修改它們。沿用歷史編譯旗標`PARENT_DTO_26E8B7F`只是選擇Mapper的Prepare／Restore分支，**本專案實際DTO來源是d268b7d，不是26e8b7f**。正式API不引用本工具，測試快照不進正式API、DI或Controller。

## 已驗證與界線

- 八欄Context含nullable、optional共用Evidence；Evidence型別來自正式API唯一宣告。保留Context、ClaimsPrincipal及Evidence原物件，不從ID建立Principal。
- 合成Evidence所有世代、ABA及Stamp均為NULL。僅驗資料保留與缺欄位提示，不是資格證明，不產生Stamp。
- 四方法簽章、ulong Revision邊界、非法Revision、NULL草稿關係／提交關係必填、Draft／Receipt映射與UTC／Replayed保留。
- Idempotency-Key為Submit方法獨立Guid參數；不是Command內容或Hash欄位。**固定DTO沒有RequestId**，沒有創造RequestId映射。正式轉送尚未實作，無法宣稱Key已經跨Bridge保存到資料庫。
- 真實錯誤轉譯的404／409／503、未知Code／Status不符回退503、公開輸出不含合成內部標記，及Filter已知Workflow／Timeout分支。
- 四方法對NULL Evidence、匿名／不同作者形狀及NULL Context均拒絕503。骨架無欄位、無注入Store或轉送建構子，所有呼叫都拒絕；這不是授權成功或真實資料庫無副作用驗收。

## 五項BLOCKED

1. 可靠世代／ABA／Stamp、共同鎖序與交易內資格重驗未落地。
2. 正式Bridge轉送、Controller／DI與Guardian／Consent授權仍關閉。
3. MySQL原子性、共同Schema／Migration及HTTP完整管線未驗收。
4. RequestId不在這份DTO契約內；不得自行創造它與Idempotency-Key的轉換規則。
5. PIPELINE_SAFETY：通用例外安全回應未實作／驗收，HTTP狀態與共同格式尚未核定。

本工具不建立DbContext、Store、Socket或HTTP Server，不呼叫資料庫、Migration或API入口，不讀取私人設定。不執行原始來源中的註冊／Service程式。原有Schema Gate及模型政策BLOCKED保持原樣。

## 未來Pipeline驗收條件（未實作、未執行）

| 情境 | 必要條件 |
|---|---|
| 一般非預期例外、尚未開始回應 | 由共同核定的全域Handler接住；非2xx且符合核定的500／503策略；固定安全訊息與可追蹤識別碼，不能回傳Exception.Message、stack trace或內部合成標記 |
| 已知Filter分支 | 保留既有404／409／503及安全訊息，不被全域Handler錯誤覆寫 |
| 回應格式 | 包含核定HTTP狀態、固定安全訊息與非空traceId；ProblemDetails或既有JSON格式待共同決定，不能單靠AddProblemDetails宣稱通過 |
| 回應已開始／用戶取消 | 不再次寫入JSON或冒稱成功；依共同核定行為結束／中斷，保留安全的追蹤資訊 |
| 環境與順序 | Handler必須覆蓋受保護的下游執行；Development的詳細例外頁不能當成外部安全回應；Production／IsolatedTest需驗證，不以預設空白500替代統一契約 |
| 驗證層次 | 先以不開Socket的合成RequestDelegate／HttpContext測試真正Handler，之後才在另外授權下做完整HTTP驗收；本Harness目前沒有提供或執行Handler |

本批修改限Program、專案檔、README並新增唯讀Pipeline盤點類別；五份11來源snapshot／擷取片段與manifest保持原樣。正式Program、Middleware、Filter與所有授權入口均未修改。
## Windows／跨平台行尾與來源完整性

- 本目錄新增[.gitattributes](.gitattributes)：`*.source.txt text eol=lf`，只影響本Harness來源快照，不修改根目錄規則。即使core.autocrlf=true，正常Git checkout也保留快照LF。檔案總數因此由15增至16；沒有刪減原有檔案。
- manifest的五個原始Git blob、Commit、start／length均不修改。讀取使用嚴格UTF-8，先且只將CRLF轉LF，再以UTF-8重新編碼、加Git blob header並與原始物件ID比較；之後才套用原始LF／UTF-16擷取位置。因已證實這五份固定原始物件本來就是LF，此比較仍驗證同一份Git內容，沒有另換基準。
- 不允許BOM、單獨CR、空白、Unicode或其他內容變更；不trim、不正規化Unicode。編譯片段也只允許CRLF／LF差異，片段任何其他改動仍會失敗。
- 新增65項驗證：五份來源各自4組來源／編譯檔LF與CRLF組合（20項）、6種來源竄改（30項）、編譯片段竄改（5項）、非法UTF-8（5項）、Git index實際checkout filters輸出（5項）。新增測試全部通過；原77項保持。
- 30項來源負向案例包含更換首字元、增加空白、加入BOM、改成單獨CR、移除最後LF及加入Unicode文字。這些必須被拒絕，並非接受成另一個blob。
- checkout驗證使用唯讀`git cat-file --filters :tools/ParentGatewayBridge/OfflineCurrent/<file>`，於記憶體取得暫存區來源與編譯片段的正常Git輸出，再呼叫與主驗證相同的blob／擷取檢查；不執行checkout、不建立或清除測試工作樹。此為**來源checkout模擬**，不是完整乾淨機器restore／建置／HTTP驗收。
- 上述模擬需要本機Git與已暫存／已追蹤的來源快照、編譯片段。新建且尚未git add的來源會明確FAIL；不自動暫存、不Fetch、不使用網路。可從乾淨clone的索引重跑。

本次修改限Program、README及新增.gitattributes；固定來源、manifest與正式程式未變。142 PASS不代表PIPELINE_SAFETY、Stamp／ABA、共同鎖序、RequestId或任何既有BLOCKED已完成。