# 第二階段規則確認與剩餘介接事項

2026-10-13，feature/case-guardian-management。以下依使用者最新決定更新。

## 已確認及實作

| 項目 | 規則 |
| --- | --- |
| 個案資料 | 身分證字號 nationalId、地址 address 必填；孩子姓名、出生日期、性別沿用原驗證 |
| 照顧者資料 | Email 必填；姓名、手機、Email 可以作為同一照顧者的共用聯絡資料；孩子地址目前保存於 Case |
| 主要聯絡人 | 每名孩子只有一位有效主要聯絡人；資料庫 computed active_case_id 的唯一索引防止並行新增第二位 |
| 共用與權限 | 共用 Guardian 不代表自動取得另一名孩子權限；每個 CaseGuardian 需自己的邀請碼完成 bound_at_utc |
| 綁定邀請 | 新邀請 expires_at_utc=NULL，不限時；仍一次性、指定手機、可撤銷。遺失或撤銷後可以重新建立，不因到期補發 |
| 問卷摘要 | 統計全部輪次，非只看最大 assignmentRound |
| migration | 16 review 最終模型後統一處理，不由 11 另產 migration |

未新增家長修改孩子身分證、地址或聯絡 Email 的權限；修改維持原規則（醫療管理核心／联絡資料，家長修改本人聯絡姓名），其他可修改欄位仍須明確決定。前端 address/email 已加入必填標記；7 的儲存 API 串接仍由 7 處理。

## 16 最新提供的介接資訊

目前網址 /parent-questionnaire.html，task query 傳遞 taskId；但仍 mode=demo／SharedDemo／localStorage。因此不能把正式任務導向該 demo 路徑。

待 16 確認正式模式、taskId、送出／返回首頁路徑後，11 再連上填表按鈕。既有 Questionnaire／Draft／Submit／Response 不另建一套。

## 已由使用者確認的答案政策

1. 家長能否讀取自己已提交的原始答案？可讀取本人答案，提交後不可修改。教師答案仍僅完成狀態。
2. 綁定撤銷後是否立即停止家長讀取草稿／已提交紀錄？保留資料及歷史、停止家長存取，重新核准並綁定後恢復。不得刪除原始醫療紀錄來達成撤銷。

上述兩項使用者已確認。由 16 依可信提交者識別實作讀取 API。

提醒 16：現在 QuestionnaireResponse 有 SubmittedByGrantId，但沒有 Parent 提交人的 userId。若規則是「只看本人提交」，需要在可信後端交易內記錄提交人（例如 SubmittedByUserId 或經確認的對應關係），不能依 respondentName 或目前 Case 的 Guardian 推定答案作者。

## 仍需合作的項目

- 7：gender → sex 欄位轉換、medical 必填欄位傳到 Case／Guardian API；確認地址是孩子個案地址，還是需要跟照顧者共用的地址。
- 16：新增 email、national_id、address、bound_at_utc、active_case_id generated column／unique index，加上三張新表整體review；舊 Case 資料補齊與院所回填。不要以虛構身分證或手機驗證時間填正式舊資料。
- 16：串接 CaseAccessService、家長 Draft／Submit／答案作者；正式填表模式與回首頁路徑。
- 三人：換手機流程、家長首次登入院所歸屬；目前非自助換手機。正式 OTP／SMS 依原決定延期；雲端連線已完成，schema 套用等待 16。


答案政策補充：家長可讀取本人已提交原始答案，不可修改；教師仍只完成狀態。撤銷綁定保留原紀錄但立即禁止存取；醫護重新核准關係仍不自動放行，必須重新完成該個案邀請綁定。

新增 CanReadParentTaskAsync(User,taskId,ct)：家長 PARENT 任務閱讀 scope，包含 SUBMITTED；與 CanWriteParentTaskAsync 分開。此方法不等於 Response 作者檢查，16 的答案 API 還必須對照可信的提交人 userId。若重新綁定的是不同帳號，不可將舊家長答案當作新家長本人答案開放。
