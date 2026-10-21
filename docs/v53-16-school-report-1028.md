# 10/28 學校報告：16 號技術進度摘要

## 報告定位

本系統目前為開發整合階段。共用家長問卷程式已實作，已有離線測試；真 Guardian／Consent Adapter、共同 Migration、真正 MySQL 驗收與正式部署尚未完成。
不能將合成資料、Fake Adapter、ADO 回傳結果或畫面展示說成正式醫療流程上線。

## 16 負責的共用架構

- 11 的專用 Controller 提供經驗證的 ClaimsPrincipal、TaskId 與答案 DTO。
- ParentQuestionnaireGateway 協調身分、scope、固定版本、作者、revision、去重與資料一致性。
- 11 的 IParentTaskAccessAdapter 在同一 DbContext／transaction 驗證 Guardian／Consent；缺少時預設拒絕。
- EfParentQuestionnaireStore 使用 EF Core／Pomelo 對應 MySQL；資料庫結構以 Migration 管理，不在啟動時自動建表。
- RBAC 使用原 PermissionService／Handler；醫療端還須通過 ICaseTaskReadScope。

## Draft → Submit → Response → Task

1. 確認唯一可信 userId，不接受前端 authorId。
2. 開啟交易，Adapter 鎖定與核對帳號、院所、Case、Guardian、Consent，再鎖 Task。
3. 草稿可缺題，expectedRevision 須符合；成功保存增加 revision，Task 進入 IN_PROGRESS。
4. 提交必須明確傳完整答案；Validator 核對固定版本與合法選項。
5. 同作者＋Idempotency-Key 防止重複提交；原 key／原 payload 重送取得同 responseId。
6. 同交易寫 Response、Task SUBMITTED 與提交時間；commit 前再驗證授權，commit 成功才回收據。
7. 提交後不能覆寫。Task／Response 不一致回 503，不假報成功或自動修復。

## 權限設計

Permission 決定「可做什麼操作」；資源 scope 決定「可操作哪個 Case／Task」。兩者必須同時通過。
同名 Policy 必須保留精確 PermissionRequirement 與登入要求，不能被後續註冊偷偷換成只需登入。
家長不能用知道 TaskId、前端 role／authorId 或 TeacherGrant 取得他人答案。真正跨院所／Consent 判斷仍須接入 11 Adapter。

## EF Core／MySQL

保留 Questionnaire／Version／Task／Draft／Response、Users／Roles／Permissions 與五份既有 Migration。
家長作者為 nullable ParentAuthorUserId，兩個 Restrict FK 及索引／去重唯一鍵仍是待審模型增量。
不回填虛構身分、不改舊教師答案、不以直接改 Snapshot 或 EnsureCreated 放行部署。

## 可報告的測試證據

本批實際數量以 `v53-16-verification.md` 最新補充為準：

- Gateway 使用 transactional Fake Store／Access Adapter：驗證作者、revision、去重、回滾與狀態不一致拒絕。
- EF／ADO 離線測試直接使用正式 Store：驗證 SQL 參數、交易順序、錯誤映射與 tracker；ADO 不具網路能力，沒有 MySQL 連線。
- RBAC Policy 安全測試：同名／錯誤 Permission／較晚註冊覆寫拒絕，Permission 與 scope 同時成立。
- 既有教師與 RBAC 回歸分別記錄；loopback HTTP 是合成身份／ADO，不是正式三端端到端。
- 預設 Model／Snapshot gate 仍應拒絕，直到正式增量完成；不將它計為全通過。

## 尚未完成

11 真 Guardian／Consent／作者 Adapter、核定家長問卷、正式專用 API／UI、共同 Migration、真 MySQL FK／鎖／並行／重啟持久化與備份還原。
07 負責醫療 API／前端接線與 Git 最終整合；16 本批未修改 Program 或醫療 UI。

## 地端測試環境

候選為 localhost API＋獨立 127.0.0.1:33326／earlycare_v53_synthetic；只用虛構資料。
本批不啟動此環境、不建庫、不改防火牆／Execution Policy、不開外網。
本輪目標分級：Level1 API 安全啟動；Level2 隔離 DB 寫讀／重啟／受控備份還原；Level3 私人 HTTPS 日後評估。這三項尚不能宣告已驗收。
