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
