# Parent Author Metadata Reader 離線驗證與交接

16 基準：`bdf9ba57caedd0b35b2c1c0e5d2b9309bd56b677`。11 參考：`7396610c22062bec112b3936c753931ac36f7a68`，`ParentModule/ParentAuthorVisibility.cs`。本批未接入 11 wrapper、正式 DI 或 HTTP。

## 提供的契約

[ParentAuthorMetadataContracts.cs](../../EarlyInterventionCare.Api/Services/Questionnaires/ParentAuthorMetadataContracts.cs)：

```csharp
ParentAuthorRowMetadata(bool Exists, Guid? ParentAuthorUserId, bool HasTeacherGrant);
ParentTaskAuthorMetadata(ParentAuthorRowMetadata Draft, ParentAuthorRowMetadata Response);
Task<ParentTaskAuthorMetadata> IParentAuthorMetadataReader.ReadAsync(
    ApplicationDbContext db, Guid taskId, CancellationToken ct);
```

CLR 型別供後端模組共用，不是公開 HTTP DTO 或授權能力。Reader 不持有 actor、Guardian、Stamp 或其他 DbContext；不判定是否可以揭露、不建立資格證據。

## 真實欄位與查詢界線

[EfParentAuthorMetadataReader.cs](../../EarlyInterventionCare.Api/Services/Questionnaires/EfParentAuthorMetadataReader.cs) 對兩表依參數化 TaskId 各做一次 AsNoTracking 投影：

| 表 | 作者欄位 | 教師 Grant 判斷 |
|---|---|---|
| questionnaire_drafts | parent_author_user_id／ParentAuthorUserId | last_saved_by_grant_id／LastSavedByGrantId IS NOT NULL |
| questionnaire_responses | parent_author_user_id／ParentAuthorUserId | submitted_by_grant_id／SubmittedByGrantId IS NOT NULL |

Grant 欄位已存在於 Entity、`CoreModelConfiguration.cs` 與既有歷史 Migration／Snapshot；不用新增欄位或推定關聯。作者欄位存在於 16 Entity／Parent Mapping，**尚不代表目標資料庫已升級**。

只選取作者與 Grant 是否存在，不載入答案、姓名、PayloadHash、AnswersJson、Relationship、HashVersion 或完整 Entity。Exists 由資料列有無產生，與作者 NULL 明確區分。空 Guid 作者原樣保留，交給 wrapper 拒絕；空 Guid Grant 只要非 NULL，HasTeacherGrant 仍為 true。

Reader 不 Begin／Commit、不 SaveChanges、不呼叫 Store.OpenAsync、不 Clear ChangeTracker、不建立或處置其他 Context／現存交易。資料庫錯誤、重複列、取消都傳遞，不轉成不存在；兩次查詢任何一次失敗都不回傳部分結果。

兩次 SELECT 不是原子快照或列鎖，AsNoTracking 也不保證 MySQL current read。正式交易一致性仍依共同隔離／鎖序與全部寫入端協調，不可用本 Reader 替代。

## 11 最小接入步驟（另批整合）

1. 先完成可信 actor、Case／Task 範圍及必要綁定確認，再呼叫既有 `IParentAuthorVisibility.CanRevealAsync`；不得讓未授權前端直接以 TaskId 查作者。
2. wrapper 引用這份 Reader 契約，傳入原 scoped DbContext；禁止呼叫 Store.OpenAsync 或另開 Context／交易。
3. wrapper 同時判斷 Draft 與 Response：不存在的列只通過作者條件；任何既有列作者 NULL、Guid.Empty、不同 actor 或 HasTeacherGrant=true 均拒絕。兩列存在必須兩列符合。不推定目前 Guardian 為作者。
4. 查詢失敗保留失敗並安全中止，不 catch 成允許。作者不符沿既有遮蔽回 404。
5. 繼續 Consent／狀態等檢查；正式核心同交易重新授權與查作者。預檢結果不快取為授權，也不變成 EligibilityStamp。
6. 缺正式 Adapter、世代、Stamp、共同鎖序或核准 Schema 時，正式 Gateway／Bridge 仍拒絕。本批未改任何既有 guarded 方法。

授權歷史收據可接受未知 HashVersion 的規則不等於未知作者可以查閱：未知作者仍拒絕。本批不修改 Submit、Idempotency、教師 Hash 或保存行為。

## 離線執行與結果

從 worktree 根目錄：

```powershell
dotnet restore tests/ParentAuthorMetadata/ParentAuthorMetadataHarness.csproj --source tests/ParentAuthorMetadata -p:NuGetAudit=false
dotnet build tests/ParentAuthorMetadata/ParentAuthorMetadataHarness.csproj --no-restore
dotnet tests/ParentAuthorMetadata/bin/Debug/net8.0/ParentAuthorMetadataHarness.dll
```

只使用本地 source／套件快取，缺少套件時停止。共用既有 [OfflineAdo.cs](../EfParentStore/OfflineAdo.cs)，不修改該檔；連線替身沒有 Socket 能力，未攔截的命令會失敗。MySQL 8.0.46 僅作 Pomelo SQL 產生設定，不是實際伺服器。

本輪 **35 PASS、0 FAIL、2 BLOCKED**；編譯 0 警告、0 錯誤；有 BLOCKED 時退出碼 2，斷言失敗為 1。測試檢查實際生成 SQL 僅選取兩個允許欄位、TaskId 參數、NULL／空／其他作者、教師 Grant、混合 metadata、兩列作者一致性、兩表查詢失敗、重複列、取消與既有交易／追蹤資料保留。

Harness 的 `Visible` 是測試用判定規格，不是正式 11 wrapper、假授權 Adapter 或可用的作者放行入口。合成 metadata 僅由封閉 ADO 替身提供，不寫入任何資料庫，不推定真實資料的作者。

仍 BLOCKED：11 真 wrapper／資格／Stamp／撤銷並行整合；核准增量 Migration、真實 MySQL SQL／隔離／鎖定與舊庫相容驗收。未升級舊庫不得使用新版 EF 模型；缺欄位應傳遞錯誤，不可切換成猜作者或允許。

本輪回歸結果如下，不能以這個窄範圍 Harness 取代完整回歸：

| 套件 | 實際結果 |
|---|---|
| ParentSubmitReadiness | 143 PASS、0 FAIL、3 BLOCKED；exit 2；純核心／Fake |
| EfParentStore | 86 PASS；exit 0；EF／Pomelo 與替身 ADO，非 MySQL |
| QuestionnairePhase22Harness | FAIL／exit 1；第 367 行 `no pending schema/model changes`；gate 後續案例 BLOCKED |
| ParentQuestionnaireHarness | FAIL／exit 1；第 198 行 `only six additive author schema operations proposed`；gate 後續案例 BLOCKED |

舊套件由既有 ParentSubmitReadiness `--regression teacher|parent` 白名單入口完整執行並捕捉例外，保留失敗，不觸發互動式偵錯。未修改／略過 gate，未產生 Migration／Snapshot。這些舊套件與 EfParentStore 建置保留 NU1900 弱點資料來源警告，皆 0 編譯錯誤。
