# 16-10／16-12／16-13、16-06：驗收案例草案

本表未執行。A＝共同 Schema 固定前能準備輸入／oracle／離線替身案例；B＝需共同 Schema、真 Adapter 及核准隔離 MySQL 才能驗證；C＝另需 07／11 正式 Controller／認證／政策。所有案例現在都可設計，不把 A 的離線證據當 B／C 通過。

共通證據欄：case ID、固定 SHA／模型與 Migration IDs、fixture alias、預期／實際 HTTP code、前後 row counts／hash、revision／responseId、兩連線交易時間線、SQL error number、commit／rollback、退出碼及 PASS／FAIL／BLOCKED。故障證據須遮蔽秘密。直接 SQL 約束測試與 Gateway 錯誤映射分開記錄。

| ID | 前置與步驟 | 預期與不得發生 | 層級／依賴 |
|---|---|---|---|
| DB01 保存重讀 | P1、有效 A1 Task；讀無草稿，expectedRevision=0 保存部分答案；新 scope 重讀 | 0→1，IN_PROGRESS；答案一致；無 Response | A oracle；B 等 11 模型 |
| DB02 重啟 | DB01 後正常停啟獨立程序；提交後再做一次 | 草稿 revision／內容及收據不變；同原 key replay 相同 responseId | B、另核准啟停 |
| RV01 舊版 | 保存 rev1 後用 expectedRevision=0 保存不同內容 | 409 DRAFT_CONFLICT；原 rev1 hash 不變 | A／B |
| RV02 首次併發 | 兩獨立連線 barrier，同 Task expectedRevision=0 保存 | 一筆成功、一筆衝突；僅一 Draft、rev1；記錄實際鎖等待 | A 模型；B 真鎖 |
| RV03 邊界 | 缺 revision、ulong.MaxValue、跨語言大整數序列化 | 缺值400；overflow409、不環繞；前端不得丟精度 | A；C HTTP binding |
| ID01 同 key | 提交後用原作者／Task／正規化等價 payload／key 重送 | 原 responseId，replayed=true，只有一 Response；仍驗授權 | A／B |
| ID02 key 衝突 | 同作者同 key 改答案或換 Task | 409 IDEMPOTENCY_CONFLICT；不變更原答案／Task | A／B |
| ID03 作者範圍 | 不同作者不同 Task 使用相同 key | 家長唯一範圍為作者＋key，不能全域誤衝突；非作者不得讀既有 Task | A／B、作者增量 |
| ID04 併發提交 | 同 key 同 Task 6 個新 scope barrier；另測不同 key 同 Task | 最終一 Response／一次提交；成功及安全衝突可並存，原 key 重讀收斂；不能要求每次首輪都200 | A oracle；B |
| UK01 唯一鍵 | 直接 SQL 嘗試相同 Task Draft／Response、重複 assignment、作者＋key、Grant＋key | 核定索引拒絕1062；家長 NULL grant 不得替代作者唯一鍵；不測壞正常資料 | B、Schema |
| FK01 FK | 不存在 Case／Version／作者；Task-Version 不符、Grant-Task 越界；刪被參照 User | INSERT 1452／Restrict delete 1451（以實際 SQL 記錄）；無孤兒；Constraint 名與核定 SQL 對照 | B、07／11 FK |
| TX01 原子性 | Response INSERT 後、Task UPDATE 前／後、commit 前分別注入失敗 | 全部 rollback；無 Response，Task原狀；tracker清理；新 scope 可原 key 重試 | A；B 受控注入 |
| TX02 commit未知 | commit已送出、回應前中斷；新 scope 原 key 查回 | 收斂同 responseId或安全重試；不先斷言已rollback、不重複建立 | B、需核准故障工具 |
| TX03 撤銷競爭 | Adapter鎖後暫停，另一連線撤綁／撤同意；再交換先後 | 同鎖協定序列化：撤銷先提交則拒絕；提交先完成則保留答案、後續依撤銷政策拒絕；不能用快取放行 | B、11 政策與 writers |
| TX04 deadlock | 依正式鎖順序壓測；受控製造timeout／deadlock | 記錄1213／1205；交易終止、新scope重試；無半套提交；不假定timeout自動全交易rollback | B |
| ST01 不一致 | 隔離故障 fixture：Task SUBMITTED無Response、反向、時間不符、Draft錯Version | 本人503 STORAGE_INCONSISTENT、不自修；非作者先404，不洩漏狀態 | A；B 可建不一致程度受FK限制 |
| ST02 提交不可改 | 提交後讀寫Draft、新key提交、直接runtime UPDATE Response | Gateway409 TASK_SUBMITTED；DB權限拒絕原答案修改；receipt無答案 | A／B／C |
| BK01 備份還原 | 完成DB01／ID01，產dump及hash；匯入restorecheck | schema/history、筆數/hash、revision、receipt一致；原庫不變；以還原庫重讀驗證 | B、另核准 |
| BK02 壞檔復原 | 複製備份後截斷／變更副本；以hash檢查 | 匯入前拒絕；留原備份／原庫；不自動刪庫 | A hash規劃；B 演練 |
| AZ01 身分 | 無登入、多已驗identity、重複／空userId、body假authorId | Gateway401且不開Store；body不能取得權限 | A；C 正式認證 |
| AZ02 Scope | 跨院／Case／Task、停用帳號、撤綁、無同意／錯用途 | 家長404、不洩漏；Permission成功仍不能繞scope | A替身；B真Adapter；C |
| AZ03 同Case異作者 | P2有有效綁定，但Draft／Response作者P1或NULL歷史作者 | 404；不能接管、改寫或讀原答案 | A／B |
| AZ04 醫療權限 | MEDICAL_STAFF呼叫現有QuestionnairesController；另測Permission與scope四組合 | 現況403；正式接入須兩者true；未核准前不改成允許 | A靜態；C HTTP |
| AZ05 Policy防覆寫 | 同名普通Policy、錯大小寫Permission、較晚PostConfigure覆寫、額外scheme | 共用讀取註冊最終驗證拒絕；不得吞錯fallback | A，既有測試不重做 |
| AZ06 故障拒絕 | 權限查詢DB／timeout、取消request | 不放行；查詢故障安全503，取消不誤包成成功／一般403 | A；C middleware |
| AZ07 教師隔離 | Grant外Task、PARENT Task、撤銷／USED後新提交 | 拒絕；全部指定Tasks完成才USED，無Grant期限假設；Session期限與Grant分開 | A；B／C需11 |
| AZ08 家長教師狀態 | 綁定家長查教師完成狀態並遞迴檢查JSON欄位 | 僅taskId、questionnaireTitle、assignmentRound、isCompleted白名單候選；無答案、分數、判讀、觀察、Draft、內部hash/key；不能靠UI隱藏 | A oracle；C DTO需11確認 |
| AZ09 輪次／Case狀態 | round1完整、round2未完整、取消必要／空輪次 | 舊跨輪摘要不代表有效輪完成；不自動改CaseStatus／寫History | A evaluator；C需07決策 |

不預設未知 HTTP 成功 status：Gateway 回傳值不是 Controller 的路由／狀態契約。每案失敗保留隔離現場；cleanup 以新 fixture／另准重建為主，禁止在未核准共享資料上 DELETE／DROP。未執行案例一律留 NOT_RUN，不可由歷史 Fake PASS 回填。
