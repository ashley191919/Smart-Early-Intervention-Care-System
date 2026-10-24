# 三端手機／平板導覽一致化

2026-10-24，依使用者確認的新版識別排列。

## 實作

- 新增共用 role-mobile-header.js／role-mobile-header.css。
- 800px 以下：左側院徽＋「天主教輔仁大學」「附設醫院」兩個不可拆開的文字單位及英文 FU JEN CATHOLIC UNIVERSITY HOSPITAL；右側僅選單按鈕。
- 系統識別與原導覽移入展開選單，下方為角色身分與登出。親師登出沿用 workspace-user-menu 原 handler，醫療沿用 staff-header 原 handler；不自行建立 Session／清除帳號或放寬權限。
- 醫療端 header-actions 移至手機導覽列的選單左側，保留原提醒清單與事件。桌面將原節點放回原位置，手機標頭隱藏。
- 移除 parent-mobile.js 原先只供家長的頂部組裝，保留其進度、欄位文案與漏填提示。
- 教師手機欄位／選項單欄，平板選項兩欄，維持可點選區與文字大小。
- 套用家長工作台／問卷、教師工作台，以及既有九個 data-staff-header 醫療頁面。登入與密碼頁未額外改版。

## 驗收

- 家長、教師、醫療工作台於 390×844：只有一條約 84px 導覽列；院名與英文可見；無水平溢出；展開後角色導覽、系統識別及登出可見。
- 768×1024：家長、醫療及教師填答檢查，無水平溢出；教師選項兩欄、高度約 54px。
- 1440×900：教師原 brand／醫院識別及醫療原 brand／main-nav／header-actions 回復原位置，手機標頭高度為 0。
- 醫療清單、新增、詳細、空詳細、編輯、指派、邀請完成及答案頁於 390×844 均有標頭、無水平溢出。
- 醫療提醒仍可展開並讀取原清單；未移除或另建提醒資料。
- 本輪為 CSS viewport 與展示介面操作，不宣稱正式登入、MySQL 或實體行動裝置驗收。
- 63 項既有 Node 回歸通過，新增／修改 JS 語法及 git diff --check 通過。

## 保護

改動前逐檔備份於 C:/Users/ashle/AppData/Local/Temp/earlycare-three-role-header-before-20261024-205310。
未修改醫療業務 Service／Controller、共用資料模型、Migration 或資料庫；未 Commit／Push。

## 待辦提醒位置補正

依使用者要求，醫療手機／平板鈴鐺按鈕位於選單左側；點擊直接展開原提醒清單，不需先開選單。390×844、768×1024 確認按鈕順序、無水平溢出，清單在可視範圍；1440×900 恢復原 topbar。未變更提醒資料或事件處理邏輯。改動前備份 C:/Users/ashle/AppData/Local/Temp/earlycare-reminder-position-before-20261024-210016。
