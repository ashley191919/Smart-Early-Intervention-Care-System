# Parent Model Union 最小整合交付

日期：2026/11/05。獨立分支 codex/integration-parent-model-union，以 11 b5573b5 為基底，選取 16 固定版本 8f03f8bd40e41eaf63dd8d6fd828052430b88113 必要來源。沒有 Merge 整個分支；原 feature/case-guardian-management 不變。

## 精確修改範圍

| 檔案 | 修改 |
|---|---|
| Data/Entities/QuestionnaireDraft.cs | 僅納入 nullable ParentAuthorUserId、RespondentRelationship。 |
| Data/Entities/QuestionnaireResponse.cs | 僅納入 nullable ParentAuthorUserId、RespondentRelationship、HashVersion。 |
| Data/ParentQuestionnaireModelConfiguration.cs | 原樣引入 16 作者 FK／索引、Relationship／HashVersion Mapping；不覆寫 CoreModelConfiguration。 |
| Data/ApplicationDbContext.cs | 僅新增 CaseStatusHistories DbSet；保留所有 11 Guardian／身分 DbSet 及 Configuration 掃描。 |
| Data/Entities/CaseStatusHistory.cs、Data/CaseStatusHistoryConfiguration.cs | 原樣引入 History 模型／Mapping。 |
| Services/Cases/CaseStatusPolicy.cs、CaseStatusHistoryPolicy.cs | History Mapping 的固定依賴；沒有新增狀態操作 API、Service／Workflow 或註冊醫療行為。 |
| Data/Migrations/20261019131342_AddCaseStatusHistories.cs 及 Designer | 原樣保留 16 第五筆歷史 Migration；沒有產生新 Migration 或執行任何 Up／Down。 |
| Services/Questionnaires/EfParentAuthorMetadataReader.cs | 原樣引入 16 metadata-only EF Reader。 |
| ParentModule/ParentModuleRegistration.cs | Reader DI 由過渡實作改為正式 EfParentAuthorMetadataReader；Gateway 仍 Unavailable。 |
| ParentModule/ParentAuthorVisibility.cs、ParentQuestionnaireContracts.cs | 移除過渡 Reader／visibility，相同作者 wrapper 與手動建構預設路徑均使用真 EF Reader。 |
| tests/ParentV53 | 模型／DI／Migration metadata、SQL 投影、真 SQLite 作者及失敗／交易擁有者驗證；Fake 答案仍限測試。 |

來源 blob 清單見 parent-model-union-source-manifest-20261105.json。10 份來源與 8f03f8b 原 blob 相同（Git 換行正規化）；原四筆 Migration、Designer、原 Snapshot 及 Case／User／Guardian／Consent／Core／Identity Mapping、Program.cs 均核對未變更。

原 Snapshot 仍保留 11 基底版本，尚未包含全部共同 Runtime Model。**五份 Migration 可在組件發現，不等於可直接套用取得最新 Schema。** 後續需 16 經正式增量審查更新共同 Snapshot／Migration，不以他分支 Snapshot 整檔覆蓋。

## 實際驗證結果

- 完整目前 API csproj 建置成功：0 warning／0 error；不是宣稱 07／16 全部 Controller、Store、Workflow 已整合。
- ParentV53Harness：249 PASS。使用真 Pomelo EF 設計模型（顯式 server version、未開連線）與記憶體 SQLite／HTTP。檢查五欄 nullable、作者 Restrict FK、教師 Grant／Task-version FK、Task／教師 Key／家長 Key 唯一鍵、Case 保留欄位、Consent／Guardian／History Entity 與 History DbSet、五筆 Migration 及 DI。
- 正式 EfParentAuthorMetadataReader 的 SQLite 實測：未知作者拒絕、本人草稿／答案允許作者預檢、跨作者 404；只 SELECT metadata，不讀 answers_json、respondent_name、payload_hash 或 hash_version；維持呼叫者交易，查詢失敗不當成不存在。
- 原作者 NULL／空值／跨作者／教師 Grant、Reader failure／取消等 wrapper 案例持續通過；已知教師 Grant 情境為隔離 metadata 測試，不冒稱 MySQL FK 並發驗收。
- CaseGuardianHarness：209 PASS，內含 71 項 Definition，不重複加計。
- QuestionnairePhase22Harness 預設模式：**FAIL（Schema gate）**，no pending schema/model changes。執行到該部署檢查才中止；沒有改測試白名單、繞過 gate 或把此項列為 PASS。共同模型／Migration 尚未對齊，這個失敗維持需處理。
- git diff --check 通過；來源／保留檔案 blob 比對通過。

沒有連 MySQL、SMTP、醫院系統或使用真實病患；SQLite EnsureCreated 只作用於測試記憶體連線，未對既有資料庫建表。沒有產生 Migration、修改原 Snapshot 或部署。無前端畫面修改。

## 真／Fake 與安全界線

正式 EF Reader 已用目前模型在隔離 SQLite 查到 ParentAuthorUserId；不再是過渡 Reader。然而正式答案 Gateway／交易資格 Adapter 仍未接上，Context Evidence 仍未知，未新增任何資格世代、Stamp 或 ABA 假值。模型可共同編譯不等於 MySQL Schema、資料政策、正式 Submit 或撤銷競態已完成。

新增五欄保持 NULL、無推定／回填。教師 FK 與唯一鍵保留；已授權收據查詢與成功重送依既有契約分流。Reader 的兩次 SELECT 仍需共同鎖序／交易重驗，不視為原子授權。

History Entity／DbSet／歷史 Migration 已具備；History Store／API、正式 Workflow、輪次與 save-only 交易不是本次範圍，不宣稱已接線。

## 需要組員配合

1. 16 審查此分支的共同 Runtime Model 與原 Snapshot 差異，固定完整增量白名單；保留五筆歷史 Migration，另安排共同 Snapshot／增量 Migration，修復 Schema gate。
2. 11／16／院方確認舊 Case 院所及必填個資缺漏處理、Consent 同文件重新同意／唯一鍵與歷史政策；本輪完全保留現行規則，不填假資料。
3. 16 提供真 Bridge／交易接線版本，三方固定 Stamp、ABA 持久來源、鎖序與隔離規格；11 再接正式資格 Adapter／寫入端。
4. 07 提供固定 stage-only Workflow／Scope 版本；本次新增的 History 模型不代替其正式業務服務。
5. 共同 Schema／資格與授權契約核定後，再另行安排隔離 MySQL 約束、升級／還原及並發驗收，最後才評估開啟正式入口。

## 可回覆 16

已在獨立 codex/integration-parent-model-union 完成最小模型聯集，保留 11 Case／User／Guardian／Consent、五欄作者 Mapping、History DbSet／模型及五份歷史 Migration，並替換正式 EF Reader。API build 成功，249 項家長／Reader／模型與 209 項 Guardian 回歸通過。舊 Phase 2-2 Schema gate 仍失敗，沒有放寬；需你審查共同 Snapshot／增量 Migration。正式 Gateway／Stamp／交易資格仍關閉，未操作 MySQL。請依本固定提交比對，不直接用原分支舊 SHA 接線。
