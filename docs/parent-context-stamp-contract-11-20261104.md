# Parent Context／EligibilityStamp 與作者預檢交接 v1 草案

日期：2026/11/04；11 修改基準 26e8b7f。16 提供 bdf9ba57caedd0b35b2c1c0e5d2b9309bd56b677，本輪未 Fetch／讀取該版本，未聲稱 Bridge 編譯相容。固定 SHA 見本次提交。

## 本輪程式修正

ParentQuestionnaireAccess 在確認可信 actor、Case／Task 範圍及唯一 Guardian 後，呼叫 IParentAuthorVisibility，再確認 Consent、回傳編輯狀態。跨作者統一 TASK_NOT_FOUND／404，不讓 ARCHIVED 等狀態先回 409。取消／不在範圍任務仍 404。SUBMITTED 保留給核心判斷授權重送／收據，不在此處一律擋住。

IParentAuthorVisibility.CanRevealAsync(ApplicationDbContext db, Guid actorUserId, Guid taskId, CancellationToken ct) 是 metadata-only 預檢，不回傳答案、姓名或作者識別。16 真作者 Store 需確認 Draft 與 Response 任一既有列都屬於可信 actor；foreign 或 unknown 均 false，不從目前 Guardian、姓名、教師 Grant 推定。

本分支目前沒有 ParentAuthorUserId：預設 LegacyParentAuthorVisibility 僅在 Draft／Response 均不存在時允許通過；既有列因作者未知回 false。不是已完成正式作者辨識，整合時須明確替換成 16 的作者查詢 Adapter。正式 Gateway 仍 Unavailable。測試替身 FakeAuthorVisibility 只在 tests，用測試作者 metadata 驗證順序；共同交易仍由核心重新授權及查作者。

Consent Filter 在 Controller 前仍檢查身分／範圍／同意，沒有回傳任務編輯狀態。作者遮蔽不等於停用同意檢查，也不允許用未授權的 Task 查作者。

## Context 承載：供雙方固定型別

現有 ParentRequestContext 的 Principal、ActorUserId、OrganizationId、CaseId、GuardianId、ConsentId、DocumentVersionId 保留；四方法 Gateway 與 HTTP DTO 不變。

建議新增一個內部 Evidence 屬性，不放 HTTP body／JWT／瀏覽器儲存。以下是待共同確認的型別草案，**本輪未加入 Runtime Context，也沒有產生可用 Stamp**：

| Evidence 欄位 | 候選型別 | 來源／語意 |
|---|---|---|
| ContractVersion | string | 固定內部協定版本，候選 parent-eligibility/v1。 |
| TaskId、QuestionnaireVersionId | Guid | 伺服器任務綁定；不得由前端重選版本。 |
| BindingGeneration | ulong? | CaseGuardian 持久資格世代。NULL 是未知，正式操作拒絕。 |
| ParentEligibilityEpoch | ulong? | Organization 全院資格世代；不因單一撤綁自動增加。 |
| DocumentEligibilityEpoch | ulong? | 文件內容／範圍／生效狀態世代。 |
| DocumentContentHash、ScopeCode、ScopeKey | string | 同意文件及適用範圍，伺服器查回。 |
| EligibilityStamp | string? | 後端產生的 opaque 值；NULL／空／未知協定拒絕，不能猜常數。 |

Stamp 還須涵蓋 Context 的 actor／院所／Case／Guardian／Consent／文件 ID。不能只用以上三個世代就宣稱 ABA 完整：User 停用再恢復、角色／Permission 改回、Task 關聯改回，以及跨模組資格寫入尚需持久版本或受控寫入限制。對應 User／授權／Task epoch 的名稱、資料來源與表示法待 07／16 共同固定，不先自行修改共同 Entity。

## Stamp 編碼／比對提案

- Bridge 將同一 Evidence 原樣傳入，不能重新產生、補零或靜默替換 Consent。
- 候選格式為帶版本前綴的 opaque string；欄位排序、Guid 格式、數值格式、null、UTF-8、雜湊及大小寫需一起固定後才寫 encoder。暫不公布可供正式運作的演算法，避免雙方各自建立不同版本。
- 不包含一般 Draft Revision、UpdatedAt、TaskStatus、SubmittedAt；正常 Save／Submit 不應因自身更新造成 Stamp 誤變。
- TaskStatus 不進 Stamp 不代表略過狀態授權；交易內仍檢查 Task 是否取消、可寫及作者條件。
- 同一 DbContext／既有交易內重新查資格及持久世代，先授權，再比對原 Evidence；變更回 ACCESS_PRECHECK_CHANGED／409，失去資格維持適當 401／403／404，不自動重送。
- Stamp 不是授權憑證，也不解決獨立查詢的讀取競態。共同鎖序、隔離等級及資格寫入端協調未完成前保持拒絕。

## 必須共同固定才能實作的項目

1. Evidence 最終欄位、Guid／ulong? 承載型別及 Bridge 版本標記。
2. User／權限／Task ABA 的持久證據或受控寫入限制。
3. Stamp 編碼、演算法及跨模組相同輸入測試向量。
4. IParentAuthorVisibility 的真 Store 接線；既有未知作者資料的拒絕及收據政策。
5. 文件協調資源 → 院所協調列 → User → Case → Guardian → CaseGuardian → Consent → Invitation → Task → Draft → Response 的實際 SQL、鎖範圍、多列排序、隔離等級。
6. 舊世代 NULL 的安全初始化及 Consent 政策；不能直接當第一代。

## 驗收

本輪增加真 HTTP＋SQLite scope、Fake 作者 metadata 的負向案例：PENDING／IN_PROGRESS／SUBMITTED／ARCHIVED 的他人 Draft／Response，讀草稿、查收據、保存與提交均回 404；自己的無效狀態仍 409；真 SQLite 既有未知作者草稿安全拒絕。測試數量及結果見交付回報。

未跑 MySQL、SMTP 或真核心並發；未新增作者／世代欄位，未改 Program.cs、DbContext、Migration、Snapshot、正式入口及前端。後續需使用同一套負向案例替換為真作者 Store／Gateway 再驗證。

本輪實際結果：ParentV53Harness 183 PASS，API 專案隨 Harness 建置成功；git diff --check 通過。作者已知部分採 Fake metadata、未知作者拒絕使用真 SQLite Draft／Response 列；不等同真 MySQL 作者／交易驗收。
