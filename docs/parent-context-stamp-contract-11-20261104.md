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

## 交接決策表（2026/11/04 補充）

以下工作已完成，不需等待後重做：作者預檢遮蔽與 183 項驗證、HTTP 錯誤及 Client 保護、Context 型別草案、資格世代提案。程式固定版本為 2b51d7c；本節僅補文件。

| 請 16 確認／提供 | 11 的具體建議 | 確認後可進行 |
|---|---|---|
| Bridge 是否接受 Context.Evidence 的內部型別與 nullable ulong 世代？請提供精確 Command／Adapter 方法簽章及固定 SHA。 | 先沿用本文欄位；未知世代拒絕，不改 HTTP DTO。Evidence 應綁定 TaskId／版本，不能由前端建立。 | 11 建立對應內部 DTO；16 轉接 Bridge。 |
| 作者預檢能否直接使用 16 的 metadata-only Store？ | 由 16 提供可信作者查詢方法；11 實作 IParentAuthorVisibility wrapper。必須檢查 Draft、Response，foreign／unknown 均拒絕，查詢失敗不放行。 | 11 接真作者 Adapter，重跑四狀態／四操作負向測試。 |
| 是否同意先固定 Stamp 共同編碼與測試向量，並確認 User／Permission／Task ABA 證據？ | 三個目前提議的世代不足以涵蓋所有 ABA；缺少證據的資格寫入先限制。版本、欄位順序、null 及數值格式由同一編碼器產生。 | 11 寫 encoder／資格讀取；16 使用相同向量與交易內比對。 |
| 核心提供什麼交易協調接口？ | 請提供同 DbContext／現存交易的鎖資源取得方法、實際 SQL／隔離等級與鎖序；不要讓兩邊各自猜鎖。 | 11 調整下列寫入端，16 整合 Draft／Submit／Receipt。 |

可平行做的事情：16 準備 Bridge／作者 Store／交易協調介面；11 先查明資格寫入端及現存交易邊界（清單如下）。不必等待整個 Adapter 完成才交換介面。

### 11 現存資格寫入端與交易邊界

程式依據：Services/Cases/CaseGuardianService.cs、ParentModule/ConsentService.cs。本輪只讀取分析，未修改寫入行為。

| 方法 | 目前會做什麼 | 待共同協定接入 |
|---|---|---|
| AddGuardianAsync | 建立聯絡人與 CaseGuardian | 新關係初始世代、與院所／Case 鎖協調。 |
| LinkGuardianAsync | 新增關係或重用 REVOKED 列，清除舊綁定時間 | 狀態轉換原子遞增世代；禁止重設／硬刪重建。 |
| UpdateGuardianAsync | 自行 Begin／Commit，更新聯絡資料及關係 | 分清純文字及資格變更；作為根操作時管理交易，受共用交易呼叫時不能再開巢狀交易。 |
| InviteAsync／RevokeInviteAsync | 建立或撤銷未消耗邀請，目前各自保存 | 邀請與綁定／撤綁互斥協調，不能邀請讀過舊資格後直接寫入。 |
| RevokeLinkAsync | 自行 Begin／Commit，撤銷關係與未消耗邀請 | 關係世代遞增、完整共同鎖序、相同請求重送語意。 |
| BindAsync | 自行 Begin／Commit，消耗邀請、綁定 ParentUserId、設 BoundAtUtc | 身分／指定 Email／院所重新檢查、綁定完成世代遞增。 |
| RecordAsync（Consent） | 新增同意，RequestId 去重及唯一性衝突處理 | 新同意與提交共用協調鎖；業務根操作自行擁有交易，Adapter 不提交。 |
| UpdateProfileAsync／UpdateAsync（Case） | 修改姓名，或個案基本資料 | 目前純資料修改不任意增加綁定世代；將來院所／帳號歸屬改動須另受資格協定控制。 |

目前沒有正式 Consent 撤銷／重新同意或文件發布管理 API，不虛構已存在的寫入端。這些需院方政策及正式服務設計後才實作。User／Permission／Task 寫入端由其他模組維護者配合列入清單。
