# 第一階段：開發測試授權驗證與指定 SNAP-IV 任務

最新整合順序與完成狀態見 [進度藍圖](progress-roadmap.md)；本文件描述已完成的開發測試流程，不代表正式家長授權及 MySQL 儲存已完成。

## 操作

重新啟動更新後的 VS2022 專案，並使用 Development 環境。開啟與 Swagger 同一主機及連接埠的 `/?role=teacher`：

1. 展開頁尾「試做畫面預覽與測試授權」。
2. 按「產生測試授權碼並填入」，後端產生四組四碼的隨機測試碼並填入欄位。
3. 按「驗證授權碼」。有效碼建立開發會話，導向 `/teacher-workspace.html?mode=authorized`。
4. 工作台向後端查詢指定任務，顯示虛構幼兒「林○安」、個案 `DEV-SNAP-001` 及唯一指定的 SNAP-IV 26 題。克氏量表未指派，不會顯示。

授權碼大小寫皆可輸入，可省略分隔線，但未知碼不能通過。授權不限時，支援開發測試撤銷。服務重啟後，記憶體中的授權碼與會話全部失效，需重新產生。

直接開啟沒有 `mode=authorized` 的工作台仍是兩份量表的公開畫面預覽。這個入口不表示已驗證授權。直接進入 authorized 模式但沒有有效會話時，只顯示返回教師入口的提示，不顯示任務內容。

## 範圍與後續

本階段已完成「後端建立測試授權碼 → 驗證 → 會話 → 取得指定問卷」。不是正式家長同意或教師本人身分驗證。建立與撤銷接口只在 Development 啟用，且僅能操作固定虛構個案。

答案依舊只暫存於本頁記憶體，刷新後清除。authorized 模式停用正式提交，避免將示範完成當成保存成功；尚未消耗授權，也不提供 USED 狀態轉換。下一階段需實作共用資料表、草稿保存、正式提交與提交成功後的授權／任務狀態。

新 SNAP 任務與既有 `/teacher/test-form` 的兩題測試授權完全分開，不將 26 題映射成兩題。

## Development API

| 方法 | 路徑（前綴 `/api/dev/teacher-workspace`） | 用途 |
| --- | --- | --- |
| POST | `/grants` | 產生一筆虛構 SNAP 授權，回傳 grantId、authorizationCode、任務及問卷版本 |
| POST | `/verify` | JSON `{ "code": "產生的測試碼" }`；有效回傳工作台路徑並設定會話 Cookie |
| GET | `/task` | 依會話回傳指定幼兒、問卷版本、全部題目及選項 |
| POST | `/grants/{grantId}/revoke` | 撤銷指定開發授權；已有會話也立即無法讀取任務 |
| POST | `/logout` | 清除開發會話 Cookie 及伺服器會話 |

接口均禁止快取。所有新增 API 在 Production 回傳 404。驗證接口依來源 IP 每分鐘限 20 次，超出回傳 429。此為開發測試限流，正式部署仍需另行確認完整安全規格。

會話 Cookie 為 HttpOnly、SameSite=Strict，路徑限制於上述 API 前綴；HTTPS 使用 Secure。HTTP localhost 僅供本機開發。碼與會話值在伺服器以雜湊索引保存，不寫入瀏覽器持久儲存、網址、稽核事件或應用日誌。不同網址參數不能改變被指派的個案或問卷。

新事件：TeacherWorkspace.Create／Verify／Revoke，記錄於既有共用記憶體 audit 服務，ResourceType 為 TeacherWorkspaceGrant，不記錄授權碼或答案。

## 檔案

- `Development/TeacherWorkspaceService.cs`：限記憶體、執行緒安全的開發授權與會話服務。
- `Development/SnapQuestionnaire.json`：依來源 PDF 整理的 26 題固定開發問卷。
- `Controllers/DevelopmentTeacherWorkspaceController.cs`：上述接口。
- `wwwroot/js/teacher-access.js`：產碼、驗證、取消請求及導向。
- `wwwroot/js/teacher-workspace.js`：authorized 模式讀取指定任務，預覽模式保持原有兩份量表。
- `tests/TestTeacherWorkspace.py`：HTTP 驗證。

## 驗證

啟動 Development 與 Production 測試主機後執行：

```text
python tests/TestTeacherWorkspace.py --development-url http://127.0.0.1:5193 --production-url http://127.0.0.1:5194
```

涵蓋未驗證、空白／未知碼、任務內容與版本、偽造個案／問卷參數、Cookie 旗標、不快取、撤銷、重複撤銷、登出，以及 Production 拒絕所有新增接口。不列印授權碼或會話值。

瀏覽器另外實測錯誤碼拒絕、產生碼、驗證、開啟指定問卷。1440 × 900 首頁主內容高度與捲動高度均為 820px；只有一份指定 SNAP-IV，26 題／104 個選項，正式提交停用；表單在主內容區內捲動。既有 `TestTeacherAccess.ps1` 回歸檢查亦通過。
