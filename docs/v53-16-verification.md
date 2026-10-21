# V5.3／16 本次驗證紀錄（2026-10-21）

基準 533b659；交付固定版本請見本分支最終 HEAD。未 Fetch／Push／Merge，未連 MySQL、Migration、Seed、實體備份或還原。測試均在隔離 Worktree。

## 已實際執行

| 項目 | 結果 | 方法與限制 |
|---|---|---|
| API Build | 成功，0 error；NU1900 warning | .NET 10 RC SDK，target net8；弱點索引無法取得，不宣稱漏洞掃描完成 |
| ParentQuestionnaireHarness | 101 checks PASS | 真 Gateway／Validator＋transactional Fake Store／Access Adapter；EF 離線模型與 DI；沒有 MySQL／HTTP |
| PermissionServiceOfflineHarness | 34 passed／0 failed | 實際 PermissionService、ADO 攔截、無 DB |
| RbacOfflineHarness | 22 passed／0 failed | Handler／Policy 替身與拒絕規則 |
| RbacPolicyHarness | 43 passed／0 failed | 四個 Policy 與 DI，無 DB |
| RbacHttpHarness | 19 passed／0 failed | loopback HTTP，合成帳號／ADO 結果，不是正式三端 Host 或 DB |
| QuestionnaireRoundCompletionHarness | 37 checks PASS | 純計算，未選有效輪次 |
| QuestionnairePhase21Harness | PASS，Exit 0 | 指派／摘要 Fake Store／離線，無 DB |
| QuestionnairePhase22Harness --parent-author-schema-proposal | 158 checks PASS | 教師原行為回歸；只容許精確六項 pending 模型差異；不放行部署 |
| CoreDatabaseHarness 預設 | Exit 1，需 reviewed Migration | 預期 gate：模型新增作者欄位，Snapshot 未改，不將不一致算通過 |
| CaseStatusHistoryMySql --offline | Exit 1，MODEL_SNAPSHOT | 預期 gate；本次不能宣稱 70 項離線全通過，亦未執行 MySQL mode |
| PowerShell AST | 0 parse errors | Start-LocalTest 語法解析，不等於啟動成功 |
| Start-LocalTest -CheckOnly | 未能執行 | Windows Execution Policy 拒絕，沒有 Bypass／政策修改 |
| 原五份 Migration／Snapshot 與 teacher workflow／validator／store | git diff 無差異 | 原有行為檔案不覆蓋 |

## 測試包含的家長情境

- 無／歧義身分在開 Store 前拒絕；作者取唯一可信 userId。
- adapter 拒絕、缺 Guardian／Consent 證據、錯 Case／Task／角色、取消任務不寫入。
- 作者隔離；另一個具 scope 的家長不能接管既有草稿／答案。
- partial draft、revision、overflow、過期更新、重建 Gateway 後從 Store 查回。
- 完整提交、payload 驗證、Task／Response 原子保存、同 key 重送、不同 payload／Task 衝突。
- commit 故障 rollback／重試、commit 前再驗證失敗 rollback、receipt／replay 再驗證拒絕。
- 重疊提交及草稿呼叫使用 Fake mutex，不能代替 MySQL 鎖／撤銷競爭測試。
- 醫療 Permission ＋ scope 必須同時成立；查詢故障不放行。
- loopback DB 設定 guard 的拒絕／允許形狀；只 parse 連線設定，沒有連線。

Fake Access Adapter 只回傳允許／拒絕證據，沒有真正查 Guardian／Consent。跨院所、角色、撤銷的資料層判斷仍需 11 真 adapter 與真 DB 驗收。
Server-side reread 是 Fake Store 層證據，不是重啟持久化證據。

## 待執行

11 真 Consent／Guardian adapter；核定 PARENT 問卷；新增 Migration／Snapshot；真正 MySQL FK／唯一鍵／鎖／回滾／重送；Level1 API 保存重讀；Level2 重啟及受控備份還原。Level3 未啟用。

## 重現命令（離線，不套用 DB）

```powershell
dotnet build EarlyInterventionCare.Api/EarlyInterventionCare.Api.csproj --no-restore
dotnet run --project tests/ParentQuestionnaire/ParentQuestionnaireHarness.csproj --no-launch-profile
dotnet run --project tests/QuestionnairePhase22Harness.csproj --no-launch-profile -- --parent-author-schema-proposal
```

勿把 proposal 參數加到部署工具。預設 CoreDatabase／History MODEL_SNAPSHOT gate 的拒絕必須保留至正式增量產生並驗收。

## 接續補強批次：以 482d575 為基準

本批只在 codex/v53-parent-core Worktree 開發；未 Commit／Push／Merge，交付狀態為 HEAD 482d575 加工作區差異。沒有變更任何其他 Worktree、Program、Migration／Snapshot、Entity、原教師核心或環境設定。

### 測試先行證據

1. 先新增 Policy 衝突測試：原版 12 通過／16 失敗，同名普通 Policy、錯 Permission、缺登入等均未拒絕。
2. 先新增 Task／Response 不一致測試：原版 GetStatus 沒有拋 STORAGE_INCONSISTENT，測試失敗。
3. 初次以 PostConfigure 檢查後，再新增較晚 PostConfigure 覆寫：28 通過／2 失敗。最後改用 IValidateOptions，在所有 Configure／PostConfigure 後驗證，沒有放寬安全條件。
4. EF Harness 起初出現 reader 型別／savepoint／write-result 模擬不符，均修正在測試 ADO 替身；正式 EF Store 不必修改。測試早期未處理例外曾造成 apphost 鎖定，改為明確回報 Exit 1，EF Harness 使用 DLL host；未操作資料庫或系統安全政策。

### 本批最終實際結果

| 項目 | 結果 | 層級 |
|---|---|---|
| API Build | Exit 0，0 errors；NU1900 | 編譯；弱點索引未取得 |
| ScopedReadSafety | 44 passed／0 failed | 38 unit／DI＋6 loopback HTTP；合成 authentication、permission、scope，無 DB |
| EfParentStoreHarness | 86 checks PASS | 正式 Store／Gateway＋EF／Pomelo SQL 轉譯、ADO／transaction 替身與攔截器，無 socket／MySQL |
| ParentQuestionnaireHarness | 126 checks PASS | 原 101＋25 個一致性拒絕檢查；transactional Fake Store／Adapter |
| PermissionServiceOfflineHarness | 34 passed／0 failed | EF／ADO 攔截 |
| RbacOfflineHarness | 22 passed／0 failed | Handler／Policy 替身 |
| RbacPolicyHarness | 43 passed／0 failed | Policy／DI |
| RbacHttpHarness | 19 passed／0 failed | 既有 loopback HTTP／JWT 與 ADO 合成結果 |
| QuestionnairePhase22 proposal mode | 158 PASS，Exit 0 | 教師回歸；精確六項 Schema 提案仍不是部署核准 |
| QuestionnairePhase21 | PASS，Exit 0 | 指派／完成摘要離線回歸 |
| RoundCompletion | 37 PASS，Exit 0 | 純計算 |
| CoreDatabase 預設 gate | Exit 1 | pending 模型未做 Migration，維持拒絕 |
| History --offline gate | Exit 1 MODEL_SNAPSHOT | 維持拒絕；本次不宣稱既有 70 項通過 |

新 production 修改僅：ScopedReadAuthorization 的最終 Policy 驗證、ParentQuestionnaireGateway 的一致性拒絕。
EF Store 實作不變；測試直接執行其 SQL 轉譯與 SaveChanges 路徑，而不是以 Fake Store 代替它。

### EF 測試能證明及不能證明的事

- 可證明：WHERE／參數形狀、作者＋key 條件、Task lock SQL、原 revision 條件、SaveChanges／Commit／Dispose 路徑、同一 command transaction、異常映射、tracker 清理、去重與拒絕流程。
- 合成 rows／ROW_COUNT／RecordsAffected 由攔截器提供，不是 SQL engine；transaction rollback 是 ADO double 的狀態紀錄，不是 MySQL 的資料回復證據。
- 跨院所／Consent 拒絕由 Access double 回傳，仍須 11 真 Adapter／DB 證明其政策與查詢。沒有替 11 實作另一套 Guardian／Consent。
- 不能證明：真 FK／唯一鍵執行、MySQL isolation／死鎖／撤銷競爭、DDL、重啟持久化、備份還原、正式 Cookie／跨網域。

### 可重現命令

```powershell
dotnet run --project tests/ScopedReadSafety/ScopedReadSafety.csproj --no-restore --no-launch-profile
dotnet build tests/EfParentStore/EfParentStoreHarness.csproj --no-restore
dotnet tests/EfParentStore/bin/Debug/net8.0/EfParentStoreHarness.dll
dotnet run --project tests/ParentQuestionnaire/ParentQuestionnaireHarness.csproj --no-restore --no-launch-profile
```

首次使用新 Harness 需一般套件還原；沒有新增第三方套件。此環境有 NU1900，不能把 Build 成功當作弱點資料查核成功。
真正 MySQL、Level1 主機啟動、Level2 持久化／備份還原均未執行；只執行測試期間的 ephemeral loopback HTTP，測後停止。
