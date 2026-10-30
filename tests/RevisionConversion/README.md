# Revision 字串轉換離線測試

本元件是暫定嚴格格式的獨立實作，未接入 Controller、Gateway、DTO 或 DI，不改變授權、提交、去重或資料庫行為。

- `RevisionStringConverter.TryParse`：僅接受 ASCII `0` 或無前導零正整數，最大為 `ulong.MaxValue`。拒絕 NULL、空白、符號、Unicode 數字、前導零及 overflow；失敗回傳 false 且 out 值為 0，呼叫者必須檢查回傳值。
- `Format`：使用 InvariantCulture 輸出完整十進位字串，不經浮點數或 JavaScript Number。
- 11 固定版 `e2affb548bb427cd02d001d5bfd5761d7ecb4648` 的 `ParentModule/ParentQuestionnaireContracts.cs` 使用 string Revision／ExpectedRevision；`ParentControllers.cs` 的 Validate 使用 ASCII 數字及 NumberStyles.None，但接受 `00`、`01`。本元件刻意拒絕前導零，正式 API 保持原樣；接入前仍需 11／16 確認。
- 16 固定版 `87edb22208ab992db5e00774d85c51d4c510e946` 的 `DTOs/Questionnaires/QuestionnaireWorkflowContracts.cs` 使用 ulong Revision／nullable ulong ExpectedRevision。本元件不將 NULL 解釋成 0，也不修改核心型別。

在 Worktree 根目錄執行：

```powershell
dotnet run --project tests/RevisionConversion/RevisionConversionHarness.csproj
```

專案只使用 .NET 8 基礎類別庫，直接連結元件原始碼，沒有 PackageReference／ProjectReference，不啟動 API、不開 Socket、不接觸資料庫。RestoreSources 指向本測試目錄且停用 NuGet audit；需本機已有 SDK 與 net8.0 targeting pack，缺少時停止，不下載第三方依賴。既有 tests/Directory.Build.props 隔離中間檔；建置會產生忽略的 bin／obj 輸出。

案例涵蓋 UInt64 與 JavaScript 精度界線（含 2^53+1）、雙向轉換、無損往返、格式拒絕、overflow 及不同 CurrentCulture。PASS 僅代表獨立轉換元件的離線結果，不代表正式 HTTP、Gateway 或 MySQL 驗收。
