# 早療智慧動態照顧系統：前端 HTML 與 CSS 原始碼

整理日期：2026-10-06。此文件收錄目前 VS2022 專案中的全部 HTML 與 CSS，採用工作目錄內最新內容（包含尚未提交的院徽更新）。

共 2 份 HTML、3 份 CSS。原始碼完整保留；JavaScript 與圖片為另外的相依檔案，未嵌入本文件。登入頁與教師工作台的院徽使用 `EarlyInterventionCare.Api/wwwroot/images/hospital-logo.png`，圖片在左、中英文院名在右；電腦版設計基準為 1440 × 900 CSS px。

## 檔案清單

- `EarlyInterventionCare.Api/wwwroot/css/login.css`
- `EarlyInterventionCare.Api/wwwroot/css/teacher-access.css`
- `EarlyInterventionCare.Api/wwwroot/css/teacher-workspace.css`
- `EarlyInterventionCare.Api/wwwroot/index.html`
- `EarlyInterventionCare.Api/wwwroot/teacher-workspace.html`

## EarlyInterventionCare.Api/wwwroot/css/login.css

```css
:root {
  color-scheme: light;
  --green: #719b96;
  --green-dark: #567f7a;
  --ink: #374151;
  --muted: #9299a3;
  --line: #e9ebee;
  font-family: "Noto Sans TC", "Microsoft JhengHei", "PingFang TC", Arial, sans-serif;
  color: var(--ink);
  background: #f8fafc;
}
* { box-sizing: border-box; }
body { margin: 0; }
button, input { font: inherit; }
button { cursor: pointer; }
button:focus-visible, input:focus-visible { outline: 3px solid #476f6b; outline-offset: 3px; }
[hidden] { display: none !important; }
.login-page { min-height: 100svh; padding: 65px 16px 28px; display: flex; flex-direction: column; align-items: center; background: radial-gradient(ellipse at 50% 28%, #eff5f4 0, transparent 50%), #f8fafc; }
.login-card { width: 480px; max-width: 100%; padding: 0 32px 28px; position: relative; overflow: hidden; border-radius: 24px; background: white; box-shadow: 0 8px 24px #33415508; }
.login-card::before { content: ""; position: absolute; inset: 0 0 auto; height: 8px; background: var(--green); }
.brand { padding-top: 40px; text-align: center; }
.hospital-brand { display: flex; align-items: center; justify-content: center; gap: 12px; text-align: left; }
.hospital-mark { display: block; width: 40px; height: 40px; object-fit: contain; flex: 0 0 auto; }
.hospital-name { margin: 0 0 4px; font-size: 16px; font-weight: 700; color: #333; }
.hospital-english { margin: 0; font-size: 9px; letter-spacing: .1px; color: #9ca3af; }
h1 { margin: 20px 0 4px; font-size: 24px; line-height: 1.5; font-weight: 700; color: var(--green); }
.system-english { margin: 0; font-size: 14px; line-height: 1.5; color: #7d8591; }
.role-tabs { display: grid; grid-template-columns: repeat(3, 1fr); margin-top: 24px; border-bottom: 1px solid var(--line); }
.role-tab { border: 0; border-bottom: 3px solid transparent; background: transparent; padding: 12px 0; font-size: 16px; color: #9ca3af; white-space: nowrap; }
.role-tab[aria-selected="true"] { color: var(--green); font-weight: 700; border-bottom-color: var(--green); }
.role-tab:hover { color: var(--green-dark); }
.role-panel { padding-top: 24px; }
.field { margin-bottom: 16px; }
.field label { display: block; margin-bottom: 8px; font-size: 14px; font-weight: 700; color: #3c4143; }
.field input { width: 100%; min-height: 46px; padding: 11px 16px; border: 1px solid #d1d5db; border-radius: 9px; background: #f8fafc; color: var(--ink); box-shadow: inset 0 1px 2px #00000003; }
.field input::placeholder { color: #a2a9b2; opacity: 1; }
.field input[aria-invalid="true"] { border-color: #b74a45; }
.primary-button { width: 100%; min-height: 52px; border: 0; border-radius: 9px; background: var(--green); color: white; font-weight: 700; letter-spacing: 4px; box-shadow: 0 3px 6px #3341551a; padding: 12px 20px; }
.primary-button:hover { background: var(--green-dark); }
.primary-button:active { transform: translateY(1px); }
.faq-section { margin-top: 28px; border-top: 1px solid #f1f2f4; padding-top: 20px; text-align: center; }
.faq-button { background: transparent; border: 0; color: #737b86; padding: 0; font-size: 14px; display: inline-flex; align-items: center; justify-content: center; gap: 8px; }
.faq-button:hover { color: var(--green-dark); }
.help-icon { display: inline-grid; place-items: center; width: 14px; height: 14px; border: 1.4px solid currentColor; border-radius: 50%; font-family: Arial, sans-serif; font-size: 10px; font-weight: 700; }
.teacher-intro { margin: 0 0 24px; border: 1px solid #d6dfeb; border-radius: 8px; background: #eff6ff; color: #7891b5; padding: 14px 16px; font-size: 16px; line-height: 1.5; }
.field-help { margin: 10px 2px 0; color: #9ba2ab; font-size: 13px; }
#teacher-code { text-transform: uppercase; letter-spacing: 1px; }
.divider { display: flex; align-items: center; gap: 16px; margin: 30px 0; color: #9ca3af; font-size: 14px; }
.divider::before, .divider::after { content: ""; height: 1px; background: var(--line); flex: 1; }
.secondary-button { display: flex; align-items: center; justify-content: center; gap: 8px; width: 100%; min-height: 50px; border: 1px solid var(--green); border-radius: 9px; background: white; color: var(--green); font-size: 16px; font-weight: 700; }
.secondary-button:hover { background: #f0f6f5; }
.secondary-button svg, .teacher-notice svg { width: 18px; height: 18px; fill: none; stroke: currentColor; stroke-width: 1.7; }
.teacher-notice { border-top: 1px solid #f1f2f4; padding-top: 24px; margin: 24px 0 0; display: flex; align-items: center; justify-content: center; gap: 5px; color: #9a9da3; font-size: 12px; }
.teacher-notice svg { width: 13px; height: 13px; color: #959b77; }
.form-status { margin: 16px 0 0; padding: 12px 14px; background: #f0f6f5; border: 1px solid #d3e2df; border-radius: 8px; color: #486b66; line-height: 1.65; font-size: 14px; letter-spacing: 0; }
.field-error { display: block; margin-top: 8px; font-size: 13px; color: #a23c37; }
.page-footer { margin-top: auto; padding-top: 30px; text-align: center; color: #a3aab4; font-size: 11px; line-height: 1.8; }
.page-footer p { margin: 3px 0; }
.footer-link { background: transparent; padding: 0; border: 0; color: #87919b; text-decoration: underline; text-underline-offset: 2px; font-size: inherit; }
.copyright { font-size: 10px; color: #bdc3cb; }
.prototype-note { font-size: 11px; color: #7e8e8b; }
dialog { width: min(440px, calc(100% - 32px)); padding: 28px; border: 1px solid #e1e7e6; border-radius: 20px; color: var(--ink); box-shadow: 0 20px 70px #1f29372b; }
dialog::backdrop { background: #243a3c66; }
.dialog-header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.dialog-header h2 { margin: 0; color: var(--green-dark); font-size: 20px; }
.close-button { background: transparent; border: 0; padding: 0 4px; font-size: 28px; color: #8b959d; }
#dialog-content { line-height: 1.8; font-size: 14px; margin: 20px 0 24px; }
#dialog-content p { margin: 0 0 14px; }
@media (max-width: 560px) {
  .login-page { padding: 28px 16px 24px; }
  .login-card { padding: 0 24px 28px; border-radius: 20px; }
  .brand { padding-top: 32px; }
  .hospital-brand { gap: 8px; }
  .hospital-name { font-size: 14px; }
  .hospital-english { font-size: 8px; }
  .hospital-mark { width: 34px; height: 34px; }
  h1 { font-size: 21px; }
  .system-english { font-size: 12px; }
  .role-tabs { margin-top: 24px; }
  .role-tab { font-size: 14px; padding: 14px 0; }
  .faq-button { font-size: 12px; }
  .teacher-intro { font-size: 14px; }
  .page-footer { max-width: 400px; }
}
@media (prefers-reduced-motion: reduce) { .primary-button:active { transform: none; } }

/* Desktop design baseline: 1440 × 900. Keep overflow available for extra messages. */
@media (min-width: 1024px) {
  .login-page { min-height: max(900px, 100svh); padding: 65px 32px 20px; }
  .login-card { flex-shrink: 0; padding-bottom: 24px; }
  .brand { padding-top: 32px; }
  .role-tabs { margin-top: 20px; }
  .role-tab { padding: 10px 0; }
  .role-panel { padding-top: 20px; }
  .field { margin-bottom: 14px; }
  .teacher-intro { margin-bottom: 16px; }
  .divider { margin: 20px 0; }
  .teacher-notice { margin-top: 16px; padding-top: 16px; }
  .faq-section { margin-top: 20px; padding-top: 16px; }
  .page-footer { padding-top: 16px; font-size: 10px; line-height: 1.6; }
  .page-footer p { margin: 2px 0; }
  .prototype-note { font-size: 10px; }
}
```

## EarlyInterventionCare.Api/wwwroot/css/teacher-access.css

```css
.teacher-flow { border-top: 1px solid #f1f2f4; margin-top: 32px; padding-top: 32px; }
.teacher-state { text-align: center; }
.teacher-state h2 { margin: 24px 0 18px; font-size: 24px; line-height: 1.5; color: #30383c; }
.teacher-state h2:focus { outline: none; }
.state-description { margin: 0 0 30px; color: #87909c; line-height: 1.8; font-size: 14px; }
.authorization-spinner { width: 48px; height: 48px; margin: 0 auto; border: 4px solid #edf2f2; border-top-color: var(--green); border-radius: 50%; animation: authorization-spin 1s linear infinite; }
.authorization-error-icon, .authorization-pending-icon { width: 64px; height: 64px; margin: 0 auto; border-radius: 50%; display: grid; place-items: center; background: #faeeed; color: #bd7770; font: 700 34px Arial, sans-serif; }
.authorization-pending-icon { background: #eff6f5; color: var(--green); }
.failure-reasons { margin: 0 0 24px; padding: 16px; border: 1px solid #d6dfeb; border-radius: 8px; background: #eff6ff; color: #7891b5; font-size: 16px; line-height: 1.65; text-align: left; }
.failure-reasons p { margin: 0; }
.failure-reasons ul { margin: 0; padding-left: 22px; }
.state-preview-note { margin: 16px 0; color: #7e8e8b; font-size: 12px; line-height: 1.6; }
.text-button { border: 0; padding: 8px; background: transparent; color: var(--green-dark); text-decoration: underline; text-underline-offset: 4px; font-size: 14px; }
.scan-state h2 { margin: 0 0 16px; }
.scan-state .state-description { margin-bottom: 30px; }
.scanner-frame { width: 250px; aspect-ratio: 1; max-width: 100%; margin: 0 auto; position: relative; border-radius: 16px; background: #293237; overflow: hidden; }
.scanner-corner { position: absolute; width: 36px; height: 36px; border-color: #eff3f2; border-style: solid; border-width: 0; }
.top-left { top: 25px; left: 25px; border-top-width: 3px; border-left-width: 3px; }
.top-right { top: 25px; right: 25px; border-top-width: 3px; border-right-width: 3px; }
.bottom-left { bottom: 25px; left: 25px; border-bottom-width: 3px; border-left-width: 3px; }
.bottom-right { bottom: 25px; right: 25px; border-bottom-width: 3px; border-right-width: 3px; }
.scanner-line { position: absolute; left: 25px; right: 25px; top: 50%; height: 2px; background: var(--green); box-shadow: 0 0 6px #83b8b0aa; }
.scan-help { margin: 24px 0 0; color: #87909c; font-size: 14px; }
.scan-state .secondary-button { margin-top: 24px; }
.teacher-preview-controls { margin: 16px auto 0; max-width: 440px; font-size: 12px; }
.teacher-preview-controls summary { cursor: pointer; color: var(--green-dark); }
.teacher-preview-controls nav { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; padding: 12px 0; }
.teacher-preview-controls button { padding: 6px 10px; border: 1px solid #d9e5e3; border-radius: 6px; color: var(--green-dark); background: #fff; font-size: 12px; }
.teacher-preview-controls button:hover { background: #edf5f3; }
@keyframes authorization-spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) { .authorization-spinner { animation: none; } }
@media (max-width: 560px) {
  .teacher-state h2 { font-size: 22px; }
  .failure-reasons { font-size: 14px; }
  .teacher-flow { margin-top: 24px; padding-top: 28px; }
}

@media (min-width: 1024px) {
  .teacher-flow { margin-top: 20px; padding-top: 20px; }
  .teacher-state h2 { margin: 16px 0 14px; }
  .state-description { margin-bottom: 20px; }
  .failure-reasons { padding: 12px 16px; font-size: 14px; margin-bottom: 20px; }
  .state-preview-note { margin: 12px 0; }
  .scan-state h2 { margin: 0 0 12px; }
  .scan-state .state-description { margin-bottom: 20px; }
  .scanner-frame { width: 200px; }
  .scan-state .state-preview-note { margin: 8px 0; }
  .scan-help { margin-top: 12px; }
  .scan-state .secondary-button { margin-top: 16px; }
  .teacher-preview-controls { margin-top: 8px; font-size: 11px; }
}

/* Separate preview entry; it never indicates an authenticated teacher session. */
.teacher-workspace-preview { margin-top:20px; padding-top:20px; border-top:1px solid #f1f2f4; }
.teacher-workspace-preview a { text-decoration:none; min-height:44px; font-size:14px; }
.teacher-workspace-preview p { margin:9px 0 0; color:#9299a3; text-align:center; font-size:11px; }

#create-test-code{font-size:12px;min-height:38px;margin:12px 0 8px}#test-code-status{max-width:430px;margin:8px auto}
#teacher-invitation-dialog { width:min(480px,calc(100% - 32px)); max-height:calc(100dvh - 48px); overflow-y:auto; box-sizing:border-box; }
#teacher-invitation-dialog p { font-size:14px; line-height:1.7; }
#teacher-invitation-qr { display:block; width:208px; height:208px; max-width:100%; margin:12px auto; }
#teacher-invitation-dialog label { display:block; margin-bottom:6px; font-size:14px; font-weight:600; }
#teacher-invitation-url { width:100%; box-sizing:border-box; resize:none; padding:10px; border:1px solid #d4dfdd; border-radius:8px; font-family:inherit; font-size:13px; line-height:1.6; overflow-wrap:anywhere; }
.invitation-actions { display:flex; flex-wrap:wrap; gap:10px; }
.invitation-actions .secondary-button { flex:1; text-align:center; text-decoration:none; }
.invitation-local-note { color:#697876; }
#teacher-invitation-status { min-height:24px; }
```

## EarlyInterventionCare.Api/wwwroot/css/teacher-workspace.css

```css

:root{--ink:#374151;--muted:#9299a3;--green:#719b96;--dark:#567f7a;--pale:#f0f6f5;--line:#e9ebee;--paper:#f8fafc;--white:#fff;--shadow:0 8px 24px #33415508;color-scheme:light}
*{box-sizing:border-box}[hidden]{display:none!important}body{margin:0;background:radial-gradient(ellipse at 50% 28%,#eff5f4 0,transparent 50%),var(--paper);color:var(--ink);font-family:"Noto Sans TC","Microsoft JhengHei","PingFang TC",Arial,sans-serif;font-size:14px;line-height:1.75}button,input,textarea{font:inherit}button{cursor:pointer;border:0}button:disabled{cursor:default;opacity:.55}button:focus-visible,a:focus-visible,input:focus-visible,textarea:focus-visible{outline:3px solid #476f6b;outline-offset:3px}a{color:var(--dark)}svg{width:21px;height:21px;stroke:currentColor;fill:none;stroke-width:1.7;stroke-linecap:round;stroke-linejoin:round;flex-shrink:0}.sprite{position:absolute;width:0;height:0;overflow:hidden}.app{display:grid;grid-template-columns:232px 1fr;min-height:100svh}.sidebar{background:#fff;border-right:1px solid var(--line);padding:32px 20px;display:flex;flex-direction:column;gap:36px;border-top:8px solid var(--green)}.brand{display:flex;gap:11px;align-items:center}.brandmark{width:36px;height:36px;display:grid;place-items:center;background:var(--pale);border-radius:10px;color:var(--green)}.brand strong{display:block;font-size:15px;color:var(--dark)}.brand small{font-size:10px;color:var(--muted)}.side-label{font-size:11px;color:var(--muted);margin:0 13px 13px}.nav{display:flex;flex-direction:column;gap:8px}.nav button{display:flex;align-items:center;gap:11px;padding:13px;border-radius:9px;background:transparent;color:#87919b;text-align:left}.nav button.active{background:var(--pale);color:var(--dark);font-weight:700}.nav button:hover{background:#f8fafc}.badge{margin-left:auto;background:#e5eeec;padding:0 7px;border-radius:5px;font-size:12px}.side-note{margin-top:auto;border:1px solid #d3e2df;background:var(--pale);border-radius:9px;padding:16px;color:#7e8e8b;font-size:12px}.side-note strong{display:flex;gap:7px;align-items:center;color:var(--dark);font-size:12px;margin-bottom:7px}.side-note p{margin:0}.demo-reset{padding:0;background:none;color:var(--dark);text-decoration:underline;font-size:11px;margin-top:14px}.workspace{min-width:0}.topbar{min-height:94px;background:#fff;border-bottom:1px solid var(--line);display:flex;align-items:center;justify-content:space-between;gap:15px;padding:18px 40px}.hospital-brand{display:flex;gap:12px;align-items:center}.hospital-mark{display:block;width:40px;height:40px;object-fit:contain;flex-shrink:0}.hospital-name{margin:0 0 4px;font-size:16px;font-weight:700;color:#333}.hospital-english{margin:0;font-size:9px;color:#9ca3af;letter-spacing:.1px}.breadcrumb{display:none}.session{display:flex;gap:10px;align-items:center;color:var(--muted);font-size:12px}.session-dot{width:7px;height:7px;border-radius:50%;background:var(--green)}.avatar{width:34px;height:34px;background:var(--pale);color:var(--green);display:grid;place-items:center;border-radius:50%}.demo{font-size:10px;border:1px solid #dce4e3;background:#f8fafc;padding:2px 7px;border-radius:5px;color:#7e8e8b}.content{max-width:1210px;margin:auto;padding:34px 40px 24px}.heading-row{display:flex;justify-content:space-between;align-items:center;gap:18px;margin-bottom:24px}.eyebrow{font-size:11px;color:#9ca3af;letter-spacing:.08em;margin-bottom:5px}h1{margin:0 0 6px;color:var(--green);font-size:27px;line-height:1.5}h2{font-size:18px;margin:0;color:#4b5563}h3{font-size:16px;margin:0}.subtitle{margin:0;font-size:13px;color:#7d8591}.date{font-size:12px;color:var(--muted)}.welcome{background:#eff6ff;border:1px solid #d6dfeb;border-radius:9px;padding:19px 23px;margin-bottom:24px}.welcome h2{font-size:16px;color:#7891b5;margin:0 0 5px}.welcome p{font-size:13px;color:#7891b5;margin:0}.welcome .tag,.welcome-art{display:none}.grid{display:grid;grid-template-columns:minmax(0,1fr) 270px;gap:22px}.panel{background:white;border:1px solid var(--line);border-radius:20px;box-shadow:var(--shadow)}.child{padding:21px 23px;display:flex;align-items:center;gap:15px;margin-bottom:24px}.child-avatar{width:48px;height:48px;border-radius:12px;display:grid;place-items:center;background:#f0f6f5;color:var(--green)}.child-label{font-size:11px;color:var(--muted)}.child h3{font-size:18px}.child-meta{font-size:12px;color:#87919b}.child-meta span{margin-right:13px}.child-end{margin-left:auto;text-align:right;font-size:11px;color:var(--muted)}.child-end strong{display:block;font-weight:500;color:#7e8e8b}.section-title{display:flex;justify-content:space-between;align-items:center;margin-bottom:14px}.section-title small{font-size:12px;color:var(--muted)}.task{padding:24px;margin-bottom:16px;position:relative;overflow:hidden}.task:before{content:"";position:absolute;inset:0 0 auto;height:4px;background:var(--green)}.task-top{display:flex;gap:12px;align-items:flex-start}.task-icon{width:40px;height:43px;border-radius:10px;background:var(--pale);color:var(--green);display:grid;place-items:center}.task-title{flex:1}.task-title p{font-size:12px;color:#9299a3;margin:6px 0 0}.status{font-size:11px;color:#9299a3;background:#f8fafc;border:1px solid var(--line);border-radius:6px;padding:3px 8px;white-space:nowrap}.status.progress{color:#7891b5;background:#eff6ff;border-color:#d6dfeb}.status.done{color:var(--dark);background:var(--pale);border-color:#d3e2df}.task-bottom{display:flex;gap:18px;align-items:center;margin-top:22px}.task-progress{flex:1}.progress-label{display:flex;justify-content:space-between;gap:12px;color:#9299a3;font-size:11px}.track{height:5px;background:#eef2f3;margin-top:8px;border-radius:8px;overflow:hidden}.track span{display:block;background:var(--green);height:100%;border-radius:8px}.btn{display:inline-flex;align-items:center;justify-content:center;gap:8px;background:var(--green);color:white;padding:11px 19px;border:1px solid var(--green);border-radius:9px;font-size:13px;font-weight:700;box-shadow:0 3px 6px #3341550a;white-space:nowrap}.btn:hover{background:var(--dark)}.btn.secondary{background:white;color:var(--green);box-shadow:none}.btn.secondary:hover{background:var(--pale)}.btn.ghost{background:transparent;color:#737b86;border-color:transparent;box-shadow:none;padding:7px 0;font-weight:400}.btn svg{width:17px;height:17px}.deadline,.tips{padding:23px}.deadline{margin-bottom:20px}.side-title{display:flex;align-items:center;gap:9px;font-weight:700;font-size:14px;margin-bottom:14px}.side-title svg{color:var(--green);width:18px;height:18px}.deadline-date{font-size:22px;color:var(--green);font-weight:700}.deadline-time{font-size:12px;color:var(--muted);margin-top:5px}.deadline-note{display:flex;gap:7px;font-size:11px;color:#7e8e8b;background:var(--pale);border-radius:8px;padding:12px;margin-top:15px}.deadline-note svg{width:15px;height:15px;margin-top:3px}.tips ol{padding-left:18px;margin:0;font-size:12px;color:#9299a3}.tips li{padding-left:3px;margin-bottom:14px}.tips strong{display:block;color:#737b86;margin-bottom:3px}.privacy{display:flex;gap:7px;margin:23px 2px;color:#9299a3;font-size:11px}.privacy svg{width:15px;height:15px;margin-top:3px}.footer{display:flex;flex-wrap:wrap;justify-content:space-between;gap:10px;text-align:center;border-top:1px solid var(--line);margin-top:30px;padding-top:18px;font-size:10px;color:#a3aab4}.tag{background:var(--pale);color:var(--dark);border:1px solid #d3e2df;font-size:11px;padding:4px 10px;border-radius:6px}.back{margin-bottom:15px}.form-layout{display:grid;grid-template-columns:minmax(0,1fr) 230px;gap:22px}.form-card{padding:26px;min-width:0}.form-intro{font-size:13px;line-height:1.9;color:#7891b5;background:#eff6ff;border:1px solid #d6dfeb;border-radius:8px;padding:17px;margin-bottom:24px}.source-note{display:block;color:#9299a3;font-size:11px;margin-top:9px}.respondent-fields{display:grid;grid-template-columns:1fr 1fr;gap:16px;padding-bottom:24px;margin-bottom:20px;border-bottom:1px solid var(--line)}.field-label{font-size:13px;font-weight:700;display:block;margin-bottom:7px}.respondent-fields input{width:100%;min-height:43px;padding:9px 12px;border:1px solid #d1d5db;border-radius:9px;background:#f8fafc;color:var(--ink)}.question{border:0;border-bottom:1px solid #f1f2f4;padding:0 0 23px;margin:0 0 23px;min-width:0}.question legend{font-size:14px;font-weight:500;margin-bottom:12px;line-height:1.85}.qnum{display:inline-grid;place-items:center;width:25px;height:25px;background:var(--pale);color:var(--green);border-radius:6px;font-size:11px;margin-right:8px}.required{font-size:10px;color:#9ca3af;margin-left:7px}.options{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}.options.three{grid-template-columns:repeat(3,minmax(0,1fr))}.option{display:flex;align-items:center;justify-content:center;gap:7px;font-size:12px;border:1px solid #d1d5db;border-radius:9px;padding:10px 6px;cursor:pointer;background:#f8fafc}.option:has(input:checked){background:var(--pale);color:var(--dark);border-color:var(--green)}.option input{width:15px;height:15px;accent-color:var(--green);margin:0}.group-heading{font-size:15px;color:var(--dark);margin:29px 0 20px;padding:10px 14px;border-left:3px solid var(--green);background:var(--pale)}.group-heading:first-child{margin-top:0}textarea{width:100%;padding:13px;min-height:108px;border:1px solid #d1d5db;border-radius:9px;background:#f8fafc;resize:vertical;color:var(--ink);font-size:13px}.hint{font-size:11px;color:var(--muted);margin:8px 0}.form-actions{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:14px;padding-top:22px;border-top:1px solid var(--line);margin-top:20px}.save-state{color:#9299a3;font-size:11px}.form-side{padding:22px;align-self:start;position:sticky;top:20px}.form-side p{font-size:12px;color:var(--muted)}.form-side .btn{width:100%;margin-top:12px}.question.invalid{background:#fff5f3;border:1px solid #e2b8b3;border-radius:9px;padding:13px}.error{background:#fff5f3;color:#a23c37;border-radius:8px;padding:12px;font-size:13px;margin-top:15px}.completion-banner{padding:17px 21px;background:var(--pale);border:1px solid #d3e2df;border-radius:9px;color:var(--dark);font-size:13px;margin-bottom:24px}.success{max-width:730px;margin:30px auto;padding:42px;text-align:center;border-top:6px solid var(--green)}.success-icon{width:68px;height:68px;border-radius:50%;background:var(--pale);color:var(--green);display:grid;place-items:center;margin:0 auto 23px}.success-icon svg{width:32px;height:32px}.success h1{font-size:26px}.success p{font-size:13px;color:var(--muted)}.receipt{padding:20px;border-radius:9px;background:#f8fafc;text-align:left;margin:24px 0}.receipt-row{display:flex;justify-content:space-between;gap:14px;font-size:12px;margin:9px 0}.receipt-row span{color:var(--muted)}.success-actions{display:flex;justify-content:center;gap:12px;flex-wrap:wrap}.help-card{padding:30px;max-width:820px}.help-card h3{margin:24px 0 8px;color:#567f7a}.help-card h3:first-child{margin-top:0}.help-card p{font-size:13px;color:#9299a3}dialog{padding:28px;width:min(460px,calc(100% - 32px));border:1px solid #e1e7e6;border-radius:20px;color:var(--ink);box-shadow:0 20px 70px #1f29372b}dialog::backdrop{background:#243a3c66}dialog h2{color:var(--dark)}dialog p{font-size:13px;color:#87919b}.dialog-actions{display:flex;justify-content:flex-end;gap:10px;margin-top:24px}.review-line{font-size:13px;padding:12px;background:var(--pale);border-radius:8px}.toast{position:fixed;bottom:28px;left:50%;transform:translateX(-50%);background:#567f7a;color:white;font-size:13px;padding:12px 22px;border-radius:9px;z-index:30;box-shadow:var(--shadow)}
@media(max-width:1100px){.app{grid-template-columns:200px 1fr}.content{padding:28px}.topbar{padding:17px 28px}.grid{grid-template-columns:minmax(0,1fr) 230px}.form-layout{grid-template-columns:1fr}.form-side{position:static}.brand strong{font-size:13px}.child-end{display:none}}
@media(max-width:850px){.app{grid-template-columns:1fr}.sidebar{padding:16px 23px;gap:15px;border-right:0;border-bottom:1px solid var(--line);border-top-width:6px}.sidebar .brand{display:none}.side-note,.side-label{display:none}.nav{flex-direction:row;justify-content:space-between}.nav button{font-size:12px;padding:9px 12px}.topbar{padding:17px 23px;min-height:80px}.hospital-name{font-size:14px}.hospital-english{font-size:8px}.content{padding:26px 23px}.grid{grid-template-columns:1fr}.aside{display:grid;grid-template-columns:1fr 1fr;gap:18px}.deadline{margin:0}.date{display:none}.form-layout{grid-template-columns:1fr}.session-label{display:none}}
@media(max-width:520px){.content{padding:24px 17px}.topbar{padding:16px 17px}.hospital-brand{gap:8px}.hospital-mark{width:34px;height:34px}.hospital-name{font-size:12px}.hospital-english{font-size:7px}.avatar,.session-dot{display:none}.session{gap:0}h1{font-size:23px}.subtitle{font-size:12px}.welcome{padding:17px}.welcome h2{font-size:14px}.child{padding:19px}.task{padding:20px 17px}.task-title h3{font-size:15px}.task-icon{width:33px;height:38px}.task-top{gap:9px}.task-bottom{gap:13px}.task-bottom .btn{padding:10px 12px}.aside{grid-template-columns:1fr}.form-card{padding:22px 16px}.options{grid-template-columns:repeat(2,minmax(0,1fr))}.options.three{grid-template-columns:repeat(3,minmax(0,1fr))}.options.three .option{font-size:11px;padding:10px 4px;gap:5px}.respondent-fields{grid-template-columns:1fr}.form-actions .btn{padding:10px 14px}.success{padding:30px 20px}.receipt{padding:16px}.receipt-row{font-size:11px}.help-card{padding:24px}.nav button{gap:7px;padding:8px}.heading-row{align-items:flex-start}.heading-row .tag{font-size:10px}.footer{justify-content:center}}

.prototype-disclaimer{font-size:12px;color:#7891b5;margin:8px 0 0}.return-login{display:block;font-size:12px;margin-top:12px}.return-login:focus-visible{outline:3px solid #476f6b;outline-offset:3px}

a.btn{text-decoration:none}

/* Desktop acceptance baseline: browser content viewport 1440 x 900 CSS pixels.
   Keep the shell within the viewport; long content scrolls in the main panel. */
@media (min-width: 1101px) {
  .app { height:100svh; min-height:0; overflow:hidden; }
  .sidebar { min-height:0; overflow-y:auto; padding:25px 20px; gap:28px; }
  .workspace { display:flex; flex-direction:column; height:100%; min-height:0; }
  .topbar { min-height:80px; flex:0 0 80px; padding:14px 32px; }
  .content { width:100%; flex:1; min-height:0; overflow-y:auto; padding:20px 32px 16px; overscroll-behavior:contain; scrollbar-gutter:stable; }
  .heading-row { margin-bottom:16px; }
  h1 { font-size:25px; line-height:1.4; }
  .eyebrow { margin-bottom:3px; }
  .prototype-disclaimer { margin:5px 0 0; line-height:1.6; }
  .subtitle { line-height:1.6; }
  .welcome { padding:14px 19px; margin-bottom:16px; }
  .welcome h2 { line-height:1.6; margin-bottom:3px; }
  .welcome p { line-height:1.6; }
  .grid { grid-template-columns:minmax(0,1fr) 300px; gap:20px; }
  .child { padding:14px 19px; margin-bottom:16px; }
  .section-title { margin-bottom:10px; }
  .task { padding:18px 20px; margin-bottom:12px; }
  .task-title p { margin-top:4px; line-height:1.6; }
  .task-bottom { margin-top:14px; }
  .deadline,.tips { padding:16px 19px; }
  .deadline { margin-bottom:14px; }
  .side-title { margin-bottom:10px; }
  .deadline-note { margin-top:10px; padding:10px; line-height:1.65; }
  .tips ol { line-height:1.65; }
  .tips li { margin-bottom:10px; }
  .tips .btn { font-size:12px; }
  .privacy { margin:15px 2px 0; line-height:1.65; }
  .footer { margin-top:20px; padding-top:12px; line-height:1.6; }
  .back { margin-bottom:10px; }
  .form-side { top:0; }
  .success { margin:15px auto; padding:30px 36px; }
  .success-icon { width:60px; height:60px; margin-bottom:17px; }
  .receipt { padding:16px 20px; margin:18px 0; }
  .help-card { padding:24px 28px; }
  .help-card h3 { margin-top:17px; margin-bottom:6px; }
  .help-card p { margin:8px 0; line-height:1.65; }
}

@media (min-width:1101px){#home-page:has(.completion-banner:not([hidden])) .welcome{display:none}.completion-banner{padding:12px 19px;margin-bottom:16px}}
```

## EarlyInterventionCare.Api/wwwroot/index.html

```html
<!doctype html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <title>登入｜早療智慧動態照顧系統</title>
  <link rel="stylesheet" href="/css/login.css?v=20261006-hospital-mark">
  <link rel="stylesheet" href="/css/teacher-access.css?v=20261006-invitation">
  <script src="/js/login.js" defer></script>
  <script src="/js/teacher-access.js?v=20261006-invitation" defer></script>
</head>
<body>
  <main class="login-page">
    <section class="login-card" aria-labelledby="system-title">
      <header class="brand">
        <div class="hospital-brand">
          <img class="hospital-mark" src="/images/hospital-logo.png" alt="" width="325" height="247"><div><p class="hospital-name">天主教輔仁大學附設醫院</p><p class="hospital-english" lang="en">FU JEN CATHOLIC UNIVERSITY HOSPITAL</p></div>
        </div>
        <h1 id="system-title">早療智慧動態照顧系統</h1>
        <p class="system-english" lang="en">Early Intervention Smart Care System</p>
      </header>

      <div class="role-tabs" role="tablist" aria-label="選擇登入身分">
        <button id="tab-parent" class="role-tab" role="tab" aria-selected="true" aria-controls="panel-parent" type="button">家長/照顧者</button>
        <button id="tab-teacher" class="role-tab" role="tab" aria-selected="false" aria-controls="panel-teacher" tabindex="-1" type="button">幼兒園教師</button>
        <button id="tab-medical" class="role-tab" role="tab" aria-selected="false" aria-controls="panel-medical" tabindex="-1" type="button">醫療行政</button>
      </div>

      <section id="panel-parent" class="role-panel" role="tabpanel" aria-labelledby="tab-parent">
        <form id="parent-form">
          <div class="field"><label for="child-id">孩童身分證字號 / 病歷號</label><input id="child-id" name="childIdentifier" placeholder="請輸入孩童身分證字號" autocomplete="off" maxlength="40" required></div>
          <div class="field"><label for="parent-phone">家長手機號碼</label><input id="parent-phone" name="phone" type="tel" placeholder="09XX-XXX-XXX" autocomplete="tel" inputmode="tel" maxlength="20" required><span id="phone-error" class="field-error" hidden>請輸入 09 開頭的 10 碼手機號碼。</span></div>
          <button class="primary-button" type="submit">取得驗證碼</button>
          <p class="form-status" role="status" hidden></p>
        </form>
      </section>

      <section id="panel-teacher" class="role-panel" role="tabpanel" aria-labelledby="tab-teacher" hidden>
        <p class="teacher-intro">老師您好，請輸入由家長提供給您的「專屬授權碼」即可登入填寫學校量表。</p>
        <form id="teacher-form">
          <div class="field"><label for="teacher-code">授權碼</label><input id="teacher-code" name="authorizationCode" placeholder="XXXX-XXXX-XXXX-XXXX" autocomplete="off" maxlength="80" spellcheck="false" aria-describedby="teacher-code-help" required><p class="field-help" id="teacher-code-help">開發測試：可展開下方「試做畫面預覽」產生測試授權碼</p></div>
          <button class="primary-button" type="submit">驗證授權碼</button>
          <p class="form-status" role="status" hidden></p>
        </form>
        <div class="divider"><span>或</span></div>
        <button id="scan-qr" class="secondary-button" type="button"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h4l2-3h4l2 3h4v13H4z"/><circle cx="12" cy="13" r="4"/></svg>掃描 QR Code</button>
        <p class="teacher-notice"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>授權不限時，提交成功或撤銷後失效</p>
        <div class="teacher-workspace-preview"><a class="secondary-button" href="/teacher-workspace.html">預覽教師填答工作台</a><p>前端操作示範，尚未驗證授權；請使用虛構資料。</p></div>
      </section>

      <section id="panel-medical" class="role-panel" role="tabpanel" aria-labelledby="tab-medical" hidden>
        <form id="medical-form">
          <div class="field"><label for="unit-code">單位代碼</label><input id="unit-code" name="unitCode" placeholder="請輸入單位代碼" autocomplete="off" maxlength="40" required></div>
          <div class="field"><label for="staff-account">醫護人員帳號</label><input id="staff-account" name="account" placeholder="請輸入醫護人員帳號" autocomplete="username" maxlength="80" required></div>
          <div class="field"><label for="staff-password">登入密碼</label><input id="staff-password" name="password" type="password" placeholder="請輸入登入密碼" autocomplete="current-password" maxlength="256" required></div>
          <button class="primary-button" type="submit">安全登入</button>
          <p class="form-status" role="status" hidden></p>
        </form>
      </section>

      <section id="teacher-flow" class="teacher-flow" aria-labelledby="teacher-state-title" hidden>
        <div id="teacher-verifying" class="teacher-state" hidden>
          <div class="authorization-spinner" aria-hidden="true"></div>
          <h2 id="verifying-title" tabindex="-1">正在驗證授權</h2>
          <p class="state-description">系統正在確認您的授權資訊<br>請稍候…</p>
          <p class="state-preview-note">正在查核開發測試授權；不代表已驗證家長同意</p>
          <button class="text-button" data-teacher-view="input" type="button">返回輸入授權碼</button>
        </div>
        <div id="teacher-failure" class="teacher-state" hidden>
          <span class="authorization-error-icon" aria-hidden="true">!</span>
          <h2 id="failure-title" tabindex="-1">授權無法使用</h2>
          <p class="state-description">此授權碼或 QR Code 已無法使用</p>
          <div class="failure-reasons"><p>可能原因：</p><ul><li>授權碼輸入有誤</li><li>授權已被使用</li><li>家長已取消授權</li></ul></div>
          <button class="primary-button" data-teacher-view="input" type="button">重新輸入授權碼</button>
          <p class="state-preview-note">授權碼不正確或已被撤銷時，無法取得填答任務</p>
        </div>
        <div id="teacher-scan" class="teacher-state scan-state" hidden>
          <h2 id="scan-title" tabindex="-1">掃描 QR Code</h2>
          <p class="state-description">請使用鏡頭掃描家長提供的 QR Code</p>
          <div class="scanner-frame" aria-label="QR Code 掃描框示意，相機尚未啟用" role="img">
            <span class="scanner-corner top-left"></span><span class="scanner-corner top-right"></span>
            <span class="scanner-corner bottom-left"></span><span class="scanner-corner bottom-right"></span>
            <span class="scanner-line"></span>
          </div>
          <p class="scan-help">將 QR Code 對準掃描框</p>
          <p class="state-preview-note">掃描畫面預覽，相機尚未啟用</p>
          <button class="secondary-button" data-teacher-view="input" type="button">使用授權碼</button>
        </div>
        <div id="teacher-unavailable" class="teacher-state" hidden>
          <span class="authorization-pending-icon" aria-hidden="true">i</span>
          <h2 id="unavailable-title" tabindex="-1">測試授權服務無法使用</h2>
          <p class="state-description">開發測試授權服務目前無法使用。<br>請確認專案以 Development 啟動，或稍後重試。</p>
          <button class="primary-button" data-teacher-view="input" type="button">返回輸入授權碼</button>
        </div>
      </section>

      <div class="faq-section" id="faq-section"><button id="open-faq" class="faq-button" type="button"><span class="help-icon" aria-hidden="true">?</span>系統操作說明與常見問題 (FAQ)</button></div>
    </section>

    <footer class="page-footer">
      <p>天主教輔仁大學附設醫院</p>
      <p>登入即表示您已閱讀本系統之 <button class="footer-link" data-info="privacy" type="button">隱私權政策</button> 與 <button class="footer-link" data-info="security" type="button">資料保護安全聲明</button></p>
      <p class="copyright" lang="en">© 2026 Fu Jen Catholic University Hospital. All rights reserved.</p>
      <p class="prototype-note">開發試做版：教師測試授權已啟用；家長與醫療登入尚未串接。</p>
      <details id="teacher-preview-controls" class="teacher-preview-controls" hidden>
        <summary>試做畫面預覽與測試授權</summary><button id="create-test-code" class="secondary-button" type="button">產生測試授權碼並填入</button><button id="show-teacher-invitation" class="secondary-button" type="button" hidden>查看邀請連結與 QR Code</button><p id="test-code-status" class="prototype-note" role="status" hidden></p>
        <nav aria-label="教師畫面預覽">
          <button data-teacher-view="input" type="button">授權碼輸入</button>
          <button data-teacher-view="verifying" type="button">驗證中</button>
          <button data-teacher-view="failure" type="button">驗證失敗</button>
          <button data-teacher-view="scan" type="button">QR Code 掃描</button>
        </nav>
      </details>
    </footer>
  </main>

  <dialog id="teacher-invitation-dialog" aria-labelledby="teacher-invitation-title">
    <div class="dialog-header"><h2 id="teacher-invitation-title">教師測試邀請</h2><button id="close-teacher-invitation" class="close-button" type="button" aria-label="關閉教師邀請">×</button></div>
    <p>開發測試：僅包含虛構個案，尚未驗證家長同意。連結開啟後，請按「驗證授權碼」。</p>
    <img id="teacher-invitation-qr" width="208" height="208" alt="教師邀請 QR Code，內容與下方邀請連結相同">
    <label for="teacher-invitation-url">邀請連結</label><textarea id="teacher-invitation-url" readonly rows="2" spellcheck="false"></textarea>
    <p class="invitation-local-note">目前是本機網址，手機掃描無法連到這台電腦。可先在同一台電腦開啟連結測試。</p>
    <div class="invitation-actions"><button id="copy-teacher-invitation" class="secondary-button" type="button">複製邀請連結</button><a id="open-teacher-invitation" class="secondary-button" target="_blank" rel="noopener noreferrer" referrerpolicy="no-referrer">開啟邀請連結</a></div>
    <p id="teacher-invitation-status" role="status"></p>
    <button id="teacher-invitation-done" class="primary-button" type="button">返回授權驗證</button>
  </dialog>
  <dialog id="info-dialog" aria-labelledby="dialog-title">
    <div class="dialog-header"><h2 id="dialog-title"></h2><button id="close-dialog" class="close-button" type="button" aria-label="關閉說明">×</button></div>
    <div id="dialog-content"></div>
    <button id="dialog-done" class="primary-button" type="button">我知道了</button>
  </dialog>
</body>
</html>
```

## EarlyInterventionCare.Api/wwwroot/teacher-workspace.html

```html
<!doctype html>
<html lang="zh-Hant">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>教師填答工作台｜早療智慧動態照顧系統</title>
<link rel="stylesheet" href="/css/teacher-workspace.css?v=20261006-hospital-mark">
<meta name="referrer" content="no-referrer">
<script src="/js/teacher-workspace.js?v=20261006-submit" defer></script>
</head>
<body>
<svg class="sprite" aria-hidden="true"><defs>
<symbol id="i-leaf" viewBox="0 0 24 24"><path d="M12 21v-9M12 14C4 14 3 8 4 3c7 0 10 5 8 11ZM12 17c0-7 4-10 9-9 0 6-3 10-9 9Z"/></symbol>
<symbol id="i-home" viewBox="0 0 24 24"><path d="m3 10 9-7 9 7v10H3Z"/><path d="M9 20v-7h6v7"/></symbol>
<symbol id="i-form" viewBox="0 0 24 24"><path d="M7 4H4v17h16V4h-3M8 2h8v5H8ZM8 11h8M8 15h8"/></symbol>
<symbol id="i-help" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M9 9a3 3 0 0 1 6 0c0 2-3 2-3 4M12 17h.01"/></symbol>
<symbol id="i-shield" viewBox="0 0 24 24"><path d="m12 3 8 3v6c0 5-8 9-8 9S4 17 4 12V6Z"/><path d="m8 12 3 3 5-6"/></symbol>
<symbol id="i-user" viewBox="0 0 24 24"><circle cx="12" cy="8" r="4"/><path d="M4 21v-2a8 8 0 0 1 16 0v2"/></symbol>
<symbol id="i-clock" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></symbol>
<symbol id="i-arrow" viewBox="0 0 24 24"><path d="M4 12h16m-6-6 6 6-6 6"/></symbol>
<symbol id="i-back" viewBox="0 0 24 24"><path d="M20 12H4m6-6-6 6 6 6"/></symbol>
<symbol id="i-check" viewBox="0 0 24 24"><path d="m5 12 4 4L19 6"/></symbol>
</defs></svg>
<div class="app">
<aside class="sidebar">
 <div class="brand"><span class="brandmark"><svg><use href="#i-leaf"/></svg></span><div><strong>早療智慧動態照顧</strong><small>教師填答工作台</small></div></div>
 <div><p class="side-label">教師填答工作台</p><nav class="nav" aria-label="主選單">
 <button type="button" data-nav="home" class="active" aria-current="page"><svg><use href="#i-home"/></svg>任務首頁</button>
 <button type="button" data-nav="tasks"><svg><use href="#i-form"/></svg>本次問卷<span class="badge" id="nav-count">2</span></button>
 <button type="button" data-nav="help"><svg><use href="#i-help"/></svg>填寫協助</button>
 </nav></div>
 <div class="side-note"><strong><svg><use href="#i-shield"/></svg>指定問卷授權規則（示意）</strong><p>僅提供本次指定問卷。授權不限時；本次指定問卷全部提交成功或家長撤銷授權後失效。</p><button type="button" class="demo-reset" id="reset-demo">重新體驗示範</button><a class="return-login" href="/?role=teacher">返回教師登入</a></div>
</aside>
<div class="workspace">
 <header class="topbar"><div class="hospital-brand"><img class="hospital-mark" src="/images/hospital-logo.png" alt="" width="325" height="247"><div><p class="hospital-name">天主教輔仁大學附設醫院</p><p class="hospital-english" lang="en">FU JEN CATHOLIC UNIVERSITY HOSPITAL</p></div></div><div class="breadcrumb">教師端 <span aria-hidden="true">／</span><b id="crumb">任務首頁</b></div><div class="session"><span class="session-dot"></span><span class="session-label" id="session-label">教師端畫面預覽</span><span class="demo">互動示範</span><span class="avatar" aria-label="教師身分">師</span></div></header>
 <main class="content">
 <section class="page" id="home-page" aria-labelledby="home-title">
  <div class="heading-row"><div><div class="eyebrow">TEACHER WORKSPACE</div><h1 id="home-title" tabindex="-1">教師填答工作台</h1><p class="prototype-disclaimer">目前為未驗證授權的畫面預覽，請使用虛構資料；示範完成不代表正式提交。</p><p class="subtitle">謝謝您提供校園觀察，讓孩子的評估資料更完整。</p></div><span class="date" id="today"></span></div>
  <div class="welcome"><div><span class="tag">每一份觀察，都讓我們更了解孩子</span><h2>老師您好，這裡是兩份評量表的填答預覽。</h2><p>請先確認幼兒資訊，再依各量表說明填答。填寫途中可以暫存本頁草稿，完成後再確認提交。</p></div><div class="welcome-art" aria-hidden="true"><span class="leaf"></span><span class="leaf two"></span><div class="art-sheet"><svg style="color:#86a47c"><use href="#i-form"/></svg><i></i><i></i><i></i></div><span class="art-check"><svg><use href="#i-check"/></svg></span></div></div>
  <div class="completion-banner" id="completion-banner" hidden>本次問卷已全部送出，填答授權已結束。謝謝老師的協助。</div>
  <div class="grid"><div>
   <div class="panel child"><div class="child-avatar"><svg><use href="#i-user"/></svg></div><div><div class="child-label">本次填答對象 · 示範資料</div><h3>王○安</h3><div class="child-meta"><span>個案代碼 DEMO-001</span><span>男 · 4 歲</span></div></div><div class="child-end"><strong>授權情境示意，尚未驗證</strong>僅顯示填答所需資訊</div></div>
   <div class="section-title" id="task-heading"><h2>本次待填問卷</h2><small id="task-summary">已完成 0 / 2 份</small></div>
   <div id="task-list"></div>
   <div class="privacy"><svg><use href="#i-shield"/></svg><span>教師端僅顯示本次授權內容；填答資料預計供獲授權的醫療團隊評估參考。</span></div>
  </div><aside class="aside">
   <div class="panel deadline"><div class="side-title"><svg><use href="#i-clock"/></svg>本次填答授權</div><div class="deadline-date" id="deadline-date"></div><div class="deadline-time">提交成功或家長撤銷後失效</div><div class="deadline-note"><svg><use href="#i-clock"/></svg><span id="deadline-note">正式版規則：全部提交成功或家長撤銷後失效。本頁尚未驗證或使用任何授權。</span></div></div>
   <div class="panel tips"><div class="side-title"><svg><use href="#i-help"/></svg>開始前的小提醒</div><ol><li><strong>確認填答對象</strong>確認本次幼兒資訊與家長邀請相符。</li><li><strong>依校園實際觀察填答</strong>各問卷的觀察期間與作答方式，SNAP-IV 依過去一星期；克氏量表依日常行為表現。</li><li><strong>送出前再看一次</strong>完成檢查並確認後，即結束該份問卷的填答。</li></ol><button type="button" class="btn ghost" id="help-link">查看填寫協助 <svg><use href="#i-arrow"/></svg></button></div>
  </aside></div>
 </section>
 <section class="page" id="form-page" hidden aria-labelledby="form-title">
  <button type="button" class="btn ghost back" id="back-home"><svg><use href="#i-back"/></svg>返回任務首頁</button>
  <div class="heading-row"><div><div class="eyebrow">QUESTIONNAIRE</div><h1 id="form-title" tabindex="-1"></h1><p class="subtitle">王○安 · DEMO-001 · 示範資料</p></div><span class="tag">量表填寫預覽</span></div>
  <div class="form-layout"><form class="panel form-card" id="questionnaire" novalidate><div class="form-intro" id="form-instructions"></div><div class="respondent-fields"><label><span class="field-label">填表人姓名 <span class="required">必填</span></span><input id="respondent-name" maxlength="50" autocomplete="off" placeholder="請輸入示範姓名" required></label><label><span class="field-label">填表日期 <span class="required">必填</span></span><input id="respondent-date" type="date" required></label><label><span class="field-label">與受試者的關係</span><input value="老師" readonly></label><label><span class="field-label">幼兒資訊（示範）</span><input value="王○安 / 男 / 4 歲" readonly></label></div><div id="questions"></div><label for="observation"><strong>補充校園觀察</strong><span class="hint">（選填）</span></label><p class="hint">此欄為平台另增的選填欄位，不屬於原量表題目。請使用虛構內容體驗。</p><textarea id="observation" maxlength="1000" placeholder="例如：在團體活動時，經老師提醒後能回到活動中……"></textarea><div class="error" id="form-error" role="alert" hidden></div><div class="form-actions"><span class="save-state" id="save-state" aria-live="polite">尚未儲存草稿</span><div style="display:flex;gap:9px"><button type="button" class="btn secondary" id="save-draft">儲存草稿</button><button type="submit" class="btn">檢查並送出 <svg><use href="#i-arrow"/></svg></button></div></div></form><aside class="panel form-side"><div class="side-title"><svg><use href="#i-form"/></svg>本份填答進度</div><div class="progress-label"><span id="form-progress-text"></span><span id="form-progress-percent"></span></div><div class="track"><span id="form-progress-bar"></span></div><p>本頁草稿可於同次預覽接續填寫，重新整理後清除。正式版授權不限時，提交成功後不可再修改。</p><p id="form-deadline"></p><button type="button" class="btn secondary" id="form-help">填寫協助</button></aside></div>
 </section>
 <section class="page" id="success-page" hidden aria-labelledby="success-title"><div class="panel success"><div class="success-icon"><svg><use href="#i-check"/></svg></div><div class="eyebrow">SUBMISSION COMPLETE</div><h1 id="success-title" tabindex="-1">本份問卷已完成</h1><p id="success-description"></p><div class="receipt"><div class="receipt-row"><span>填答對象</span><strong>王○安 · DEMO-001</strong></div><div class="receipt-row"><span>問卷名稱</span><strong id="receipt-form"></strong></div><div class="receipt-row"><span>填表人</span><strong id="receipt-respondent"></strong></div><div class="receipt-row"><span>填表日期</span><strong id="receipt-date"></strong></div><div class="receipt-row"><span>示範送出時間</span><strong id="receipt-time"></strong></div><div class="receipt-row"><span>本次完成進度</span><strong id="receipt-progress"></strong></div></div><p class="hint">這是前端操作示範，填答未傳送至醫院或任何伺服器，也未消耗正式授權。</p><div class="success-actions" id="success-actions"></div></div></section>
 <section class="page" id="help-page" hidden aria-labelledby="help-title"><div class="heading-row"><div><div class="eyebrow">FILLING GUIDE</div><h1 id="help-title" tabindex="-1">填寫協助</h1><p class="subtitle">讓您安心完成本次觀察填答。</p></div></div><div class="panel help-card"><h3>我可以分次完成嗎？</h3><p>可以先按「儲存草稿」，再透過原授權連結接續填寫；授權不限時，提交成功或家長撤銷後失效。這份示範的草稿僅暫存於本頁記憶體，重新整理、關閉頁面或離開後會清除；尚未提供跨次填答保存。</p><h3>幼兒資訊不符合，怎麼辦？</h3><p>請先停止填答，與提供邀請的家長確認，或透過正式平台提供的承辦窗口協助處理。</p><h3>送出後可以修改嗎？</h3><p>依目前規劃，確認送出後即結束該份問卷的填答權限。若需要更正，請聯絡承辦窗口，由授權流程另行處理。</p><h3>授權遭撤銷或已使用，怎麼辦？</h3><p>請聯絡提供邀請的家長或承辦窗口，確認是否需要重新授權。</p><h3>關於這份畫面提案</h3><p>幼兒資料為虛構範例；SNAP-IV 26 題與克氏行為量表 14 題依資料夾中的 PDF 整理，僅調整標點與換行。填表人、填表日期與幼兒資訊分開呈現。本頁不提供醫療判讀，也未串接正式提交服務。</p><button type="button" class="btn secondary" id="help-return">返回任務首頁</button><a class="btn secondary" href="/?role=teacher" style="margin:10px">返回教師登入</a><button type="button" class="btn ghost" id="help-reset" style="margin-left:16px">重新體驗示範</button></div></section>
 <footer class="footer"><span>天主教輔仁大學附設醫院 · 早療智慧動態照顧系統</span><span>教師前端試做版 · 草稿僅暫存本頁 · 尚未驗證授權或串接提交</span></footer>
 </main>
</div></div>
<dialog id="confirm-dialog" aria-labelledby="dialog-title"><h2 id="dialog-title">確認送出本份問卷？</h2><p>請確認您已依實際觀察完成填答。送出後，該份問卷的填答權限即結束。</p><div class="review-line" id="review-line"></div><p class="hint">此按鈕僅模擬送出，不會傳送任何資料。</p><div class="dialog-actions"><button type="button" class="btn secondary" id="cancel-submit">返回檢查</button><button type="button" class="btn" id="confirm-submit">確認送出</button></div></dialog>
<dialog id="reset-dialog" aria-labelledby="reset-title"><h2 id="reset-title">重新體驗示範</h2><p>這會清除本頁的示範草稿與完成狀態，回到初始畫面。</p><div class="dialog-actions"><button type="button" class="btn secondary" id="cancel-reset">保留目前進度</button><button type="button" class="btn" id="confirm-reset">重新開始</button></div></dialog>
<div class="toast" id="toast" role="status" hidden></div>

</body>
</html>
```
