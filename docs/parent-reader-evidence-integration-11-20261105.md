# 11／16 Reader、Evidence 接合與 Workflow 比對交接

日期：2026/11/05。11 基準 7396610；必要來源固定於 16 的 3463e37cfb3e81bf81e0450cd322a0895e16f567；Workflow 文件固定於 159c27f7790a77eca1ab2d746aac9fd7202b6688。

本輪已 Fetch 固定分支並讀取上述來源，沒有 Merge／Cherry-pick 整個分支。沒有修改 Entity、DbContext、Program.cs、Migration／Snapshot，沒有 MySQL、SMTP 或部署。

## 來源與實際接線

逐檔引入下列原始來源，未自行另定共用型別：

- Services/Questionnaires/ParentAuthorMetadataContracts.cs
- Services/Questionnaires/ParentEligibilityEvidence.cs
- Services/Questionnaires/ParentEligibilityEvidenceChecks.cs

ParentRequestContext 新增可空 Evidence 參數，型別為單一共用 ParentEligibilityEvidence。原四方法 Gateway／HTTP DTO 保留。現存預檢仍產生 Evidence=null，不造世代或 Stamp；正式 Gateway 維持 Unavailable。Evidence 結構檢查及缺欄位清單都不是交易授權。

MetadataParentAuthorVisibility 使用共用 IParentAuthorMetadataReader，逐筆判斷 Draft／Response：任一既有列作者 NULL／空 Guid／非本人／帶 TeacherGrant 則拒絕。兩筆不存在只通過作者檢查；不存在列卻帶作者／Grant 的不一致 metadata 也拒絕。原 scoped DbContext、TaskId、CancellationToken 原樣傳入，Reader 失敗／取消不轉成不存在。

## EF Reader 的明確限制

16 的 EfParentAuthorMetadataReader 投影 ParentAuthorUserId，本分支 Draft／Response Entity 尚無此欄位。為避免自行修改共用模型，**本輪未引入／註冊該 EF 實作，也未宣稱已接真作者 Store**。

目前 DI 接 MetadataParentAuthorVisibility + LegacyParentAuthorMetadataReader。過渡 Reader 只投影現有列存在性及教師 Grant，ParentAuthorUserId 一律保留未知 NULL；有既有列就拒絕，不從 Guardian 推定。模型及 Mapping 經整合後，替換 IParentAuthorMetadataReader 為 16 原 EF Reader，再重跑相同測試。沒有把 Fake Reader 放入正式服務。

LegacyParentAuthorVisibility 保留供既有手動建立預檢者相容，內部同樣委派新 wrapper，不維護第二套作者規則。這項可見性檢查仍只是預檢，兩次 SELECT 不是原子資格證據，正式交易必須重查並協調鎖。

## 測試

ParentV53Harness：212 PASS；API 隨 Harness 建置成功。包含：

- 兩筆不存在、本人 Draft／Response 可通過作者檢查；兩側各自測未知／空作者、跨作者、教師 Grant、不一致 metadata 拒絕。
- 原 DbContext／Task 識別保留；Reader Timeout／取消傳遞；空 actor 不放行。
- Context Evidence 保持未知；共用 Checks 回報缺少世代／ABA／Stamp，拒絕無效 ID／未知版本，不補值；正式 Gateway 仍 503。
- 原四狀態／四操作作者遮蔽 HTTP 案例使用新 wrapper 與測試 Reader；真 SQLite 未知作者列仍拒絕。

已知作者 metadata 來自 Fake；SQLite 不是 MySQL 鎖驗收，未證明實際 EF ParentAuthorUserId 欄位或正式世代來源可用。沒有重跑與本輪無關的前端測試。

## Workflow 候選文件的實際比對

| 比對項目 | 11 現況／結論 |
|---|---|
| Case／Scope | 11 Case 有 OrganizationId／Revision，Guardian 檢查依賴此院所模型；16 文件也指出其基準 Case 尚無這些欄位，必須保留 11 差異。 |
| 家長 Submit | 11 只有專用入口及未啟用 Gateway；真家長 Store 未合入，不能宣稱可呼叫 Workflow。 |
| Task／輪次 | 既有 AssignmentRound 不能代替權威有效輪次；本分支尚無提案中的輪次指標／封存模型。 |
| 完成度 DTO | CandidateRoundCompletionDto 的未知值 nullable、Source、IsCommitted 區分合理，但尚無 11 正式消費端。不是既有 HTTP DTO 的可直接替代物。 |
| 交易能力 | 需要 save-only 與單一擁有者；不能先提交答案再更新 Case。16 Store 介面需先提供真實固定版本，11 不自行另開保存引擎。 |
| Workflow／History | 本分支沒有正式 Workflow Service；07 需交付 stage-only 版本與權限契約。未聲稱已完成 History 整合。 |
| 鎖序 | Case→Task 僅局部順序；不得持有 Task 後反向取得 Case。要銜接文件／院所／User／Guardian／Consent 等資格資源及教師 Grant／Session、輪次資源。 |
| 成功重送／收據 | 同意不重複觸發 Workflow／History；未知 HashVersion 仍拒絕重送，授權歷史收據分開處理。 |
| Scope | 自動 Workflow 不代表家長得到醫療寫入權；完成度不取代 Guardian／Consent／教師 Grant 驗證。 |
| 未定政策 | 必要問卷取消、零必填、封存／重開及 History 去重待共同與院方確認。不將 PendingIntegration 當成吞掉儲存錯誤。 |

結論：原則可對齊，完整模型／DTO／交易接口仍未完成跨分支編譯與 MySQL 驗收。本輪未實作候選輪次模型、Workflow 或 save-only Store。

## 下一輪交接需求

1. 16／統整者先固定家長作者 Entity／Mapping 白名單；11 再切換真 EF Reader，驗證 metadata SQL 不讀答案／姓名／Hash。
2. 共用 Evidence 型別已引用；待 Stamp golden vectors、六項 ABA 持久來源、共同鎖 SQL／隔離層級固定後，11 才產生真實 Evidence 並實作正式資格 Adapter。
3. 16 提供 Bridge／save-only 交易簽章，07 提供 stage-only Workflow；三方固定全域取鎖順序後，再做負向／回滾／重送／並發驗收。

正式入口仍關閉。Schema gate、MySQL 及資格競態缺口維持未完成，不能以 212 項隔離測試取代。
