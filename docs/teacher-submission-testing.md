# 第一階段 SNAP-IV 提交整合驗收

日期：2026-10-06。①答案驗證與保存、②提交後授權失效、③重複／失敗／撤銷衝突處理已完成實作與獨立測試，使用者已確認本節點無問題並要求保存推送。範圍為 Development、虛構個案及單一指定 SNAP-IV；尚未整合正式家長身分、綁定與同意。

## 啟動前

VS2022 按 Shift+F5 停止舊服務；其他 dotnet run 視窗按 Ctrl+C。於 VS2022 的開發人員 PowerShell 或 Windows PowerShell 執行：

```powershell
dotnet run --project "C:\Users\ashle\Desktop\輔仁大學\早療專題\vs 2022\Smart-Early-Intervention-Care-System\tests\CoreDatabaseHarness.csproj" -- --apply
```

預期 `applied migrations=3, pending migrations=0`。第三份 AddSubmissionReceiptWindow 只在 teacher_sessions 增加可空的 receipt_expires_at_utc，原表與答案保留，不重新建庫／清空資料／重跑 seed。

重新啟動 Development 網站，在教師入口按 Ctrl+F5。以下網址改成實際服務網址，並全程使用同一主機與連接埠。

## 一次驗收清單

### A. 缺題不能提交

1. 產生測試授權碼，將原碼暫記於自己的測試筆記。
2. 驗證、開始填寫 SNAP-IV，填入虛構老師姓名及今天或較早的有效日期。
3. 留一題未填，按「檢查並送出」：應提示缺題，不能進入確認提交，不顯示完成。

### B. 完整提交保存

1. 填完 26 題，按「檢查並送出」，再按「確認送出」。
2. 應看到「本次指定問卷已提交」、保存成功說明、提交時間及 responseId 收據，授權 USED。
3. 在 Workbench 執行以下唯讀查詢，找到本次虛構老師與時間；記下 task_id 和 submitted_by_grant_id。

```sql
USE earlycare_dev;
SELECT response_id, task_id, questionnaire_version_id,
       respondent_name, filled_on, submitted_by_grant_id,
       JSON_LENGTH(answers_json) AS answer_count,
       DATE_ADD(submitted_at_utc, INTERVAL 8 HOUR) AS submitted_at_taiwan
FROM questionnaire_responses
ORDER BY submitted_at_utc DESC
LIMIT 10;
```

本次應為 26 個答案。核對狀態（兩處 ID 換成本次 grantId）：

```sql
SELECT g.grant_id, g.grant_status, g.used_at_utc,
       t.task_id, t.task_status, t.submitted_at_utc
FROM teacher_grants g
JOIN teacher_grant_tasks l ON l.grant_id = g.grant_id
JOIN case_questionnaires t ON t.task_id = l.task_id
WHERE g.grant_id = '貼上本次 grantId';

SELECT r.task_id, COUNT(*) AS response_count
FROM questionnaire_responses r
WHERE r.submitted_by_grant_id = '貼上本次 grantId'
GROUP BY r.task_id;
```

預期 grant_status=USED、task_status=SUBMITTED、response_count=1。需要看原始答案時，在 Workbench 對 questionnaire_responses 按「Select Rows」，查看 answers_json；沒有提供教師任意查閱答案的 API。

### C. 完成後仍失效、重啟仍保存

按「登出並返回教師入口」，輸入剛才原碼：應被拒絕。停止並重啟網站，再驗同一原碼仍被拒絕；Workbench 重跑相同 ID 的查詢，答案仍一份，狀態仍 USED／SUBMITTED。

### D. 撤銷後不能提交

1. 再次「產生測試授權碼」：上一任務已提交時，開發入口會建立新的虛構測試任務與新輪次，不修改或刪除舊答案。這只是可重複測試 fixture，不是正式醫護複評功能。
2. 新碼驗證後進入表單，填完但先不按確認送出。
3. Workbench 從最新 ACTIVE 授權取得新 grantId；PowerShell 執行：

```powershell
$teacherSubmitBase = 'http://127.0.0.1:5193'
$teacherSubmitGrantId = '貼上新 grantId'
Invoke-RestMethod -Method Post -Uri "$teacherSubmitBase/api/dev/teacher-workspace/grants/$teacherSubmitGrantId/revoke"
```

4. 回到原表單按確認送出：應拒絕並提示授權不可用，不顯示保存成功。
5. 依新 grantId 查詢 response_count，應沒有本次答案，授權仍 REVOKED。

以上四項回報通過，即可保存本節點。並行／故障測試已在獨立資料庫自動完成，不需在你的資料庫建立故障 trigger 或刪除資料。

## 防止重複與失敗規則

- 前端提交帶 UUID Idempotency-Key；同一請求重試保留原 key 與內容。相同內容（含答案順序正規化）回同一 responseId，不能新增第二份。
- 同 key 不同內容回 409 IDEMPOTENCY_CONFLICT；USED 後新 key 無法建立答案。資料庫另有 task 唯一鍵及 grant+key 唯一鍵。
- 提交依 grant → 排序 tasks 加資料列鎖，同一交易保存 response、SUBMITTED、USED 及會話失效；撤銷鎖同一 grant。撤銷先完成則提交拒絕；完整提交先完成則撤銷回 409。
- 保存失敗 rollback，答案不新增，task 與 grant 不轉完成。網路中斷可能發生於伺服器已保存之後，因此保留原請求，提示「重試本次提交」，只在確認回應成功後顯示完成。
- 僅成功提交的那個會話可在最多 15 分鐘內重試完全相同請求取得簡短收據；不允許讀任務、讀答案或新建提交。到期、登出、原會話到期即關閉收據通道。這段時間不代表授權仍 ACTIVE。
- 回應只有 responseId、taskId、questionnaireVersionId、submittedAtUtc、grantStatus、replayed，不回原始答案。
- 僅 Development 開放，提交要求同來源及自訂請求標頭；Production 回 404。沒有答案更新／刪除接口，沒有家長答案讀取接口。

## 已完成的驗證

- 建置 0 警告／0 錯誤；第三份 migration 在獨立 port 33318 套用，未操作使用者 port 3306。
- 缺題、非法值、重複題、未知題、空姓名、未來日期、版本不符、未授權任務及跨來源拒絕。
- 用獨立資料庫 trigger 模擬 response 寫入失敗，確認答案／任務／授權一起 rollback，隨後移除測試 trigger。
- 六筆同時同 key 提交只有一份答案，原收據重啟後仍可重取；不同內容／新 key 拒絕，登出與收據到期後拒絕。
- 六組提交／撤銷競爭僅出現 USED+SUBMITTED+1份，或 REVOKED+PENDING+0份。
- 真實瀏覽器完成缺題提示、26 題提交、收據與登出；1440×900 確認視窗與收據均在可視區、無橫向溢出。

草稿仍只暫存本頁；多問卷、正式家長授權與醫護權限另行整合。本節點不宣告整組第一階段正式完成。
