# 11 固定版本交接索引

整理日期：2026/10/30。分支：`feature/case-guardian-management`。

此文件與近期修正保存於同一個本機 Commit，完整固定版本請以 `git log -1` 為準。本次僅 Commit，不 Push、Fetch、Merge 或執行 Migration；不能宣稱組員已能由遠端取得。遠端狀態未即時查證。

## 已包含成果

- `de9bf40`：Guardian／Consent／家長專用 Controller 與 Gateway 契約、模組 DI、三角色行動版，以及隔離測試與交接文件。
- `8f8b16c`：家長首頁教師邀請分享展示、授權碼／QR、本機 QR rendering endpoint。
- 本次：教師克氏展示任務可取消、保留原紀錄與草稿、調整 Grant 任務範圍、取消後重新指派建立新任務、教師「本次問卷」導覽修正及快取版本更新。
- 克氏教師指派選項依使用者最新指示保持可選；不以此決定最終院方問卷適用角色政策。未整合題目的其他選項仍停用。

## 統整者先看

| 範圍 | 交接文件／程式 |
|---|---|
| Case／Guardian DTO、路由、Revision | [Case API](v53-11-case-guardian-api.md) |
| Consent 有效條件、Gateway DTO／錯誤／交易契約 | [Consent／Gateway](v53-11-consent-gateway-handoff.md)；`EarlyInterventionCare.Api/ParentModule/` |
| Entity、Configuration、Schema 差異與教師責任界線 | [模型／教師](v53-11-integration-model-teacher.md) |
| 既有隔離驗收範圍 | [V5.3 驗證](v53-11-validation.md)、[10/24 保存驗證](1028-parent-freeze-validation.md) |
| 本次前端行為 | [取消與導覽修正](teacher-clancy-cancellation-20261025.md) |
| 教師邀請展示限制 | [邀請第一版](parent-teacher-invitation-prototype-20261024.md) |

## 尚未完成／整合前必須確認

1. Guardian、Email、Consent Entity 已有程式與 Mapping，不等於共同 Schema 已定稿或真實 MySQL 已套用。
2. Consent 預設關閉。正式核准本文、文件適用範圍 ACCOUNT／CASE 及政策仍待確認；展示視窗接受狀態不能當正式 Consent。
3. 正式家長 Draft／Submit／Receipt Controller 委派 `IParentQuestionnaireGateway`，預設 adapter 回 503；Fake Gateway 僅在隔離測試使用。
4. `IParentQuestionnaireAccess.RequireInTransactionAsync` 要求同一個 scoped ApplicationDbContext 且存在交易；Guardian／Consent 重新驗證契約已提供，但尚未與 16 的 `bb74e72c1ed00f601646ba669c8ea73952359e98` 實際對接或驗證鎖定順序。本機尚無該 Git object，本次沒有取得遠端。
5. Definition 使用 Task 綁定版本與既有 Guardian Adapter；Task／Assignment DTO、RBAC／資源 Scope、Program／DbContext 的組合仍須三方固定版本後確認。
6. 共用 Migration／Snapshot 本次沒有修改；整合需保留 History Migration，由統整者比較模型及產生後續 Migration。
7. 教師邀請碼、取消與頁面展示保存仍是同 Origin 的 localStorage，不能作為正式跨裝置授權、交易或資料庫保存證據。

## 本次檢查

- 五組 Node 測試：SharedFrontendDemo、ParentAccountClient、ParentV53Clients、TeacherDraftClient、ParentOtpClient，66 項通過。
- 修改 JavaScript 語法及 Git whitespace 檢查通過。
- 本輪沒有改 C#；沒有重新執行 SMTP、資料庫、Migration 或共用引擎整合驗收。先前 C#／瀏覽器證據請見上列文件，不視為本輪重新通過。

待使用者指示 Push 後，再提供此 Commit 的完整 Hash 供組員確認取得；本輪不進行整體合併。
