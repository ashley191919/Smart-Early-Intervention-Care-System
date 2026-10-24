# 10/28 展示版本保存與驗證紀錄

日期：2026/10/24。基準 HEAD：`0e4e16939b2fb711190aa490ca80098f0615ff47`。本文件與實作保存於同一個本機 Commit；實際 Commit 請以 `git log -1` 為準。未更新即時遠端，也未 Push。

## 保存範圍

- 保留先前累積的 V5.3 ParentModule、Consent、家長 Gateway 契約、前端簡化與三角色行動版頁首。
- 保留 Case／Guardian 與教師最小契約、模型交接、既有盤點及驗收文件。
- 沿用現有家長工作台，沒有新增另一套展示工作台。
- 彩排修正：登出一併清除 UI 閱讀狀態；未確認前不渲染展示孩子資料；更新相關 script 版本，避免舊快取與新方法不一致。
- 沒有修改共用 Program.cs、ApplicationDbContext、正式 Migration 或 Snapshot。

修改前來源備份：`C:\Users\ashle\AppData\Local\Temp\earlycare-1028-freeze-before-20261024-212324`，包含 52 份檔案、SHA256 清單與 tracked.patch。備份不納入 Repository。

## 本次實際執行的驗證

| 驗證 | 結果 | 範圍 |
|---|---|---|
| API build | 0 warning、0 error | 隔離輸出目錄 |
| ParentV53Harness | 60 通過 | SQLite Consent／Guardian、簽署 JWT HTTP；答案 Fake Gateway |
| CaseGuardianHarness | 209 通過，內含 71 項 Definition | SQLite／隔離測試；不可額外重複加計 71 |
| ParentEmailHarness | 28＋32＝60 通過 | 離線寄信與 OTP；沒有 SMTP、寄信、資料庫或 Secrets 存取 |
| Node 五組測試 | 63 通過 | SharedFrontendDemo、ParentAccountClient、ParentV53Clients、TeacherDraftClient、ParentOtpClient |
| 實際瀏覽器彩排 | 通過 | 同意阻擋、教師不勾仍可進入、關係選擇、草稿、接續、漏題、26 題提交、完成後重新載入、單份重新試填 |

390×844、768×1024 已操作驗證並留截圖；1440×900 已恢復桌面布局。手機與平板均查核無根頁面水平溢出。彩排使用虛構資料，提交結果是瀏覽器展示保存，不是伺服器 Response。

## 尚未被此版本證明的部分

正式 Email 登入端到端、核准版同意本文、真實共用答案 Gateway、正式作者隔離與交易鎖、最新模型 Migration／真實 MySQL、正式教師授權及重開均不能宣稱完成。

Consent Runtime 預設關閉、正式問卷 Gateway 預設不可用；SQLite／Fake 通過不能取代正式驗收。未連線或修改共用資料庫、未部署。

操作入口、重試方法與會前檢查見 [展示操作清單](1028-parent-demo-checklist.md)。交接契約見 [Case／Guardian](v53-11-case-guardian-api.md)、[Consent／Gateway](v53-11-consent-gateway-handoff.md)、[模型／教師界線](v53-11-integration-model-teacher.md)。
