# 教師授權 MySQL 本機驗收（2026-10-06）

本次沿用已套用的九表 migration，不需要重新建表、刪表或重設密碼。教師授權與會話已保存；草稿、正式提交、正式家長同意及身份尚未完成。測試只使用虛構 seed，不能輸入真實幼兒或教師資料。

## 1. 重新啟動

停止 VS2022 舊服務，重新建置並啟動 EarlyInterventionCare.Api（Development）。開啟與 Swagger 相同主機及連接埠的 `/?role=teacher`；例如使用預設 http profile 時為 `http://localhost:5177/?role=teacher`。以實際啟動網址為準，不要混用 localhost 與 127.0.0.1 或不同連接埠。

## 2. 建立、驗證、查詢

展開「試做畫面預覽與測試授權」，按「產生測試授權碼並填入」。先將此碼暫記於自己的測試筆記，再按「驗證授權碼」（驗證後欄位會清空）。工作台應顯示 CASE-DEMO-001、虛構幼兒、未提供性別與 SNAP-IV 26 題，只有這份指定問卷。

MySQL Workbench 可用以下唯讀查詢核對，沒有授權碼或會話明碼：

```sql
USE earlycare_dev;
SELECT grant_id, grant_status, is_development, created_at_utc, revoked_at_utc
FROM teacher_grants ORDER BY created_at_utc DESC;
SELECT grant_id, task_id FROM teacher_grant_tasks;
SELECT grant_id, session_status, session_expires_at_utc FROM teacher_sessions;
```

授權與會話表只保存 SHA-256 雜湊；沒有明碼查閱 API。

## 3. 網站重啟

停止並重新啟動同一網站，再進入教師入口，直接輸入步驟 2 的原碼（不要再按產生新碼）。應能取得同一任務。會話未超過 8 小時時，既有工作台重新整理也應能載入。這不表示本頁草稿已保存；答案仍會在重新整理時清除。

## 4. 撤銷與重啟

從 Workbench 第一個查詢複製這次的 grant_id。在 PowerShell 使用同一網站網址，替換以下兩個值：

```powershell
$teacherTestBase = 'http://localhost:5177'
$teacherTestGrantId = '貼上本次 grant_id'
Invoke-RestMethod -Method Post -Uri "$teacherTestBase/api/dev/teacher-workspace/grants/$teacherTestGrantId/revoke"
```

應回傳 REVOKED。原工作台重新整理應顯示無法取得任務；原授權碼也不能驗證。重啟網站後再次輸入原碼，仍須被拒絕。重複撤銷同一 REVOKED 授權可成功；USED 不會改成 REVOKED。

## 5. 新碼、登出與錯誤碼

再次產生新碼可重新取得未提交任務。每次產生都會撤銷該任務舊的 ACTIVE 開發授權及其全部會話；舊碼失效是此按鈕的設計，不是重啟造成。只看問卷、驗證或登出不消耗授權。

輸入不存在的授權碼須被拒絕；未登入直接開啟 `teacher-workspace.html?mode=authorized` 不得取得任務。工作台「返回教師登入」會登出目前會話，原碼仍可重驗；重驗同一碼會使舊會話失效。

8 小時為目前開發會話的固定有效期間，並非授權期限。正式提交完成後 USED 的完整流程待下一階段。

## 已完成驗證與限制

- API build：0 警告、0 錯誤；九表模型驗證通過。
- 獨立本機 MySQL 8.0.46（33318，非使用者 3306）：HTTP 指定任務／UUID／26 題／4 選項、錯誤碼、匿名、Cookie／no-store、撤銷、登出；Production 全部開發路由 404。
- 實際停止再啟動測試網站：有效授權及會話保存、重驗替換會話、撤銷保存、到期後重驗、同時建立僅保留一筆 ACTIVE 授權。
- USED 撤銷拒絕以獨立 DB 模擬終止狀態驗證，未宣稱正式提交已完成。
- 瀏覽器 1440 × 900：首頁與表單無水平溢出，表單 26 題、長內容由主內容區捲動。
- 使用者本機網站驗收待上述操作完成；本次不修改使用者資料庫 schema、不自動 seed、不提交或推送 Git。

`tests/TestTeacherWorkspace.py` 可對本機 Development 網站執行 HTTP 驗收；會建立／撤銷虛構測試授權。`tests/TestTeacherWorkspacePersistence.py` 僅供已建立的獨立 33318 測試實例，不應拿來指向個人資料庫；該腳本不建立或清除資料庫。

2026-10-06 本機驗收回報：使用者確認建立、驗證、Workbench 查詢、重啟保存、撤銷後重啟拒絕、新碼、登出及錯誤碼測試成功。MySQL 教師授權節點可保存；下一步為邀請連結與 QR Code。
