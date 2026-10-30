# V5.3／16 本機 HTTP 通訊驗收紀錄

## 一、驗收基準

- 日期：2026-10-30
- Branch：codex/v53-parent-core
- Git HEAD：bf8f1e8cb76c53f104cad6565791871fdb7980e5
- 環境：Windows PowerShell 5.1
- ASP.NET Core API：127.0.0.1:5186
- Node LocalHostProbe：127.0.0.1:5187
- 資料：僅使用合成測試設定，未使用真實病患資料

## 二、實際驗收結果

| 項目 | 結果 |
|---|---|
| ASP.NET Core API dotnet build --no-restore | PASS，1 個 NU1900 警告 |
| API /health/local | HTTP 200 PASS |
| Node → API /health/local | HTTP 200 PASS |
| API 未啟動時 Node 健康檢查 | HTTP 502 PASS |
| Node 拒絕 POST | HTTP 405 PASS |
| Node 拒絕未開放 API 路由 | HTTP 404 PASS |
| Node 拒絕 Authorization 標頭 | HTTP 400 PASS |
| Node 離線測試 | 43/43 PASS |
| Node 正常停止、PID 消失、5187 釋放 | PASS |
| API 正常停止、PID 消失、5186 釋放 | PASS |
| 主要環境變數還原 | PASS |
| 診斷日誌環境變數清理 | PASS |
| 本次私人測試 JSON 刪除 | PASS |

## 三、測試限制

- /health/local 回傳 databaseChecked=false，不代表資料庫可用。
- 未啟動隔離 MySQL，未執行 Migration 或資料寫入。
- 未驗收真實家長／教師問卷、登入、RBAC、Guardian／Consent。
- Node LocalHostProbe 僅提供本機健康檢查代理，不是正式業務前端。
- NU1900 表示 NuGet 弱點資料查核未完成。
- Git HEAD 是程式基準；LocalHostProbe 尚未提交，需另外審查與固定版本。

## 四、後續事項

1. 審查 LocalHostProbe 七個檔案並決定是否納入 Git。
2. 確認 07／11 的交接版本與共同資料模型。
3. 另行核准隔離 MySQL、Schema、Migration 及持久化測試。