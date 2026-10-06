# Smart-Early-Intervention-Care-System
#413S570165鄧靖霖
#413570115洪靜吟
# 早療智慧動態照護系統

本專題為早期療育流程之資訊系統開發專題。

目前早療聯合評估流程中，個案資料與評估表單多以紙本方式處理，
個管師需人工追蹤個案聯絡、預約及表單填寫進度。

本系統希望將早療個案管理與評估表單電子化，
讓家長及幼兒園教師可於到院前完成資料填寫，
並讓個管師與醫療人員能透過後台掌握個案進度與評估資料。

---

## 本學期開發範圍

### 1. 使用者登入與權限管理
- 帳號密碼登入
- 使用者角色管理
- 角色權限控管
- API 存取權限檢查

### 2. 早療個案管理
- 新增早療個案
- 兒童基本資料
- 家長／聯絡人資料
- 個案列表
- 個案詳細資料

### 3. 個案狀態與進度管理
系統需記錄：
- 待聯絡
- 已預約
- 待填表
- 已完成
- 聯絡狀況
- 預約狀態
- 表單填寫進度
- 備註
- 待辦事項

### 4. 智慧動態儀表板
集中呈現：
- 個案目前狀態
- 待處理事項
- 表單完成狀況
- 個案數量統計

### 5. 早療表單管理
- 將紙本量表電子化
- 依個案需求指派表單
- 支援一位個案多張表單
- 查看填寫進度
- 自動計分
- Excel 匯出

目前規劃表單：
- 早療評估個案基本資料
- SNAP-IV
- 克氏行為量表
- 家長滿意度調查
- 學校行為觀察表

### 6. 教師動態授權
- 家長授權教師填寫
- 產生具時效性的填寫 Token / URL / QR Code
- 教師只能填寫指定個案的指定表單
- 填寫完成或逾期後 Token 失效

---

## 本學期暫不實作

以下屬於後續延伸功能，本階段不要主動實作：

- SMART on FHIR 完整整合
- HIS / EMR 串接
- FHIR Server 正式部署
- SMART App Launch
- 真實院內 API 串接

---

## 第一階段開發分工

### A - Authentication
負責：
- Login
- Logout
- Password 驗證
- Password Hash
- JWT / Session
- OTP

不負責：
- Role / Permission 規則
- 教師動態 Token

### B - Authorization
負責：
- Role
- Permission
- RolePermission
- API Authorization
- API Guard
- 403 Forbidden 權限判斷

不負責：
- Login UI
- 密碼驗證
- JWT 產生
- OTP
- 教師邀請 Token
- QR Code

### C - Teacher Dynamic Authorization
負責：
- Teacher Token
- Token expiration
- Token revoke
- QR Code
- 教師指定表單存取
- Audit Log 主要架構

不負責：
- 一般帳號密碼登入
- Role / Permission 核心判斷

---

## AI / Codex 開發規則

在修改本專案前，請先閱讀本 README。

### 開發原則
1. 不要自行擴大功能範圍。
2. 不要在未確認前新增新的角色、資料表或主要業務流程。
3. 優先沿用既有專案架構與命名。
4. 修改共用資料模型前，應先說明會影響哪些模組。
5. 不要把 Authentication、Authorization 與 Teacher Token 合併成同一模組。
6. 不要因為某功能尚未完成，就自行替其他組員完成。
7. 如果需求不明確，應先提出問題，而不是自行假設。
8. SMART on FHIR 為後續延伸功能，本階段不要主動實作。

---

## 目前工作分支：Authorization

目前主要開發目標為 B 模組 Authorization。

允許修改：
- Role
- Permission
- RolePermission
- Authorization Service
- API Authorization / Guard
- Authorization-related tests

除非明確要求，請勿修改：
- Login
- Password
- JWT generation
- OTP
- Teacher Token
- QR Code
- Case CRUD
- Questionnaire CRUD
- Dashboard

---

## 初步資料關係

```text
User
 │
 ├── Role
 │    └── Permission
 │
 └── Case
      │
      ├── Guardian
      ├── CaseStatus
      └── CaseQuestionnaire
             │
             ├── Questionnaire
             └── QuestionnaireResponse