# ParentEligibilityEvidence 單一共用候選契約

16 基準：`cdf7792b3e8dd6d9880d65a7f6b0797cb4326fa6`。已唯讀取得 11 固定版本 `7396610c22062bec112b3936c753931ac36f7a68` 的 [Context／Stamp 草案](https://github.com/ashley191919/Smart-Early-Intervention-Care-System/blob/7396610c22062bec112b3936c753931ac36f7a68/docs/parent-context-stamp-contract-11-20261104.md)，blob `629d8ea04944c8df45497cb38700e31827333e8d`。11 文件中的三世代及內部 Evidence 承載仍為草案，六個 ABA 欄位依本批指定候選型別提供；不是已核定 Schema。

## 唯一型別及檢查範圍

命名空間：`EarlyInterventionCare.Api.Services.Questionnaires`。

- [ParentEligibilityEvidence.cs](../../EarlyInterventionCare.Api/Services/Questionnaires/ParentEligibilityEvidence.cs)：唯一的 `ParentEligibilityEvidence`、`ParentEligibilityAbaEvidence` 宣告，精確沿用本批指定欄位與 nullable 型別。
- [ParentEligibilityEvidenceChecks.cs](../../EarlyInterventionCare.Api/Services/Questionnaires/ParentEligibilityEvidenceChecks.cs)：純 `ValidateStructure`、`FindMissingCandidateFields`，不編碼、不查資料、不授權、不補值、不正規化。

候選識別常數為 `parent-eligibility/v1`。它只表示本批可識別的候選格式，不代表已發布可啟用的安全協定。

| 層次 | 本批能證明什麼 | 不能證明什麼 |
|---|---|---|
| 契約資料結構有效 | ValidateStructure 檢查 exact ordinal 候選版本、非空 TaskId／QuestionnaireVersionId | 持久來源、Scope、文件 Hash 正確性或資格 |
| 候選欄位是否缺少 | FindMissingCandidateFields 列出未知世代、缺少 ABA／文字／Stamp | 即使清單為空，也不能證明資格證據完整可信、Stamp 有效或未發生 ABA |
| 交易內已完成授權 | 本批不提供此結果；沒有 IsAuthorized／IsEligible 等旗標 | 必須由未來正式 Adapter 以可信 Principal、資料庫現況、鎖序及共同協定完成 |

Records 可承載未知／不合法的候選輸入以供檢查，建構子不會自動執行 ValidateStructure。未來接入者必須明確檢查；不能把成功建構、record 相等或缺欄位清單當作授權。這些純 helper 尚未被任何 Gateway 使用。

## NULL 規則及來源

| 欄位 | 型別與來源限制 |
|---|---|
| ContractVersion | string；檢查時拒絕 NULL、空白、未知版本、大小寫差異與尾端空白，不 Trim |
| TaskId／QuestionnaireVersionId | Guid；不得 Guid.Empty，未來必須由可信伺服器 Task 關聯取得 |
| BindingGeneration | ulong?；CaseGuardian 持久資格世代來源尚未交付，未知必須 NULL |
| ParentEligibilityEpoch | ulong?；院所級持久資格來源待定，不因單一撤綁任意增加 |
| DocumentEligibilityEpoch | ulong?；文件內容／Scope／有效性持久來源待定 |
| DocumentContentHash／ScopeCode／ScopeKey | string?；由後端文件／有效 Consent 來源取得；本批只檢查是否缺少，不自行核定本文、Scope 或格式 |
| Aba | ParentEligibilityAbaEvidence?；NULL 與存在但六欄皆 NULL 不同；兩者均不能證明 ABA 已處理 |
| 六個 ABA Generation | 全為 ulong?；User／Authorization／Case／Guardian／Task／Consent 的持久版本及所有寫入端仍待共同固定 |
| EligibilityStamp | string?；本批不產生；測試只使用 NULL／空字串，兩者資料上不同且都列為缺少 |

未提供任何預設世代。不將 NULL 變成 0／1、目前時間、GuardianContact.Revision、Draft Revision 或其他替代值。測試中的 0／1／整數邊界是明確指定的合成資料值，只驗證 nullable／ulong 不失真，不是持久世代初始化或資格證據。

不包含 Draft Revision、UpdatedAt、TaskStatus、SubmittedAt、答案 Hash。一般 Save／Submit 不應憑自身暫態變更改變 Evidence；但這不免除交易內的 Task 狀態檢查。

## 11 未來引用方式（本批未實作）

11 的後端 Context 引用上述同一型別，不複製成另一份 record，也不引用測試 pinned DTO。可在共同核准後加入以下屬性：

```csharp
[System.Text.Json.Serialization.JsonIgnore]
public ParentEligibilityEvidence? Evidence { get; init; }
```

Context／Evidence 必須由後端以原始可信 Principal 產生，不能作為 HTTP body、JWT claim 或瀏覽器保存資料接收。JsonIgnore 只是序列化防線，不替代 Controller 輸入邊界及真正授權。原四方法 Gateway、HTTP DTO、16 ParentGatewayCallContext／ParentAccessExpectation 本輪全部不改，也沒有 Context → Expectation 轉換或 Bridge 接線。

## Stamp 編碼候選規格（僅設計，未實作／未固定 golden vectors）

1. 固定欄位順序候選：ContractVersion → Context 的 Actor／Organization／Case／Guardian／Consent／Document IDs → TaskId／QuestionnaireVersionId → 三個世代 → 文件 Hash／ScopeCode／ScopeKey → ABA 容器與六世代。Context IDs 不在 Evidence 中重複定義，未來 encoder 需取得共同核准的伺服器 snapshot。
2. 嚴格 UTF-8、無 BOM；拒絕無效 UTF-16，不 Trim、不 case-fold、不自行 Unicode normalization。
3. 字串以四位元組 unsigned big-endian 長度界定；候選 NULL 標記 FFFFFFFF，空字串 00000000；保留標記不得當合法長度。
4. Guid 候選採小寫 D 格式的 36 個 ASCII bytes，不用平台／.NET Guid.ToByteArray 的混合位元組序。
5. nullable ulong 候選為 NULL=00；有值=01 加八位元組 unsigned big-endian。未知值不補零。ABA 容器同樣使用存在標記，再依固定順序編碼六欄。
6. ContractVersion 必須進入 domain separation；EligibilityStamp 本身、Principal 物件、答案 Hash 及前述暫態欄位不進編碼。
7. 文件 Hash 的 exact 格式、其他文字限制、長度上限、Stamp 前綴／Digest、Context snapshot 型別及完整 ABA 來源需雙方核定；完成前不提供 encoder 或可用 Stamp。

下列只有候選測試案例，**本輪未執行任何編碼／Digest 測試**：

| 候選案例 | 未來預期 |
|---|---|
| NULL／空字串 | 原始 bytes 不同；必要欄位缺少均拒絕 |
| 相鄰欄位 ab+c／a+bc | 長度界定後 bytes 不同 |
| é／e 加組合重音 | 不自行合併；bytes 不同 |
| 無效 surrogate | 嚴格拒絕，不以替代字元折疊 |
| 同資料、不同 Culture | bytes／Digest 相同 |
| NULL ABA／六欄未知 ABA | 形狀不同，但均不可建立可用 Stamp |
| 世代 NULL／明確 0／ulong.MaxValue | 存在標記及整數 bytes 不同；0 合法性另待初始化政策核准 |
| 未知版本／缺必要持久來源 | 拒絕，不降版、不補常數 |
| 綁定撤銷再恢復 | 由核准持久世代辨識 ABA，不以最後狀態相同即放行 |

## 離線執行

```powershell
dotnet restore tests/ParentEligibilityEvidence/ParentEligibilityEvidenceHarness.csproj --source tests/ParentEligibilityEvidence -p:NuGetAudit=false
dotnet build tests/ParentEligibilityEvidence/ParentEligibilityEvidenceHarness.csproj --no-restore
dotnet tests/ParentEligibilityEvidence/bin/Debug/net8.0/ParentEligibilityEvidenceHarness.dll
```

最小依賴：無套件、無 API ProjectReference，只連結兩個正式共用來源檔，不重新宣告 Evidence。restore 限本地 source／快取，缺依賴則停止。不同 assembly 各自編譯同一來源只供離線測試，不是第二份契約定義，也不會將測試 assembly／pinned DTO 編入 API。

本輪 **34 PASS、0 FAIL、2 BLOCKED，exit 2**；編譯 0 警告、0 錯誤。測試涵蓋必要 IDs／版本、NULL 與明確整數、NULL／空字串、with 複製全欄保留、每個未知 ABA 欄位、Culture、不產生 Stamp／授權結果。這不是完整資格驗證，也不是正式前後端或資料庫驗收。

API 隨既有 ParentSubmitReadiness 建置成功（0 警告、0 錯誤）；核心回歸仍為 143 PASS、0 FAIL、3 BLOCKED，exit 2。教師完整套件仍在 `QuestionnairePhase22Harness.cs:367` 的 `no pending schema/model changes` FAIL／exit 1；舊家長套件仍在 `ParentQuestionnaire/Program.cs:198` 的 `only six additive author schema operations proposed` FAIL／exit 1，兩者 gate 後續案例 BLOCKED。以既有白名單 regression runner 捕捉例外，未使用 proposal／skip 模式；舊專案編譯各保留 NU1900 警告，0 錯誤。

BLOCKED：ABA／世代持久來源、初始化與資格寫入端；Stamp encoder／golden bytes／Digest、來源可信性及同交易授權。本批未改任何模型、Migration／Snapshot 或 Schema gate；共同 Schema、11 Context／Adapter 與 07 Task 寫入端仍待共同整合。
