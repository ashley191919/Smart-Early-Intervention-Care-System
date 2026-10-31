# 家長 Gateway 契約對齊：11 交接紀錄

建立：2026/10/30；本輪更新：2026/10/31。

11 分支：feature/case-guardian-management，修改基準 e2affb548bb427cd02d001d5bfd5761d7ecb4648。本輪將先前本機補強與以下新成果一起保存於本功能分支；固定 SHA 以本次 Git 提交為準。

16 介面曾查閱固定版本 9d7bd31783942d61c12a64fde97585f66f6b371f；使用者後續提供 a366d43f5be51e2b4998f3bb9756be523b194d76 的設計回覆，本輪沒有取得／驗證該新 Commit 的遠端內容，不宣稱已與其 Bridge 編譯整合。

## 已完成的程式

- ParentControllers／HTTP DTO：Draft 關係可 null，Submit 必選；Revision 使用 UInt64 十進位字串，拒絕前導零、溢位等格式。
- ParentQuestionnaireAccess：可信登入者／院所／Case／Task 查回唯一 GuardianId；無資料或多筆歧義回 404。不接受前端自選 GuardianId，也沒有生成假的 EligibilityStamp。
- ParentCorePayloadMapping：未接線的轉換準備元件，保留關係與 Submit Revision。正式 Bridge、Command／Hash 與答案交易由 16 維護。
- ParentCoreHttpErrors／ParentModuleErrors：家長核心例外轉安全 code／message／traceId，不洩漏 SQL／Exception.Message，不覆蓋教師錯誤。
- 前端失效處理：資格變更停止填答、清除失效資料，由使用者重新確認；不自動重送、不退回 localStorage 假保存。

## 2026/10/31 固定的兩個錯誤

| code | HTTP | 前端行為 |
|---|---|---|
| ACCESS_PRECHECK_CHANGED | 409 | 提示資格變更，重新確認；不自動重送。已失去授權仍可回既有 401／403／404，不把它偽裝成資格變更。 |
| IDEMPOTENCY_VERSION_UNSUPPORTED | 409 | 「目前無法確認先前提交結果，請聯絡服務人員。」停止保存／提交，不換 Key，保留授權收據查詢。 |

未知 Hash 版本在目前 Client instance 中保持寫入阻擋，即使顯式讀回草稿也不解除。不依賴此記憶體旗標作正式安全控制：頁面重開後真核心仍須自行拒絕不支援版本的重送及已提交 Task 的新 Key。

原 Key 保留在 Client pending intent；網路中斷可由使用者重試原內容／原 Key，但未知版本不能走該重試。收據由已授權 GET 單獨取得，不能因未知 Hash 回假成功，也不能略過作者／Guardian／Consent 檢查。

## 本輪驗證結果

| 驗證 | 結果／邊界 |
|---|---|
| ParentV53Harness | 164 項通過；真 HTTP Controller＋SQLite Guardian／Consent＋Fake 答案。 |
| CaseGuardianHarness | 209 項通過，其中含 71 項 Definition，不重複加計。 |
| 五組 Node 前端測試 | 72 項通過。 |
| API 編譯 | 本輪 Harness 帶入 API 專案建置成功。 |
| 瀏覽器 1440×900 | 未知 Hash 版本提示正確，保存／提交／草稿重新載入停用，查詢結果可操作；對話框完整可見、無根節點橫向溢位。 |

新案例涵蓋：未知／空／不支援 HashVersion 拒絕重送、不重算舊 Hash、不偷偷升級版本；已授權收據仍可查；撤綁阻止重送、未完成重綁阻止 scope、合成完成綁定後重新預檢有效；Consent 撤銷阻止重送；文件停用阻止讀取。Fixture 回復列只作隔離清理，不是正式重新同意流程。

測試 Fixture 使用 --serve --unsupported-replay 可手動觸發未知版本畫面，僅存在 tests/ParentV53，先正常授權才拒絕 Submit。Fake 不進正式 DI，無真實病患、SMTP、MySQL、Migration 或正式答案保存。瀏覽器驗收後臨時服務停止、viewport 還原。

畫面證據：[未知版本安全拒絕](evidence/parent-contract-1031/unsupported-replay.png)。先前 [關係必選](evidence/parent-contract-1030/relationship-required.png)／[隔離回執](evidence/parent-contract-1030/receipt.png) 為 10/30 合成測試紀錄。

## 模型交接與尚未完成

詳見 [資格世代模型交接提案](parent-eligibility-generation-proposal-11-20261031.md)。BindingGeneration、院所／文件 Epoch 僅為提案，未加入 Runtime Model、Configuration 或 Migration。列出初始值／舊資料未知、每種狀態轉換、重送、溢位、FK／索引及責任範圍，供 16 統整。

仍缺：精確鎖序／跨程序鎖／隔離等級、Stamp 欄位與編碼、資格世代真寫入端、撤綁重綁 ABA、Consent 重新同意政策、真 IParentTransactionAccessAdapter／Bridge、隔離 MySQL 並發驗收。

正式入口仍使用 Unavailable Gateway。未修改 Program.cs、DbContext、Entity、正式 Migration／Snapshot、History Migration，也未 Merge／部署。Definition 固定 Task 版本讀題仍保留。

## 給 16 的交接重點

本輪固定 HTTP 409 錯誤、前端禁止未知版本重送及授權收據分流，並保存先前 Revision／關係／GuardianId 補強。請以此提交固定 SHA 比對 Controller／DTO。BindingGeneration／Epoch 是文件提案，並非已落地；待共同規格確認後，由 11 補業務寫入端與交易 Adapter、16 補 Bridge／交易及 Migration，再用相同 Contract Test 接真核心驗證。
