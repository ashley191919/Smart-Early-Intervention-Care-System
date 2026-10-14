# 11 → 7：Case／Guardian API 介接契約

日期：2026-10-15。分支：feature/case-guardian-management；基底 origin/main a68ed6d。

本輪沿用現有個案前端，不重做醫療畫面。此契約是依 repo 中 7 的欄位比對結果，尚待 7 確認接線；不是已由 7 驗收的正式公告。

## 欄位對照

| 7 現有表單 | API 欄位／值 |
| --- | --- |
| childName | childName，必填，1–100 字元 |
| birthDate | birthDate，yyyy-MM-dd，不接受未來日期 |
| gender: male/female/unspecified | sex: MALE/FEMALE/UNKNOWN；UI 必須轉換 |
| nationalId | nationalId，必填，國民身分證格式與檢查碼；居留證另行訂契約 |
| address | address，必填，1–300 字元；目前是孩子個案地址 |
| contactName | Guardian displayName，必填，1–100 字元 |
| phone | Guardian phone，台灣手機，後端正規化 09…／+886… |
| email | Guardian email，必填，Email 格式、最多254字元 |
| relationship | 大寫 FATHER/MOTHER/GRANDPARENT/RELATIVE/GUARDIAN/CAREGIVER/OTHER |
| 主要聯絡人 | isPrimaryContact，預設 true；其他照顧者傳 false |

CaseId、CaseCode、OrganizationId、NEW 狀態及建立/修改 UTC 時間由後端決定，不讓表單自訂。修改 Case/Guardian 必須携帶最新 revision；409 後重新載入，不能直接重送舊資料。

## 建立流程

1. POST /api/cases 建立兒童基本資料，取得 caseId。
2. POST /api/cases/{caseId}/guardians 建立新聯絡人。
3. 若 CONTACT_ALREADY_EXISTS，GET /api/guardians 分頁選既有聯絡人，再 POST /api/cases/{caseId}/guardians/{guardianId}/link。
4. 聯絡人建立失敗不會自動刪除已建立 Case；UI 保存 caseId，重試聯絡人步驟，不重新 POST Case。
5. 邀請與家長綁定屬後續流程；醫護建立聯絡資料不會自動替家長取得權限。

## Case endpoints

- POST /api/cases → 201 + CaseDto。
- GET /api/cases?page=1&pageSize=20 → 本人院所、可讀個案的陣列；pageSize 最大100。
- GET /api/cases/{caseId} → 200 + CaseDto。
- PUT /api/cases/{caseId} → 更新基本資料、回新 revision，不更改 CaseStatus。

GET/PUT 其他院所或不存在 Case 回404；無登入401、非醫療建立/修改403、驗證失敗400、舊 revision409、DB尚未就緒503。資料庫唯一鍵競爭回 DATA_CONFLICT/409。

CaseDto：caseId、caseCode、childName、birthDate、sex、nationalId、address、caseStatus、revision、createdAtUtc、updatedAtUtc。

## Guardian endpoints

- GET/POST /api/cases/{caseId}/guardians。
- PUT /api/cases/{caseId}/guardians/{guardianId}：修改共用聯絡資料及該孩子的關係，需 revision。
- GET /api/guardians?page=1&pageSize=20：本院所聯絡清單。
- POST /api/cases/{caseId}/guardians/{guardianId}/link：body relationship、isPrimaryContact。
- POST /api/cases/{caseId}/guardians/{guardianId}/revoke：停止家長存取，保留既有答案、草稿與歷史。

最多一位有效主要聯絡人，其他照顧者可以並存。共用 Guardian 的姓名/手機/Email 會影響其關聯孩子，關係及主要聯絡人標記則是逐個案保存。已綁定手機不可直接修改；其他欄位最終政策見團隊決定文件。

## 分工及驗收

7：串接既有醫療畫面、Workflow/CaseStatus；11：Case/Guardian 核心與 scope；16：填答模組與唯一 migration 出口。

依既有模型暫採醫療角色同院所範圍；更細 assigned case/科別规则需由 7 確認。前端醫療展示登入不會取得正式 JWT；正式 API 必須有效 JWT 並重查帳號/院所/角色，不能拿 DEMO 密碼或前端自訂 userId 呼叫。

此分支模型與既有4份migration不同步是預期交接狀態，不可直接對舊 DB 啟動驗收。先由16 review/產生migration與舊資料回填計畫，再做真實MySQL、JWT HTTP整合驗收。
