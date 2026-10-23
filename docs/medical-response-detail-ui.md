# 醫療端問卷填答詳細頁

> 最新狀態：合成答案已移出 wwwroot，改由僅限 Development 且需有效醫療端 Session 的 Demo API 提供；使用既有登入 → 個案管理 → 查看 DEMO-001 → 表單紀錄流程，不新增 Demo 按鈕、不需 medicalDemo=1。請以 [medical-demo-security.md](medical-demo-security.md) 為目前架構與驗收依據。下方公開 fixture／舊 PreviewServer 內容為前幾輪實作紀錄，不是目前提供答案的方式。

## 本次介面

沿用醫療端 Sidebar、Header 與綠色主題，將個案「表單紀錄 → 查看原始答案」導向 `response-detail.html?taskId=...`，移除該入口原有的簡易答案彈窗。上、下方的返回連結指向 `case-detail.html#shared-tasks`，沿用原有程式選中表單紀錄頁籤。

頁面依序顯示問卷名稱／版本、遮罩個案、填答角色、提交時間與狀態；計分結果；完整逐題題目／題號／原始答案標籤；補充觀察；返回與匯出操作。長題目、答案和觀察文字保留換行，允許垂直捲動，不裁切問卷。

匯出按鈕目前 disabled，只預留入口，不產生檔案、不假稱下載成功。未修改問卷指派、填寫、提交、計分、Authentication、RBAC 或 DenyAll。

## 目前資料來源與計分界線

- 目前閱讀來源為 `SharedDemo.tasks()` 中已有的虛構已提交填答。`taskId` 只選取該筆紀錄，不補預設答案，也不自造正式 responseId。題目與答案標籤使用該筆 draft 保存的 questions／options 快照，不讀取新版本題目替換舊內容。
- 原始 answers 和 observation 只讀取並以 textContent 顯示；頁面沒有 save、assign、提交、匯入或改寫 localStorage。不存在、重複、缺失 taskId 或未提交紀錄均不顯示答案；storage 更新後若紀錄消失，清除頁面舊內容。
- SNAP-IV 與克氏在此前端資料流都沒有正式計分結果。程式檢查顯示 CoreDevelopmentSeed 的 SNAP-IV `ScoringDefinition = null`；目前 QuestionnaireReceipt／DraftResponse／CaseTaskResponse 不提供醫療端分數結果，QuestionnaireResponse 實體也未包含計分結果。未連接資料庫確認任何院所資料。
- 因此頁面對目前全部紀錄顯示「尚未計分」。不加總原始選項、不讀取 localStorage 中新增的 scoring／totalScore，不保留舊 HTML 的固定範例分數，也不提供臨界值、診斷或判讀結論。選項定義裡的 score 不代表已核准整份量表的計分／分量表規則。
- `buildScoreView` 是未來顯示已確認資料的 UI adapter 設計，不是現有 API DTO。它支援可選的 total、數量可變的 subscales、method／version；只呈現傳入數值，允許只提供分量表而不強制總分。現有 fixture adapter 一律傳 null。未提交資料不顯示分數；未來有正式不適用狀態時可顯示「不適用」。

未來須由表單主責／老師提供經確認的計分規則、分量表結構與版本，再由授權後端提供正式結果。醫療端答案讀取與匯出 API、資料範圍與 Permission 契約仍待整合。本頁並不完成正式結果讀取授權；既有前端 fixture 可見不代表可取得任何 MySQL 個案資料。家長與教師的既有隔離契約不因本次醫療 UI 變更而放寬。

## 驗收

```powershell
node --test tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:OutputPath=bin/ResponseUiVerification/
```

21 項 Node 測試通過（18 項詳細頁與 3 項既有共用前端回歸），包含 26／14 題、家長／教師分離、原始資料不變、未知／重複 taskId、尚未提交、缺失題目／答案標籤、無計分來源、忽略本機偽造分數、可選總分／分量表、文字安全顯示與返回連結。

標準 Release 輸出因現有執行中服務鎖住 exe 而失敗；改用以上獨立輸出目錄成功，0 警告、0 錯誤。未停止既有服務。

瀏覽器驗收以 `node tests/ResponseDetailPreviewServer.cjs` 啟動獨立 loopback 測試來源。它在該來源注入虛構填答供 UI 驗收，不啟動正式 API、不讀 secrets、不連 MySQL，其他 API 路徑維持拒絕。只在此隔離來源寫入合成 localStorage，不變更使用者原本系統來源的答案。它從現有 Development/SnapQuestionnaire.json 取 26 題與標籤；測試答案不是醫療資料且不帶分數。

已實測：

- 1440×900：document 1440×900，main 高 820、scrollHeight 6120；26 題完整，主要內容在 main 捲動。頁首基本資訊／計分空狀態清楚可讀，頁尾返回與匯出按鈕可完整捲入可視區。
- 390×844：保留側欄的既有窄螢幕導覽模式、單欄資訊與題目，沒有文件橫向溢出，匯出與返回按鈕邊界在可視區內。
- 返回後表單紀錄頁籤 aria-selected=true；再次點查看原始答案為獨立頁，沒有開啟 dialog。
- `fixture=unfinished` 不顯示正式答案與分數，`fixture=missing` 顯示找不到紀錄；臨時 viewport 已還原。

以上為隔離前端驗收，不是真實 MySQL、正式醫療帳號授權或臨床計分驗收。

## 恢復 DEMO-001 的唯讀展示結果（2026-10-23）

新增 `wwwroot/js/medical-demo-fixtures.js`。只有 URL 含唯一的 `medicalDemo=1` 才可選取合成結果；一般網址不新增任何結果。開啟：

- `https://localhost:7127/case-detail.html?medicalDemo=1#shared-tasks`
- 詳細頁：`https://localhost:7127/response-detail.html?taskId=medical-demo-snap-teacher-001-v1&caseId=DEMO-001&source=medical-demo&medicalDemo=1`
- 空個案：`https://localhost:7127/case-detail-empty.html?medicalDemo=1#shared-tasks`

使用者本次明確要求展示模式標示「Demo 模擬資料」，因此僅在此模式新增來源標示，不把合成紀錄當成正式提交或院方計分。DEMO-001 提供一份唯讀教師 SNAP-IV；DEMO-024 不提供合成任務。題目／選項快照與 Development/SnapQuestionnaire.json 一致，26 題答案沿用 ResponseDetailPreviewServer 的 `i % options.length` 合成模式，這是選擇展示選項，不是計分。填答者「示範教師」、日期、提交時間與補充觀察亦為虛構資料，無 responseId、正式任務建立或正式提交。

資料來源分離：

- `source=medical-demo` 必須同時有明確開關、正確 caseId 與固定 taskId。只讀深層 frozen fixture，不寫 localStorage、sessionStorage、Cookie、問卷 task、Response 或資料庫。
- `source=local-demo` 是既有 SharedDemo 紀錄；清單只篩選當前 caseId，將 fixture 作為額外顯示列，不取代任何原任務。相同 ID 也按來源區分。舊的僅帶 taskId 連結仍可選取唯一的 local-demo 紀錄；有提供 caseId 時必須吻合，重複／空參數拒絕。
- 缺少／重複 taskId、錯誤 caseId、未知來源（包含 `source=api`）、停用模式或查詢失敗，都顯示安全空狀態。不將服務失敗視為啟用 Demo 的條件，也不以 fixture 回補未知 local 任務。
- 此頁仍沒有正式醫療端 Response API adapter，沒有呼叫正式 API 或測試真實 401／403。失敗分支透過隔離替身驗證，另保留既有登入與前端權限測試。正式 API 對接時須另經後端授權，不得以 query、localStorage 或前端 caseId 判斷取代 RBAC／個案範圍防護。
- 沒有修改任何登入檢查、Authentication、RBAC、DenyAll、SessionVersion、Draft、Submit、問卷指派或計分後端。

詳細頁計分 adapter 仍一律使用 null，顯示「尚未計分」，不加總答案、不產生總分／分量表分數、診斷或評估結論。返回連結保留展示開關及 `#shared-tasks` 頁籤。

驗收使用 `StaffLoginEntryPreviewServer.cjs` 純靜態伺服器，**不使用**會注入 localStorage 的舊 ResponseDetailPreviewServer。所有 `/api/` 路徑固定拒絕，不啟動 API Program、不載入 secrets、不連 MySQL。瀏覽器實測 1440×900 與 390×844，完整 26 題、未計分、手機底部操作、返回頁籤、DEMO-024 空狀態及非展示模式皆通過。1440×900 文件無橫向溢出，詳細頁主內容高度 820、scrollHeight 6282；窄螢幕文件寬度不超過 viewport，按鈕可完整捲入可視區。臨時 viewport 已還原。

測試命令：

```powershell
node --test tests/MedicalDemoFixtures.test.cjs tests/ResponseDetailFrontend.test.cjs tests/SharedFrontendDemo.test.cjs tests/StaffAuthenticationFrontend.test.cjs tests/StaffPermissionPresentation.test.cjs tests/StaffLoginEntry.test.cjs tests/TeacherDraftClient.test.cjs
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -c Release -p:OutputPath=bin/ResponseUiVerification/
```

建置使用獨立 Release 輸出，避免覆寫執行中服務；不是資料庫或正式權限驗收。

本輪結果：120 項前端測試全部通過（原有 101 項及本輪新增 19 項），API Release Build 成功，0 警告、0 錯誤，`git diff --check` 通過。最初 loopback 測試受沙箱連線限制，經核准於沙箱外重跑後通過。驗收用靜態服務已停止，未停止原本的醫療端服務，未進行 Commit／Push／Merge。
