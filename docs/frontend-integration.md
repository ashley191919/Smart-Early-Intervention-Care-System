# 前端整合候選版（2026-10-11）

分支：integration/frontend；基底 origin/integration/phase1 @ b922a40。尚未合入 main。

| 座號 | 來源分支 | 本次納入提交 |
| --- | --- | --- |
| 11 | feature/teacher-authorization | 25cb35f |
| 7 | feature/authorization | f40a788c |
| 16 | feature/questionnaire-phase2-1 | 0608f09（待本人確認最新交付） |

## 整合方式

使用 Git merge 保留三人歷史及必要後端依賴，不將 16 的教師草稿前端單獨搬離 API。以 11 已確認的 hospital-logo.png 解決圖片衝突；教師頁同時保留共用 workspace-theme.css 與 16 的 teacher-draft.js、最新版教師 JS/CSS。沒有新增、套用或修改 migration，仍由 16 統整。

補上醫療登入與家長／教師入口互相連結，新增 frontend-preview.html 驗收入口。醫療登入保留 7 的既有示範驗證流程，另外提供明確標示的儀表板預覽，沒有將示範視為正式登入。

## 本機啟動與驗收

在此整合 checkout 的 EarlyInterventionCare.Api 專案啟動；另指定 5180 避免與原 5177 功能分支混淆：

```powershell
dotnet run --project EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --launch-profile http --urls http://localhost:5180
```

打開 http://localhost:5180/frontend-preview.html。原本 5177 不代表整合版。

驗收家長手機 → 示範碼 → 家長工作台；教師授權入口 → 教師預覽 → 填答／示範草稿；醫療登入畫面 → 儀表板預覽 → 個案列表／詳細／指派。以 1440×900 確認主要操作及無橫向溢出，另檢查窄螢幕。正式資料庫流程另驗收，未自動變更 MySQL。

## 合入 main 前

1. 16 確認 0608f09 或補推新版本，再 merge 新提交與重驗。
2. 三人共同核對頁面、登入導覽、問卷 UI 主責及剩餘示範。
3. 7 的問卷指派／結果畫面接 16 的 API；家長畫面正式接任務、綁定及授權仍是後續工作。
4. 確認既有 integration/phase1 的後端基底也獲團隊同意納入 main，不能將本分支宣稱只有前端修改。
5. 確認後再開 integration/frontend → main PR，review 後合併；目前不合 main。

原本 feature/teacher-authorization checkout 保留未追蹤部署檔，未納入此整合。

## 已執行檢查

- dotnet build：0 warning / 0 error。
- QuestionnairePhase21Harness：離線指派規則通過。
- QuestionnairePhase22Harness：76 項離線檢查通過。
- TeacherDraftClient.test.cjs：8 項通過；所有 wwwroot/js 語法檢查通過。
- 本地 HTML 靜態資源與連結檔案檢查通過。
- 瀏覽器：家長 OTP 示範至儀表板、醫療工作台至列表／個案詳細／指派頁、醫療入口返回家長登入、教師題目及本頁示範草稿均可操作；整合入口與已檢查工作台在 1440×900 無橫向溢出，整合入口 390×844 無橫向溢出。
- 未執行正式 DB 提交或 migration；此處離線檢查與畫面測試不取代正式資料庫驗收。
- 家長分享預覽連結使用目前 origin，5180 不會誤開原 5177；非 5177 不顯示不適用的靜態 QR。
