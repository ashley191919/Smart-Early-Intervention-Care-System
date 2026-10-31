# Guardian／Consent 資格世代模型交接提案

日期：2026/10/31。11 主責業務模型與寫入端，16 統整共同 Schema／Migration。

**本文件是模型／一致性協定提案，尚未加入 Runtime Entity／Configuration，也未建立或套用 Migration。** 正式 Gateway／Consent 入口仍關閉。不得依本文件推定院方已核准撤回或重新同意政策。

## 現況與必要差異

| 模型 | 既有欄位／問題 | 候選差異與 Mapping |
|---|---|---|
| CaseGuardian／case_guardians | (CaseId, GuardianId) 複合鍵；ACTIVE、BoundAtUtc、RevokedAtUtc；沒有資格世代；撤綁重綁重用同一列 | BindingGeneration／binding_generation，候選 ulong?、BIGINT UNSIGNED、並發 token；舊資料 NULL 表未知，不能假設舊資格已具世代。新關係由受協定保護的後端建立為 1。 |
| Organization／organizations | Status 等資格條件；沒有跨流程資格 epoch | ParentEligibilityEpoch／parent_eligibility_epoch，候選 ulong?、BIGINT UNSIGNED；僅由院所整體資格變更遞增。由共同模型維護者落地。 |
| ConsentDocumentVersion／consent_document_versions | DocumentVersionId、內容 Hash、ScopeCode、生效／停用；沒有資格 epoch | EligibilityEpoch／eligibility_epoch，候選 ulong?、BIGINT UNSIGNED；文件資格內容、適用範圍或生效狀態變更時遞增。 |

nullable／初始值／並發 token 最終由雙方固定，**未宣稱已與 16 完全定案**。先允許舊資料 NULL 的提案是避免偷偷回填；正式啟用前需明確且可稽核的回填／重綁方案。世代未知時拒絕正式交易，不取零、時間戳或固定值代替。

- 不改既有 CaseGuardian FK、複合主鍵及主要聯絡人唯一索引，不需以 generation 建立另一套關係表。
- 資格查回沿用 CaseId／GuardianId／OrganizationId／ParentUserId；現有索引是否足夠由統整者依共同模型確認，不預先新增競爭索引。
- ParentConsentRecord 保留 ConsentId、文件 ID／Hash、範圍、actor、決策及撤銷時間。不用 GuardianContact.Revision 代替綁定世代。
- 不新增正式 Response.HashVersion；此欄位與 canonical Hash 由 16 負責，11 只處理錯誤邊界與測試。

## BindingGeneration 寫入矩陣

| 操作 | 候選世代規則 | 醫療／家長行為限制 |
|---|---|---|
| 建立新 CaseGuardian | 初始 1 | 尚未完成邀請綁定仍不能填答。 |
| 完成邀請兌換／綁定 | 真正完成狀態轉換才 +1 | actor、指定 Email、院所、邀請一次性及目前狀態都要重新檢查。 |
| 撤綁 | ACTIVE → REVOKED 才 +1 | 保留列與既有答案；不能刪除後重建相同識別。 |
| 重新連結 | REVOKED → ACTIVE（尚未綁定）才 +1 | 不沿用舊 BoundAtUtc／舊預檢；之後完成綁定再 +1。 |
| 同一請求重送 | 沒有新狀態轉換，不 +1 | 回既有結果或明確衝突；仍需授權，不為了重送新增世代。 |
| 修改純聯絡姓名／電話 | 不因純文字變更增加綁定世代 | ParentUserId／院所／關係資格等有權限影響的變更不能歸為純文字修改。 |
| 世代溢位／未知 | 安全拒絕 | 不繞回 0，不 reset，不以 current-time 補值。 |

單一關係撤綁只改該 BindingGeneration，**不自動遞增全院 ParentEligibilityEpoch**。院所級協調鎖與全院資格失效是不同概念。

## 共同一致性協定：尚待固定

- 候選第一版採跨程序有效的院所協調鎖，不以 C# lock／SemaphoreSlim 當 MySQL 共同鎖。
- 候選鎖序：文件 → 院所 → User → Case → Guardian → CaseGuardian → Consent → Invitation → Task → Draft → Response。
- 精確 SELECT FOR UPDATE／隔離等級、鎖範圍、多列排序及文件鎖策略尚未定案；目前沒有 SQL／執行碼可作正式鎖定證明。
- Adapter 使用 Gateway 傳入的同一 scoped ApplicationDbContext／既有交易，不自行 Begin／Commit；不同 context／缺少交易安全拒絕。
- 所有變更資格的寫入端需遵守同一協定；讀取預檢結果不構成持鎖授權。
- EligibilityStamp 欄位集合、編碼與演算法待共同固定，不自行產生常數 Stamp。應至少涵蓋可信 actor／院所／Case／Guardian、世代、Consent／文件 Hash／範圍及 Task 關聯；還要重新查有效狀態。
- 交易內資格已改變回 ACCESS_PRECHECK_CHANGED／409；已無權限仍維持適當 401／403／404。死鎖不自動重試。

## 寫入端責任及缺口

| 範圍 | 責任／待整合 |
|---|---|
| CaseGuardianService 重新連結、邀請兌換、撤綁、Guardian 帳號改綁 | 11；正式加入世代前需一起固定鎖序，避免只改其中一條路徑。 |
| Consent 新增／撤銷／重新同意、文件生效／停用 | 11 業務；撤銷／重新同意部分尚無正式政策及完整 API。不能先讓舊 Consent 列取消撤銷以冒充重新同意。 |
| User 狀態、角色／權限、院所管理範圍 | 07／16 共用模組；需列入協調清單。 |
| Case／Guardian 院所、ParentUserId 變更 | 11 模型；必須與共同身分契約對齊。 |
| Task 取消、版本／Case／角色關聯變更 | 問卷／醫療共用模組；16／07 配合。 |
| Gateway、HashVersion、答案及 Task／Response 交易 | 16；11 不建立第二套正式保存服務。 |

## 驗收矩陣

已執行的本輪測試：現有 Guardian 撤銷阻止存取與重送、未完成重綁不恢復 scope、合成綁定完成後重新預檢有效、Consent 撤銷阻止成功重送、文件停用阻止讀取、未知 HashVersion 拒絕重送但已授權收據仍可查詢。

**尚未通過／尚不能執行的共同整合驗收：**

1. 同 GuardianId 撤綁重綁後，以舊世代預檢提交必須 409（ABA）。現有 Context 尚無世代，不能將 ID 比較測試冒充此項完成。
2. 真寫入端重複請求不重複 +1、並發單調遞增、溢位拒絕。
3. 新 Consent／文件 epoch 改變後舊 Stamp 必須拒絕；院方重新同意政策先確認。
4. Submit 與撤綁／Consent 撤銷實際隔離 MySQL 並發、回滾、鎖順序及多程序測試。
5. 同院不同 Case 單一撤綁不失效其他家長；跨院不因粗文件鎖不必要互阻。

SQLite Fixture 的回復列只用於隔離測試清理，不是正式重綁／重新同意實作；Fake Gateway 的 HashVersion 只作契約情境，不是 canonical Hash 實作。

## 仍需院方確認

撤銷資料使用同意的影響範圍、生效時間、歷史保存與收據查閱；重新同意與新文件是否要求再次確認；教師參與撤回是否連動既有 Grant。拒絕教師參與不阻擋家長自己的問卷。未定政策不寫入正式規則。
