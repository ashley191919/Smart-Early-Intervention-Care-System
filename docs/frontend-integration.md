最新共用版本與組員啟動方式請參考 [團隊啟動說明](team-frontend-start.md)。下方保留功能整合範圍與限制。

# 共用前端整合（2026-10-11）

目前分支：integration/frontend。既有 7、11、16 的頁面已置於同一 ASP.NET Core 專案，入口為 `/`，原 frontend-preview.html 轉向共用登入。

## 已接通的示範流程

同一瀏覽器、同一 origin（localhost:5180）中：

1. 醫療登入：單位 DEMO、帳號 demo、密碼 demo。
2. 儀表板 → 指派問卷，替 DEMO-001 王○安指派 SNAP-IV 或克氏，分家長／教師。重複指派不覆蓋任務與答案。
3. 家長手機 OTP 使用頁面顯示的示範碼，不寄送簡訊。家長首頁取得共用任務。
4. 家長問卷使用與教師相同的排版與驗證，保存草稿、重新開啟、完整填答後確認提交；提交後不能修改。
5. 家長建立教師示範授權；到教師入口輸入 DEMO- 開頭示範碼。只取得授權任務。
6. 第一份提交仍有效，全部授權任務提交後失效。撤銷阻止保存與提交，已提交答案保留；重新授權僅含未完成任務。
7. 家長只顯示教師是否完成；醫療端儀表板／個案詳細的表單紀錄可以查看共用原始答案。
8. 各角色由共用登入頁進入工作台；跨角色示範導覽已移除，家長、教師使用右上角選單登出。

草稿、任務、答案與授權僅保存到 `earlycare.frontend.v1` 的 localStorage。教師示範會話用 sessionStorage；它們不是可信身分、正式權限、MySQL 紀錄或正式知情同意。只用虛構資料。不同瀏覽器／裝置不共享；5177 與 5180 也不共享。

原教師 MySQL Development 路徑 mode=authorized、16 號 DB 草稿客戶端保留；DEMO- 碼走獨立前端 fixture 路徑。未修改後端、資料庫或 migration。

## 尚未整合

- 正式醫療 JWT 登入與 Organization Code，以及家長 OTP 服務。
- Case CRUD、家長綁定、個案 workflow 與 Dashboard 的正式共用資料；現有其他個案／提醒／統計仍為固定展示。
- 基本資料、滿意度、自編學校問卷尚未接入共用題目／填答；指派頁暫時停用這些選項。
- 正式多輪複評、更多個案、跨裝置共享、同意查核與 API 權限。
- 表單共用 API／MySQL 串接； schema 需變更时由 16 號統一產 migration。
- 16 號新提交 197dbf2 尚未額外整合；後續需比對並由三人驗收。

目前共用流程固定 DEMO-001、第 2 輪、demo-1 示範版本；指派日期只用來展示提醒，不讓教師授權到期。

## 驗證

- dotnet build：0 警告、0 錯誤。
- Node：3 項共用流程狀態測試 + 原 8 項教師草稿客戶端測試通過。
- 瀏覽器：醫療指派三份任務、家長 OTP／SNAP 提交、教師克氏提交、SNAP 草稿重載、撤銷阻擋、僅剩任務重新授權、教師全部提交、家長完成狀態、醫療結果查閱。

## 本機啟動

在此整合工作目錄執行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Initialize-LocalJwt.ps1
dotnet run --project EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --launch-profile http -- --environment Development
```

需保留既有本機 User Secrets（JWT 與 ConnectionString）；沒有執行 migration。
