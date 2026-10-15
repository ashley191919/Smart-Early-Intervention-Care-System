# 16 第二批：指定輪次完成度核心

本批新增純計算元件，未接入任何 HTTP API、DI、資料查詢或 CaseStatus 寫入。既有 Completion DTO／API 契約維持原樣。

## 既有計算位置

- `QuestionnaireService.CompletionAsync` 呼叫 `ListTasksAsync` 後，對所有 `IsRequired` 任務計數，沒有 assignmentRound 篩選；必要 CANCELLED 任務也在分母中。
- `EfQuestionnaireStore.ListTasksAsync` 只依 CaseId 篩選並按輪次排序，不排除取消任務。
- `QuestionnaireTask.AssignmentRound` 已存在；本批不改 Entity、索引、Migration、Snapshot 或 Seed。
- `CaseTasksCompletionResponse` 沒有輪次／取消／待決策欄位，不能直接將新結果塞入舊 DTO 而遺失語意。

## 新計算介面

`QuestionnaireRoundCompletionEvaluator.Evaluate(caseId, assignmentRound, tasks)` 明確要求非空 CaseId 與正整數輪次。呼叫端須先取得已授權 Case、指定輪次的**完整**任務快照，包含 PARENT／TEACHER、必要／非必要及取消任務；不可只傳某個 Grant 的部分任務。

輸入僅含 CaseId、AssignmentRound、TaskId、IsRequired、TaskStatus，不含答案或個資。混入其他 Case／輪次、空 TaskId、重複 TaskId、null 或異常狀態皆拋出 ArgumentException；不靜默丟棄異常資料，不自動去重。

輸出分開保存 Required／Optional 的 Submitted、Pending、InProgress、Cancelled 與 Total 數量。Decision：

| 狀態 | 意義 | IsComplete |
| --- | --- | --- |
| Complete | 有必要任務、沒有取消必要任務，且必要任務全部 SUBMITTED | true |
| Incomplete | 有必要任務、沒有取消必要任務，但仍有 PENDING／IN_PROGRESS | false |
| CancellationPolicyRequired | 存在取消的必要任務，等待業務規則 | null |
| NoRequiredTasks | 有任務但沒有必要任務，不推定個案流程完成 | null |
| EmptyRound | 本次輸入沒有任務，不推定資料庫輪次存在與否 | null |

這些輸出是計算結果，不是宣告目前有效輪次、輪次已封存或個案可轉移狀態。歷史輪次可重複指定計算，元件不修改輸入或刪除歷史資訊。不得以 `IsComplete != false` 把 null 當完成。

## 與 07 契約的關係

唯讀參考本機 `origin/feature/medical-backend` 的 `47c3fc5e3866d0c9e53e1fd8887e7ef8782d03f6`，未整合分支或修改 07 程式。

07 `Services/Workflow/RoundCompletionEvaluator` 同樣要求單一指定 Case／round、拒絕混合範圍與重複 ID，並以 CancellationPolicyRequired 表示取消必要任務待決策。本元件使用問卷 namespace／獨立名稱，避免未來整合時與 07 類別撞名；增加必要／非必要狀態明細及 EmptyRound。07 的 IncompleteRequiredTaskCount 包含所有未提交必要任務；本元件保留各狀態，取消數不直接轉成業務「不完成」判定。

後續應共同決定採用哪個共用計算元件或加 adapter，避免保留兩套獨立演進的業務邏輯。不可直接以 bool 映射所有狀態：NoRequiredTasks／EmptyRound／取消必要任務皆應保留未決語意。

## 測試規則分類

已確認的技術規則：明確輪次、必要任務全部提交、部分提交、非必要任務不阻擋必要任務完成、不同輪次互不影響、歷史輸入不變、拒絕重複及異常資料。

待確認情境的測試：取消必要任務、沒有必要任務、指定輪次沒有任務。測試只確認計數及明確未決狀態，不斷言醫院應將這些情況視為完成／不完成，不決定 API 應回 404 或其他狀態。

```powershell
dotnet run --project tests/QuestionnaireRoundCompletion/QuestionnaireRoundCompletionHarness.csproj --no-launch-profile
```

這是直接呼叫純計算元件的記憶體測試，沒有 Fake Store、ADO 攔截或真正 MySQL。既有 Phase 2-2 使用 Fake Store；RBAC 部分測試使用 ADO 攔截器，均不代表真正 MySQL 交易驗證。

新專案位於獨立子目錄，使用自己的 obj／bin，避免與既有 tests 根目錄 Harness 共用建置產物。

本批驗收：API Build 成功；RBAC 34＋22＋19＋43＝118 項、Phase 2-2 158 項、新輪次計算 37 項全通過，另有 Phase 2-1 回歸通過。最後以 `dotnet build tests/QuestionnairePhase22Harness.csproj --no-restore --no-incremental` 強制重建，再直接執行產出的 DLL，確認執行的是 Phase 2-2 而非 tests 根目錄共用 obj 的其他 Harness。新輪次 Harness 也直接執行其獨立 DLL 重驗通過。套件還原／測試建置曾出現 NU1900（無法取得 NuGet 弱點資料），未造成建置或測試失敗；不代表已完成套件弱點查核。

## 待醫院／07 確認與後續接入

1. 有效輪次的可信來源、建立／封存時機，及封存後新增任務規則。
2. 必要任務取消後是否補派、豁免或重開輪次；本批不選擇政策。
3. 無必要任務、空輪次是否具備業務完成意義，及不存在輪次的正式 API 契約。
4. 07 的 CaseStatus 協調器如何在共用交易中鎖定並重查整輪任務，防止並行指派／取消造成過早完成。

獲准接入時，先由可信後端選定輪次並完成授權，查回該輪完整任務後轉成 QuestionnaireRoundTask，再呼叫本元件。若要對外提供新輪次 API，另行核准 DTO／路由；本批不替換現有 Completion API，也不自動選最大輪次。只有計算 Complete 仍不足以單獨觸發 FORM_COMPLETED，須另滿足 07 的有效輪次、封存與狀態轉移契約。
