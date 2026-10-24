# K1：Case／Guardian 固定契約（交接 07）

日期：2026-10-22。依《專題開會紀錄檔-分工_1020》V5.3 與使用者正式開發指示。
基準 HEAD：`0e4e169` 加本輪未提交修改；不是已推送的交接 Commit。

## 單一所有權

- 11 維護 `Controllers/CasesController.cs`、`Services/Cases/CaseGuardianService.cs`、`CaseAccessService.cs`、`DTOs/Cases/CaseContracts.cs`、Case／Guardian Entity 及 Configuration。
- 07 呼叫既有 API；不複製 Case CRUD。CaseStatus／History 維持獨立後續範圍。
- 16 統整正式 DbContext／Migration／Snapshot。本輪未修改這些共同檔案。
- 以下檔案路徑均相對 `EarlyInterventionCare.Api/`。

## Case API

| 方法 | 路徑 | 成功 |
| --- | --- | --- |
| GET | `/api/cases?page=1&pageSize=20` | 200，`CaseDto[]`，空資料 `[]` |
| GET | `/api/cases/{caseId}` | 200，CaseDto |
| POST | `/api/cases` | 201，CaseDto 與 Location |
| PUT | `/api/cases/{caseId}` | 200，更新後 CaseDto |

沒有 DELETE；PUT 不修改 CaseStatus／院所。CaseId、OrganizationId 由後端可信資料決定，不接受前端挑選院所。

以下全部為合成契約範例，不能作真實病患 Seed：

```json
{
  "childName": "虛構孩子",
  "nationalId": "A123456789",
  "address": "隔離測試地址",
  "birthDate": "2022-01-01",
  "sex": "UNKNOWN"
}
```

PUT 同上述欄位並帶 `"revision": 0`。回傳：

```json
{
  "caseId": "11111111-1111-4111-8111-111111111111",
  "caseCode": "CASE-EXAMPLE",
  "childName": "虛構孩子",
  "birthDate": "2022-01-01",
  "sex": "UNKNOWN",
  "nationalId": "A123456789",
  "address": "隔離測試地址",
  "caseStatus": "NEW",
  "revision": 0,
  "createdAtUtc": "2026-10-22T00:00:00Z",
  "updatedAtUtc": "2026-10-22T00:00:00Z"
}
```

`OrganizationId` 是 Entity／授權內部契約，目前 CaseDto 不對外增加該欄位。`revision` 為 uint 並發版本；不要改成時間戳。列表 page 1–100000、pageSize 1–100。

## Guardian／Binding／家長

| 方法／路徑 | 主要輸入／回傳 |
| --- | --- |
| GET／POST `/api/cases/{caseId}/guardians` | GuardianDto[]／GuardianInput |
| PUT `/api/cases/{caseId}/guardians/{guardianId}` | GuardianInput＋revision；204 |
| POST `.../{guardianId}/link` | relationship、isPrimaryContact；GuardianDto |
| POST `.../{guardianId}/invitations` | 回 invitationId、invitationCode、expiresAtUtc |
| POST `.../{guardianId}/revoke` | 撤銷關係；204 |
| POST `/api/parent-binding-invitations/{invitationId}/revoke` | 撤銷未用邀請；204 |
| GET `/api/guardians` | 同院所聯絡人 |
| GET `/api/parent/cases` | ParentCaseDto[]，增加 relationships |
| POST `/api/parent/bindings` | `{ "invitationCode": "醫護提供的碼" }` |
| GET／PUT `/api/parent/profile[/{guardianId}]` | 本人聯絡資料／修改 displayName＋revision |
| GET `/api/parent/cases/{caseId}/tasks` | CaseTaskResponse[]；僅 PARENT、非 CANCELLED |

GuardianInput：displayName、email、phone、relationship、isPrimaryContact。關係碼：FATHER／MOTHER／GRANDPARENT／RELATIVE／GUARDIAN／CAREGIVER／OTHER。

- GuardianId 為聯絡人主鍵；CaseGuardian 以 CaseId＋GuardianId 關聯。
- 多照顧者可存在，最多一位 ACTIVE 主要聯絡人。
- 已綁定、ACTIVE、可信已驗證 Email、同院所、有效帳號／院所皆須符合。
- 邀請只回傳建立時的明碼，DB 保存雜湊；一次性、可撤銷。既有家長綁定邀請期限不因教師新期限需求而變動。
- 本輪保留 teacher-completion 後端，但家長 UI 已移除，不作 V5.3 展示入口。

## 錯誤語意

| HTTP | 代表 |
| --- | --- |
| 400 | 輸入不合法、邀請無法使用等；含 ValidationProblem 的可能性 |
| 401 | 無效 JWT、目前帳號／院所停用、可信身分缺失 |
| 403 | 有身分但不允許該操作，或 CONSENT_REQUIRED／EMAIL_VERIFICATION_REQUIRED |
| 404 | 指定資源不存在或不在 scope；不洩漏別人的 Case／Task |
| 409 | REVISION_CONFLICT、DATA_CONFLICT、已連結／主要聯絡人等衝突 |
| 503 | DB 不可用、同意未核定／未配置；不得補虛構資料 |

既有 Case 錯誤 code／message 保留；V5.3 部分回應增加 traceId。JWT Middleware 可能只有狀態無 JSON，07 Client 必須處理。

## Consent 與既有 Case 建立的界線

正式首次資料蒐集順序尚待院方確認。本期只使用事先建立、已綁定的虛構個案。沒有刪除既有 Case 必填欄位，也沒有新增未同意的家長建案功能。

V5.3 會保護家長舊的 `/api/cases`、Profile、Task、Definition 等旁路；醫療角色不因家長 Consent gate 被開放或阻擋。現有同院所醫療檢查仍需與共用 RBAC／07 的醫療授權契約整合，不能視為最終個案指派規則。
