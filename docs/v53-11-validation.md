# V5.3：11 實作與驗收交接

日期：2026-10-22。工作基準：feature/case-guardian-management／0e4e169 加本輪未提交內容。
依《專題開會紀錄檔-分工_1020》及本輪使用者指示；沒有重新分工、遠端更新或正式部署。

## A. 修改檔案

本輪新增：

- ParentModule/ConsentModels.cs、ConsentService.cs、ParentControllers.cs、ParentQuestionnaireContracts.cs、ParentModuleRegistration.cs（均位於 EarlyInterventionCare.Api）。
- wwwroot/js/parent-consent-client.js、parent-questionnaire-client.js、parent-questionnaire-ui.js。
- wwwroot/css/parent-questionnaire-account.css。
- tests/ParentV53/Program.cs、FakeParentGateway.cs、ParentGatewayContractSuite.cs、ParentV53Harness.csproj。
- tests/ParentV53Clients.test.cjs。
- docs/v53-11-case-guardian-api.md、v53-11-consent-gateway-handoff.md、v53-11-integration-model-teacher.md、本文件。

本輪增量修改：

- Services/Questionnaires/GuardianQuestionnaireDefinitionAccess.cs：原有註冊點呼叫模組 Extension。
- wwwroot/js/parent-account-workspace.js、wwwroot/parent-workspace.html：前置伺服器同意、正式填答 UI 與刷新。
- 原未追蹤的 wwwroot/js/parent-entry-consent.js、wwwroot/css/parent-entry-consent.css：保留原展示處理，新增正式 account 伺服器流程；正式流程不採用展示接受狀態。
- tests/CaseGuardianHarness.cs：既有 SQLite/JWT fixture 明確加入合成同意前提，未停用新 gate；未同意拒絕由新 Harness 獨立驗證。

## B. 工作包結果

| 包 | 已完成的安全獨立成果 | 尚未完成 |
| --- | --- | --- |
| A | 唯一 Case CRUD 保留；固定路由、DTO 範例、Revision、錯誤與 Guardian 界線 | 三方整合及醫療資源授權驗收 |
| B | Consent Entity／Configuration／Service／Controller、可信 actor、文件內容雜湊、三態教師選項、有效紀錄查詢、舊家長路由 gate | 院方本文／範圍核定、正式 Migration、跨交易撤銷一致性 |
| C | 正式家長 Draft／Submit／Receipt Controller、固定 Gateway、關係選項、Client 衝突與重試、伺服器完成狀態刷新、手機平板操作 | 16 真 Gateway、作者模型、MySQL 交易／持久化驗收 |
| D | 冪等模組 DI、獨立 Configuration、欄位／FK／索引／舊資料交接 | 16 統整 Snapshot／Migration、07 最終主程式整合 |
| E | 既有教師成果保留、業務／引擎界線、八項期限與重開候選案例 | 未把候選政策實作成正式授權；正式教師擴充不列本輪成果 |

## C. 真實程式與 Fake 的界線

- 產品程式包含實際 Consent DB Service、JWT Controller、Actor／Guardian／院所檢查、前置 gate、前端 Client 與 UI。
- 正式 Consent 預設關閉；沒有核定文件、組態或適當 Schema 時拒絕，不新增假的核准本文。
- 正式 DI 預設是 UnavailableParentQuestionnaireGateway，回 503；不保存答案、不更新正式 Task、不假裝提交成功。
- FakeParentGateway 僅在測試專案，記憶體保存答案，隔離 SQLite 更新 fixture Task 狀態供 UI 驗證；沒有寫正式 QuestionnaireResponse。
- Consent 測試是真 SQLite 儲存；不是正式 MySQL。SQLite 記憶體測試不證明重啟持久化或 MySQL 鎖定。
- 瀏覽器使用隔離 127.0.0.1:5185、合成文件／虛構孩子／測試 JWT，沒有使用 Gmail、正式登入帳號、真病患、User Secrets 或共享 DB。
- 原 5177 網站沒有被重新啟動；不能把目前使用者既有程序當作已載入本輪後端。

## D. 本輪實際驗證

| 驗證 | 結果 | 範圍 |
| --- | --- | --- |
| API Build | 0 warning／0 error | 隔離輸出目錄，不鎖住現有網站 |
| ParentV53 Harness | 60 項通過 | 真 SQLite Consent／Guardian、簽章 JWT HTTP、Fake Gateway 共用契約、未同意／撤銷／角色偽造／衝突／重試 |
| CaseGuardian Harness | 209 項通過（含 71 Definition） | 原有範圍、綁定、Email、讀題回歸；加入合成同意前提 |
| ParentEmail Harness | 28＋32 項通過 | 不連 SMTP、不寄信、不讀 Secrets／DB |
| Node 前端測試 | 61 項通過（20 新增＋41 既有） | 同意狀態、無本機 fallback、Revision、去重、斷線、409／503、錯誤回執等 |
| JS 語法 | 5 檔通過 | account、同意與問卷新增／增量 JS |
| git diff --check | 通過 | 沒有空白格式錯誤；Git 提示既有 LF／CRLF 正規化，不影響結果 |

安全命令（從 Repository 根目錄執行）：

```powershell
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-v53-build/
dotnet run --project tests/ParentV53/ParentV53Harness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-v53-harness/
dotnet run --project tests/CaseGuardianHarness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-v53-guardian-regression/
dotnet run --project tests/ParentEmailHarness.csproj --no-launch-profile -p:BaseOutputPath=C:/Users/ashle/AppData/Local/Temp/earlycare-v53-email-regression/
node --test tests/ParentV53Clients.test.cjs tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs tests/ParentAccountClient.test.cjs tests/ParentOtpClient.test.cjs
```

瀏覽器實測：

1. 進入 account 先出現合成同意文件；接受前孩子選單為空，受保護內容不顯示孩子。
2. 資料使用同意＋「不同意教師參與」仍可進入家長流程。
3. 讀取指定版本一題問卷，填姓名／日期／母親／選項，儲存草稿。
4. 返回再開啟，欄位與答案由伺服器草稿回復。
5. 提交取得回執；返回後伺服器統計為待填 0、填寫中 0、已提交 1。
6. 我的問卷重新查詢相同回執，不顯示成可再次填寫。
7. 1440×900、390×844、768×1024 檢查對話框邊界、操作與水平溢出；未發現水平溢出。瀏覽器 Console 無 error／warn。
8. 測試分頁關閉、viewport 還原，隔離測試程序停止。截圖工具擷取失敗，因此沒有宣稱已保存截圖證據。

409／503／斷線由自動化驗證，不宣稱已在瀏覽器逐一注入故障。正式 Email→真 MySQL 全流程、跨程序重啟與撤銷競態尚未驗收。

## E. Entity／Configuration／API 交接

- Case／Guardian 固定契約：v53-11-case-guardian-api.md。
- Consent／Gateway DTO、錯誤、交易回呼與 Contract Suite：v53-11-consent-gateway-handoff.md。
- Configuration、FK／索引／舊資料與教師邊界：v53-11-integration-model-teacher.md。
- 新 API：GET /api/parent/consent/context、POST /api/parent/consent；GET／PUT /api/parent/tasks/{taskId}/draft；POST .../submit；GET .../receipt。
- 真 Gateway 必須執行相同 ParentGatewayContractSuite，並額外通過真實 MySQL 的作者隔離、唯一鍵、Rollback、提交／撤銷一致性測試。
- RequireInTransactionAsync 只驗證同一 DbContext 及有交易、重新讀取資格；本身不是 Case 協調鎖，也不證明競態已解決。

## F. 等待項目

1. 院方：核定同意本文、版本、適用範圍 ACCOUNT／CASE；撤回／改選／重填／教師期限政策仍待決策。
2. 16：實作固定 Gateway、作者及關係欄位、單一儲存交易、共同鎖定、增量 Migration／MySQL 與重啟驗收。
3. 07：使用既有 Case DTO／API，最終 JWT／RBAC／部署整合；本機同來源開發無須等待部署。

本輪沒有正式家長 Response 查詢／歷史重填、正式教師重開、正式同意撤回 API，也沒有將這些列為完成。

## G. 工時規劃

本輪沒有按人時計時，不把自動化執行時間換算成已投入 42–45 人時。
11 後續 Adapter 實接／契約修正約 3–5 人時、核定同意與資料整合驗收約 2–4 人時、真環境家長流程與行動版回歸約 3–5 人時，合計暫估 8–14 人時。
以上不含院方等待時間與 16 的引擎／Migration 工作；若固定契約變動應重新估算，優先保留單一家長流程。

## H. Git 與保護

- Branch／HEAD 未改；未 Commit／Push／Pull／Fetch／Merge／部署。
- 原 7 個已追蹤修改與 4 個未追蹤檔案先行備份至 C:/Users/ashle/AppData/Local/Temp/earlycare-v53-before-20261021-222028，含 SHA256 manifest 與 tracked.patch。
- 非本輪目標的原修改檔案逐一比對 SHA256 保持一致；account／HTML／Consent 只在原成果上增量修改。
- Program.cs、ApplicationDbContext.cs、既有 Migration／Snapshot 沒有修改；未建立新正式 Migration，未執行 MySQL。
- 目前工作區含原修改與本輪新增，尚未提交。HEAD 與本機保存的 upstream 為 0／0；未查證即時遠端。
- 下一階段先交接固定文件及 Contract Suite；取得真引擎與核定資料後再實接與驗收，不先啟用正式寫入。
