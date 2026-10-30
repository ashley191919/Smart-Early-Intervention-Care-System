# Parent Bridge 純資料 Mapper（未接線）

基準：16 `9d7bd31783942d61c12a64fde97585f66f6b371f`；11 `e2affb548bb427cd02d001d5bfd5761d7ecb4648`。Mapper 原始碼位於本目錄，沒有納入正式 API 編譯或 DI；未實作 IParentQuestionnaireGateway，沒有服務入口。

## 實際型別及方法

- `MapDraft(ParentDraftRequest)` → `ParentSaveDraftCommand`。
- `MapSubmit(ParentSubmitRequest)` → `ParentSubmitCommand`。
- `GetOriginalPrincipal(ParentRequestContext)` → 原本的 ClaimsPrincipal 物件；不根據 Context ID 建身分，不驗證資格、不回傳授權。

命名空間為 `EarlyInterventionCare.Integration.ParentBridge`。11 DTO 尚未在此 API Worktree，因此獨立 source 只在新測試專案編譯；未來整合者須在真實 11 模組到位後安排編譯位置，不在正式專案建立第二套 DTO。

測試 `tests/ParentBridgeMapper/Pinned11Dtos.cs` 是從固定 11 原始碼逐字擷取的五個 record 宣告（Context、Draft request/response、Submit request、Receipt），僅供測試編譯。沒有擷取 Controller、Gateway／Access 實作或 EF。它不是目前完整 11 Controller／binding 驗收；正式組合應使用真正的 11 型別，不能把此 fixture 發布到正式 API。

## 已實作的轉換規則

- Revision 委派已審查的 RevisionStringConverter：只接受 ASCII `0` 或無前導零正整數、最大 UInt64；沒有 trim／浮點數／Number。null、空白、前導零、overflow 拋 FormatException，沒有默認 0。
- 草稿 Relationship 可 null；非 null 及提交值只接受 11 現有的七個代碼：FATHER、MOTHER、GRANDPARENT、RELATIVE、GUARDIAN、CAREGIVER、OTHER。提交 null／空白／未知代碼拋 ArgumentException，不推定、不自動轉大小寫。
- pinned 11 宣告的草稿 Relationship 尚為非 nullable；測試以 null! 模擬缺少欄位，保留宣告原樣。11 後續調整 nullable 屬其所有權。
- 姓名、日期、Observation、答案值與順序保留，不建立 canonical Hash、不推定有效日期或答案。答案陣列複製，避免 caller 改陣列影響 Command；null／缺答案交給既有核心 Validator。
- 格式成功不代表可提交；版本／Task／作者／Guardian／Consent、Draft Revision 比較、Idempotency 與提交交易皆未執行。
- FormatException／ArgumentException 是此純 Mapper 的本機錯誤，不是新核定 HTTP code；將來錯誤轉譯由 11＋16 固定，不註冊例外 filter。

## 離線執行

```powershell
dotnet restore tests/ParentBridgeMapper/ParentBridgeMapperHarness.csproj --source tests/ParentBridgeMapper -p:NuGetAudit=false
dotnet run --project tests/ParentBridgeMapper/ParentBridgeMapperHarness.csproj --no-restore
```

需要本機 SDK／targeting pack 與既有 API 套件快取。參考 API assembly 只為使用真實 16 Command／DTO，不執行 Program；restore 僅本機來源。缺套件時停止，不下載。建置會產生忽略的 bin／obj。

測試直接編譯 Mapper＋固定 11 DTO 片段，驗證欄位、Revision、合法／缺少關係、陣列隔離與原 Principal；不建立 DbContext、transaction、Host 或 Socket。無交易參數的結構檢查只是輔助證據，不能單獨證明一般程式不會開交易；本 Mapper 的逐行原碼僅包含資料處理及純 codec，無任何交易呼叫。

本批沒有修改教師共用 DTO／Validator／Hash／Gateway；教師行為仍需後續正式整合回歸，不能僅由 DTO shape 測試宣稱已驗收。資格 stamp、精確鎖序、Hash bytes／舊資料、HTTP 錯誤轉譯仍待共同確認；本批不實作也不啟用它們。不 Commit／Push／連 DB／啟動 API。
