# 家長教師邀請入口第一版

2026/10/24，依使用者要求試做，沿用現有家長首頁，不恢復教師進度或同意書側欄。

## 操作

1. 開啟 `/parent-workspace.html`。首頁提供「邀請老師填寫」。
2. 未勾選教師參與時，先顯示確認選項入口，可回到同意視窗重新選擇。這只是展示選擇，不保存正式 Consent。
3. 沒有目前孩子／輪次的待填教師任務時，顯示請醫護確認指派，不自行指派問卷。
4. 既有醫療展示頁 `/form-assign.html` 指派教師 SNAP-IV 後，可回家長首頁取得授權碼、QR Code、複製連結與開啟教師入口。
5. 同一瀏覽器開啟連結後，授權碼自動帶入；按驗證才進入指定教師問卷。沒有新增教師進度或答案查詢。

## 實作界線

- 只在無正式 Token、非 account 模式的既有展示程式加入入口。正式家長 API 沒有改為使用瀏覽器授權。
- 重用 SharedDemo 的任務與 Grant；限制 DEMO-001／第 2 輪／教師任務，不加入其他孩子、輪次或家長任務；反覆開啟沿用匹配的有效碼。
- `/api/dev/presentation-invitation` 只在 Development／loopback Host 接受 `DEMO-XXXXXXXX` 格式，重用既有 QRCoder 產生 PNG；不建立或驗證 Grant、Session、Consent，也不查詢資料庫。不接受正式授權碼。回應 no-store。
- 連結的碼位於 fragment，教師入口讀取後移除 fragment；QR 在本機產生，沒有傳至外部 QR 服務。
- 授權與任務依舊是同 Origin 的瀏覽器 localStorage。換瀏覽器、換電腦或手機掃描 localhost，不能當成正式可用的跨裝置授權。
- 現有展示行為沿用「全部指定任務提交或撤銷後失效」；沒有宣稱新增正式期限、重開或同意撤回連動。正式期限待院方確認，正式 Grant／Consent 與共享儲存仍需整合。
- 不使用 Development EF 固定個案授權 API 代替正式家長入口，未連線 MySQL、未執行 Migration。

## 驗證

- API Build：0 warning／0 error。
- SharedFrontendDemo＋ParentV53Clients：28 項通過，新增跨孩子／輪次／角色授權範圍與重複開啟測試。
- 瀏覽器：未同意阻擋、回到同意選項、無任務不產碼、既有醫療展示指派、產碼／QR、複製授權碼、連結帶碼及教師驗碼成功。
- 1440×900、390×844、768×1024：對話框無水平溢出；手機可在視窗內垂直捲動。
- 圖片見 `evidence/teacher-share/desktop.png`、`mobile.png`。

依使用者指示保存為本機 Git Commit，尚未 Push；Commit 編號可由此文件的 Git 歷史查詢。
