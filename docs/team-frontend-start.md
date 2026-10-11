# 團隊前端下載與啟動（2026-10-11）

共用儲存庫：https://github.com/ashley191919/Smart-Early-Intervention-Care-System

本次整合以 main 為共用驗收版本；integration/frontend 保留整合來源。Frontend-Integration 是同一 Git 儲存庫的另一個工作目錄，不是另一個專案或另一個遠端儲存庫。

## 已有專案的組員

先保存自己的修改並推送自己的功能分支。不要在有未保存修改的狀態直接切換 main。

```powershell
git fetch origin
git switch main
git pull --ff-only origin main
```

如 pull 顯示不能 fast-forward，先停止並確認自己的 main 是否另有提交；不要強制覆蓋。

## 尚未下載

```powershell
git clone https://github.com/ashley191919/Smart-Early-Intervention-Care-System.git
cd Smart-Early-Intervention-Care-System
```

## 第一次啟動

在儲存庫根目錄的 PowerShell 執行（需安裝 .NET 8 SDK）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Initialize-LocalJwt.ps1
dotnet run --project .\EarlyInterventionCare.Api\EarlyInterventionCare.Api.csproj --launch-profile http -- --environment Development
```

JWT 初始化僅補上此 Windows 使用者缺少的本機設定，不覆寫既有金鑰，不上傳秘密。若 5177 已由另一份專案使用，先停止自己先前啟動的網站，再啟動本份。

出現 Now listening on: http://localhost:5177 後開啟該網址。三種身分入口都有「前往工作台」按鈕可檢視現有前端。家長驗證流程目前使用頁面產生的驗證碼；醫療前端登入資料為 DEMO / demo / demo。這些前端流程不等同正式 OTP 或 JWT 身分驗證。

VS2022：選「檔案 → 開啟 → 專案/方案」，開啟 EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj，選 http，按 Ctrl+F5。首次仍需在自己的 Windows 使用者執行上述 JWT 初始化。

## 之後啟動

開啟同一份專案，選 http，按 Ctrl+F5；保持網站程序執行。網址 localhost 指的是各自電腦，不能把這個網址當成其他組員可連線的網站。

## 資料庫與版本範圍

Git 分享程式碼，不分享 MySQL 資料、User Secrets 或本機瀏覽器答案。每個人各自設定 ConnectionString；需要後端驗收時再依 16 號提供的 migration 流程套用，網站啟動不自動執行 migration。本次沒有建立新 migration 或套用資料庫變更。

目前包含三端前端、工作台登出、共用問卷前端流程，以及已整合的既有後端。正式醫療登入、家長 OTP、跨装置資料、API 權限與部分 MySQL 串接仍須依模組繼續驗收，前端文字呈現不代表這些功能已完成。

16 號提交 197dbf2 已合入，新增 GET /api/cases/{caseId}/tasks/completion 必填問卷完成摘要。此查詢沿用既有身分與個案限制、統計所有輪次，不改變個案狀態或存取逐題答案。未新增 migration；驗證完成摘要、Phase 2-2 的 76 項離線檢查與 11 項前端測試通過。資料庫及真實 JWT HTTP 驗收需另外執行。

## 後续開發

從更新後的 main 開自己的功能分支再修改、提交、推送。資料表變動仍由 16 號統整 migration；一般前端修改不需交由 16 號代為推送。
