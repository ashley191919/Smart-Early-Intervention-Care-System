# 醫療登入頁前後一致性檢查

日期：2026-10-20。

原因：初次醫療入口為 `/?role=medical`，登出後的 staff-header 卻開啟 `staff-login.html`。後者載入另一組 HTML／CSS，沒有共用頁的提示，且 secondary-button 缺少邊框定義，導致字體、間距、FAQ 與工作台按鈕不一致。

修正：

- 醫療登出導頁統一為 `/?role=medical`，舊 `staff-login.html` 仍可使用，但只轉至同一入口；JavaScript 與 meta refresh 均提供轉址，另有普通連結作備援。
- 共用頁保留「請輸入醫療人員登入資料。」並新增「忘記密碼？」至既有 staff-forgot-password.html；忘記密碼的返回登入也會回到共用入口。
- 帳號欄位與提示統一為「醫療人員帳號」，不再混用「醫護人員帳號」。院徽、身分頁籤、系統名稱、FAQ、字體、欄位與按鈕間距使用同一 HTML／CSS。
- 僅醫療登入面板明確指定工作台入口的綠色邊框、圓角、字重、內距及 hover；不更動家長或教師入口樣式。
- 醫療各頁更新共用 staff-header.js 版本號，避免登出仍執行快取舊腳本。

驗收使用隔離靜態 origin 127.0.0.1:5184，沒有真實登入、寄信或資料庫操作：

- 1440×900 實際按「前往醫療人員工作台 → 使用者選單 → 登出」。返回 URL 正確。
- 比較登出前後的三個 label、提示、忘記密碼、入口 border／border-radius／font／padding 及三個 input 高度，結果完全一致；欄位高度約 46px。
- 直接開啟舊 staff-login.html 也進入共用醫療面板。
- 點忘記密碼並按返回登入，回到同一頁，未輸入任何帳號或密碼。
- 390×844，文件寬 375px，入口有 solid 邊框且左右界線在 viewport 內。
- 原有 41 項前端測試通過，staff-header.js 語法檢查與 git diff --check 通過。

這是前端入口統一；未改 Authentication API、角色權限、密碼重設服務、JWT／Cookie 撤銷或共用資料庫。原醫療頁的登出目前仍是前端導頁，正式 Session 撤銷由醫療端後續整合。
