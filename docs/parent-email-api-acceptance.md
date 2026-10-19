# 家長 Email 登入：API、前端與啟用驗收

日期：2026-10-19。分支 `feature/case-guardian-management`。核心模型保存基準 `c347ba3`；本文件隨後續 API／前端提交一起交接，取最新交接 Commit，不直接合 main。

## 已完成與目前限制

已接 Email 前端 → OTP request／verify → 可信 JWT → 正式 account 工作台，使用 User、CaseGuardian 及指定 Email 邀請。移除原登入頁的前端亂數 OTP 與假登入跳轉，所有家長登入連結均使用後端 API，維持相同 CSS／院徽／字體。

正式網站預設 `ParentEmailLogin:Enabled=false`，request／verify 回 503 `EMAIL_LOGIN_UNAVAILABLE`，且不寄信、不讀寫 User。這是尚未完成 Migration 的部署保護，不應把 503 當成 Gmail 密碼錯誤。API 的服務建構只建立 DbContext，不會在關閉時執行資料庫查詢。

資料模型／舊資料交接見 [Email 模型交接](parent-email-model-handoff.md)。尚未套用新 Schema 前，不啟用真實登入；**共用醫療登入若查詢 User Entity，也會依賴新欄位**，因此整個新模型須一起整合，不只是切換家長前端。

## API 契約

| API | Request | 成功回應 |
| --- | --- | --- |
| POST `/api/parent/auth/email/request` | `{ "email": "已核准家長的信箱" }` | challengeId、expiresAtUtc、resendAfterSeconds；沒有驗證碼與帳號資料 |
| POST `/api/parent/auth/email/verify` | `{ "challengeId": "伺服器回傳值", "code": "六位數" }` | userId、role=PARENT、accessToken、expiresAt |

- 兩個入口 `[AllowAnonymous]`，只接受 JSON 模型；不依賴 Cookie，回應 no-store。
- request IP 每分鐘 5 次，verify IP 每分鐘 10 次；同信箱另有 60 秒重寄限制。跨站 Origin 拒絕；不得為了代理部署自行信任任意 Forwarded Headers。
- verify 不接受前端指定的可信 Email／userId／院所；即使 JSON 額外提供也不影響帳號選擇。
- 錯誤：輸入模型錯誤 400 ValidationProblem；無效 OTP 401 `OTP_INVALID`；帳號未就緒 403 `PARENT_ACCOUNT_NOT_READY`；跨站 403 `REQUEST_ORIGIN_REJECTED`；重寄 429 `OTP_RESEND_TOO_SOON`；IP 限制可能是無 JSON 的 429；未啟用 503 `EMAIL_LOGIN_UNAVAILABLE`；寄信失敗 503 `EMAIL_SERVICE_UNAVAILABLE`。前端保留 HTTP 狀態並支援非 JSON 錯誤，不假設已統一成相同 DTO。
- 認證成功不代表有孩子；未綁定回空清單。家長不能查看他人的孩子、草稿或答案。

## 前端行為

入口：`/?role=parent` 或 `/?role=parent&mode=account`，兩者都是正式 Email 路徑，不再讓缺少 mode 時產生本機驗證碼。

驗證信箱 → 取得 challenge → 顯示信箱遮罩、到期／重寄倒數 → 驗證後交由 parent-session 保存伺服器 Token，導向 `/parent-workspace.html?mode=account`。

錯誤焦點回欄位；提交與寄送執行中阻擋重複點擊；切角色／換信箱／離頁會取消舊流程，晚到的 Token 不會登入。重寄失敗會清除舊 challenge，不能繼續拿舊碼假裝有效。不自動重寄郵件；登入憑證目前仍為單分頁 JWT，登出清除本機憑證，不等於 SessionVersion 已完成。

## 隔離驗收結果

- Email 寄信／OTP 核心：28＋32 項通過。
- Case／Guardian／Email：138 項通過，新增 HTTP 登入、偽造欄位、關閉時不寄信、跨站、重寄、可信 JWT 綁定指定孩子、重複兌換、撤銷後停止存取及保留紀錄。
- 問卷 Phase 2-2 handoff：79 項通過，預設未遷移的部署檢查仍維持嚴格。
- Node 前端：34 項通過，包括重複 request／verify、重寄失敗、非 JSON 429、切換角色後忽略舊回應。
- 瀏覽器 1440×900：Email 與教師輸入字體均 16px；初始、必填錯誤、503 及 OTP 錯誤畫面寬高均不超出 1440×900。角色切換清除欄位與錯誤。
- SQLite／測試 sender／真實 JWT 隔離瀏覽器：取得 OTP 畫面、60 秒重寄阻擋、格式錯誤、伺服器錯誤 OTP、成功進 account 工作台、未綁定不顯示孩子、登出回 Email 頁均通過。沒有真實郵件或共用 DB；測試完成後已關閉 fixture。

重跑（專案根目錄）：

```powershell
dotnet run --project tests/ParentEmailHarness.csproj --no-launch-profile
dotnet run --project tests/CaseGuardianHarness.csproj --no-launch-profile
dotnet run --project tests/QuestionnairePhase22Harness.csproj --no-launch-profile -- --case-guardian-schema-handoff
node --test tests/SharedFrontendDemo.test.cjs tests/TeacherDraftClient.test.cjs tests/ParentAccountClient.test.cjs tests/ParentOtpClient.test.cjs
```

需要瀏覽器隔離測試才使用 `CaseGuardianHarness --serve-email-browser-fixture`，port 5183，固定虛構帳號 `otp-parent@example.invalid`；code 只在這個測試執行檔的 `/fixture-email-code` 讀取。此路由不屬於網站，不可部署到正式環境。

## 使用者及 16 接下來的步驟

1. 16 Fetch 此功能分支固定 Commit，對照模型交接文件審查 User／邀請欄位、索引、舊資料與帳號核准來源；在獨立整合分支處理衝突，不整檔覆蓋醫療／RBAC 成果。
2. 由 16 統一產 Migration、preflight／review，再依團隊流程套用目標開發 DB；保留既有 ID，不把手機驗證時間當 Email 驗證。
3. 準備一個**核准的虛構 PARENT 測試帳號**：Email／NormalizedEmail／院所／角色與有效狀態正確，EmailVerifiedAtUtc 初始 null；信箱需可收信。這是團隊測試帳號準備，不是開放自動註冊。
4. 確認該網站只有一個程序、OTP 不需跨主機共享；若多程序先接共享狀態。寄件服務目前為 Gmail，仍有額度與防濫用限制。
5. Schema／回填與環境都確認後，才在目標執行環境設定 `ParentEmailLogin:Enabled=true`。本機 Development 可在 API 資料夾執行 `dotnet user-secrets set "ParentEmailLogin:Enabled" "true"`，再重新啟動；本輪**沒有代為設定**。正式寄信可能產生外部郵件，人工測試以本人／團隊同意的測試信箱進行。
6. 開啟 `http://localhost:5177/?role=parent`，完成 Email／收碼／登入 → 未綁定看不到孩子 → 指定 Email 邀請兌換 → 只看到指定孩子 → 登出／再次登入 → 撤銷後不能存取。錯碼、到期、重寄後舊碼、停用與跨院所也要驗收。
7. 網站重啟：已保存的綁定需持續存在，但尚未使用的記憶體 OTP 應失效並要求重新取得。不要把重啟後 OTP 失效當作 MySQL 資料遺失。

仍未完成：首次帳號與院所的正式核准／建立流程、換登入信箱與跨帳號轉移、共享 OTP 儲存／SessionVersion，以及真實 MySQL＋Gmail Email 登入端到端驗收。不得由目前的示範工作台資料代替這些項目。

## 真實環境人工驗收表（待 Migration，尚未執行）

前置：16 確認 Schema／回填及單程序環境後才啟用。僅用已核准的虛構家長／孩子及本人或已同意的測試信箱。不要為測限流而大量寄信；頻率與失敗情境優先使用受控替身。

| 勾選 | 操作 | 預期結果 |
| --- | --- | --- |
| [ ] | 未啟用時按取得驗證碼 | 503，沒有郵件；前端不進驗證畫面，也不誤判密碼錯誤。 |
| [ ] | 核准家長輸入有效 Email | 實際收到六位碼，API 不回碼，畫面顯示信箱遮罩與五分鐘期限。 |
| [ ] | 輸入格式錯誤信箱 | 欄位提示並聚焦，不寄信、不登入。 |
| [ ] | 未核准／重複信箱驗證正確碼 | 不發 Token、不建帳號、不猜院所；request 不透過帳號是否存在顯示不同結果。 |
| [ ] | 輸入已收到碼以外的碼五次，再填正確碼 | 前五次均失敗，之後原正確碼也不能使用；按規則重新取得。 |
| [ ] | 五分鐘到期後輸入原碼 | 拒絕，不發 Token。 |
| [ ] | 六十秒內連按取得／重寄 | 前端阻擋重複點擊；繞過前端亦受後端限制。 |
| [ ] | 六十秒後重寄，再用舊 challenge／舊碼驗證 | 舊 challenge 失效；只能用新 challenge 與新碼，成功後不得重播。 |
| [ ] | 取得碼時切角色／更換信箱，舊回應稍後抵達 | 不恢復舊流程，不接受晚到 Token。 |
| [ ] | 正確碼登入，尚未綁孩子 | 正式 account 工作台顯示沒有孩子；不使用展示資料補上。 |
| [ ] | 正確邀請碼綁定指定信箱的孩子 | 出現該孩子，不能看到其他院所／家長的孩子。相同邀請不能再次兌換。 |
| [ ] | 錯誤信箱使用同一邀請 | 拒絕且不消耗邀請；相同手機不能代替指定 Email。 |
| [ ] | 撤銷指定孩子的 Guardian 關係後重新載入 | 該孩子不再可讀，原綁定／答案保留；其他仍有效孩子不因而轉移或刪除。 |
| [ ] | 停用帳號／院所，再使用既有 Token | 後端重新查 DB 後拒絕，不只看 Token 的舊角色。 |
| [ ] | 登出後按上一頁／恢復頁面 | 不恢復孩子資料；登入頁／工作台要求重新登入。 |
| [ ] | 網站重啟後重新登入 | 真實 DB 的已綁定孩子仍在；重啟前未使用的記憶體 OTP 作廢，需要新碼。 |
| [ ] | 寄信失敗／服務中斷 | 顯示可理解錯誤，不自動重寄、不假裝登入。結果不確定時先核對收信。 |
| [ ] | 前端與 API 額外送 userId／role／院所 | 不影響後端可信帳號選擇與孩子範圍。 |

記錄方式：每項填執行日期、版本、測試帳號代稱、通過／失敗與安全錯誤碼；不記錄明碼 OTP、JWT、應用程式密碼、完整邀請碼或真實兒童資料。本表不代表以上實際 MySQL 驗收已通過。
