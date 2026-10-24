const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..', 'EarlyInterventionCare.Api', 'wwwroot');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

async function loadStaffAuth(responses) {
    const protectedArea = { hidden: false, cleared: false, replaceChildren() { this.cleared = true; } };
    const errorArea = { hidden: true, textContent: '' };
    const redirects = [];
    const calls = [];
    const document = {
        querySelectorAll(selector) { return selector === '[data-staff-protected]' ? [protectedArea] : []; },
        querySelector(selector) { return selector === '[data-staff-auth-error]' ? errorArea : null; },
        dispatchEvent() {}
    };
    const window = {
        addEventListener() {},
        location: { origin: 'https://localhost:7127', replace: (url) => redirects.push(url) }
    };
    const context = {
        window,
        document,
        Headers, AbortController, DOMException, setTimeout, clearTimeout,
        URL,
        CustomEvent: class CustomEvent { constructor(type, init) { this.type = type; this.detail = init?.detail; } },
        fetch: async (url, options = {}) => {
            calls.push({ url, options });
            const next = responses.shift();
            if (next instanceof Error) throw next;
            return new Response(next?.body === undefined ? null : JSON.stringify(next.body), { status: next.status });
        }
    };
    vm.runInNewContext(read('js', 'medical-api.js'), context, { filename: 'medical-api.js' });
    vm.runInNewContext(read('js', 'staff-auth.js'), context, { filename: 'staff-auth.js' });
    await new Promise((resolve) => setImmediate(resolve));
    return { protectedArea, errorArea, redirects, calls, auth: window.StaffAuth };
}

async function submitStaffLogin(responses, input = {}) {
    const handlers = {};
    const makeElement = () => ({
        hidden: false,
        disabled: false,
        value: '',
        textContent: '',
        focus() {},
        addEventListener(type, handler) { handlers[this.name + ':' + type] = handler; }
    });
    const loginCard = { classList: { add() {}, remove() {} } };
    const loginButton = makeElement(); loginButton.name = 'loginButton';
    const loginForm = makeElement(); loginForm.name = 'loginForm';
    const verificationState = makeElement();
    const loginErrorState = makeElement();
    const errorMessage = makeElement();
    loginErrorState.querySelector = () => errorMessage;
    const retryLoginButton = makeElement(); retryLoginButton.name = 'retryLoginButton';
    const organizationCodeInput = makeElement(); organizationCodeInput.value = input.organizationCode ?? 'OFFLINE_ORG';
    const staffAccountInput = makeElement(); staffAccountInput.name = 'staffAccountInput'; staffAccountInput.value = input.username ?? 'staff';
    const staffPasswordInput = makeElement(); staffPasswordInput.name = 'staffPasswordInput'; staffPasswordInput.value = input.password ?? 'correct-password';
    const selectors = new Map([
        ['.login-card', loginCard], ['.login-button', loginButton], ['.login-form', loginForm],
        ['.verification-state', verificationState], ['.login-error-state', loginErrorState],
        ['.retry-login-button', retryLoginButton], ['#organization-code', organizationCodeInput],
        ['#staff-account', staffAccountInput], ['#staff-password', staffPasswordInput]
    ]);
    const calls = [];
    const redirects = [];
    const context = {
        window: { MedicalApiConfig: input.apiConfig, location: { origin: 'https://localhost:7127', assign: (url) => redirects.push(url) } },
        document: { querySelector: (selector) => selectors.get(selector) || null },
        Headers, URL, AbortController, DOMException, setTimeout, clearTimeout,
        fetch: async (url, options = {}) => {
            calls.push({ url, options });
            const next = responses.shift();
            return new Response(next?.body === undefined ? null : JSON.stringify(next.body), { status: next.status });
        }
    };
    vm.runInNewContext(read('js', 'medical-api.js'), context, { filename: 'medical-api.js' });
    vm.runInNewContext(read('js', 'staff-login.js'), context, { filename: 'staff-login.js' });
    handlers['loginForm:submit']({ preventDefault() {} });
    await new Promise((resolve) => setImmediate(resolve));
    return { calls, redirects, errorMessage, loginErrorState, staffPasswordInput };
}

const activeUser = { userId: '10000000-0000-4000-8000-000000000001', username: 'staff', role: 'MEDICAL_STAFF' };

test('staff login uses the cookie session endpoint without exposing a JWT', () => {
    const source = read('js', 'staff-login.js');
    assert.match(source, /fetch\("\/api\/auth\/staff-session"/);
    assert.doesNotMatch(source, /fetch\("\/api\/auth\/login"/);
    assert.doesNotMatch(source, /accessTokenInMemory|body\.accessToken|localStorage|sessionStorage|console\s*\./);
    assert.match(source, /credentials:\s*"same-origin"/);
    assert.match(source, /location\.assign\("\/staff-dashboard\.html"\)/);
});

test('單位代碼與帳號保留既有欄位並具有必填與最大長度', () => {
    const page = read('staff-login.html');
    assert.match(page, /<input[^>]+id="organization-code"[^>]+required[^>]+maxlength="50"/);
    assert.match(page, /<input[^>]+id="staff-account"[^>]+required[^>]+maxlength="100"/);
    assert.match(page, /href="staff-forgot-password\.html"/);
});

test('送出三個欄位：代碼與帳號去頭尾空白，密碼完全保留', async () => {
    const password = '  synthetic pass phrase  ';
    const state = await submitStaffLogin([
        { status: 401 },
        { status: 200, body: { ...activeUser, expiresAt: '2026-10-20T00:30:00Z' } }
    ], { organizationCode: '  offline_ORG  ', username: '  staff  ', password });
    assert.deepEqual(JSON.parse(state.calls[1].options.body), { organizationCode: 'offline_ORG', username: 'staff', password });
    assert.deepEqual(state.redirects, ['/staff-dashboard.html']);
});

for (const [label, input] of [
    ['缺少代碼', { organizationCode: '' }],
    ['空白代碼', { organizationCode: '   ' }],
    ['缺少帳號', { username: '' }],
    ['缺少密碼', { password: '' }],
    ['代碼超長', { organizationCode: 'a'.repeat(51) }],
    ['帳號超長', { username: 'a'.repeat(101) }]
]) {
    test(`登入欄位驗證阻止送出：${label}`, async () => {
        const state = await submitStaffLogin([], input);
        assert.equal(state.calls.length, 0);
        assert.equal(state.loginErrorState.hidden, false);
        assert.deepEqual(state.redirects, []);
    });
}

test('401 統一顯示登入失敗，不呈現後端院所或帳號細節，並清除密碼', async () => {
    const state = await submitStaffLogin([
        { status: 401 }, { status: 401, body: { message: 'synthetic internal organization detail' } }
    ]);
    assert.equal(state.errorMessage.textContent, '登入資料有誤，請確認後重新輸入。');
    assert.equal(state.staffPasswordInput.value, '');
    assert.deepEqual(state.redirects, []);
});

test('dashboard loads the common cookie authentication guard before exposing its workspace', () => {
    const page = read('staff-dashboard.html');
    assert.match(page, /data-staff-protected hidden/);
    assert.match(page, /data-staff-auth-error/);
    assert.match(page, /js\/staff-auth\.js/);
});

test('common staff authentication uses me, csrf and logout without script-readable token storage', () => {
    const source = read('js', 'staff-auth.js');
    assert.match(source, /fetch\("\/api\/auth\/me"/);
    assert.match(source, /fetch\("\/api\/auth\/csrf"/);
    assert.match(source, /X-EarlyCare-CSRF/);
    assert.match(source, /cookieFetch\("\/api\/auth\/logout", \{ method: "POST" \}\)/);
    assert.match(source, /credentials:\s*"same-origin"/);
    assert.match(source, /window\.location\.replace\("\/staff-login\.html"\)/);
    assert.doesNotMatch(source, /localStorage|sessionStorage|document\.cookie|Authorization\s*:/);
});

test('未登入時 Dashboard 隱藏受保護內容並導回登入頁', async () => {
    const state = await loadStaffAuth([{ status: 401 }]);
    assert.equal(state.protectedArea.hidden, true);
    assert.equal(state.protectedArea.cleared, true);
    assert.deepEqual(state.redirects, ['/staff-login.html']);
});

test('Dashboard API 回傳 401 時會要求重新登入', async () => {
    const state = await loadStaffAuth([{ status: 401 }]);
    assert.equal(state.calls[0].url, '/api/auth/me');
    assert.equal(state.redirects.length, 1);
});

test('Dashboard API 回傳 403 時顯示權限不足而不導向登入頁', async () => {
    const state = await loadStaffAuth([{ status: 403 }]);
    assert.equal(state.protectedArea.hidden, true);
    assert.equal(state.errorArea.hidden, false);
    assert.match(state.errorArea.textContent, /沒有使用醫療端工作區的權限/);
    assert.deepEqual(state.redirects, []);
});

test('正常登出取得 CSRF token 後清除畫面並返回登入頁', async () => {
    const state = await loadStaffAuth([
        { status: 200, body: activeUser },
        { status: 200, body: { requestToken: 'csrf-token' } },
        { status: 204 }
    ]);
    await state.auth.logout();
    assert.equal(state.calls[1].url, '/api/auth/csrf');
    assert.equal(state.calls[2].url, '/api/auth/logout');
    assert.equal(new Headers(state.calls[2].options.headers).get('X-EarlyCare-CSRF'), 'csrf-token');
    assert.equal(state.protectedArea.cleared, true);
    assert.deepEqual(state.redirects, ['/staff-login.html']);
});

test('CSRF 驗證失敗時登出不可假裝成功', async () => {
    const state = await loadStaffAuth([
        { status: 200, body: activeUser },
        { status: 200, body: { requestToken: 'csrf-token' } },
        { status: 403 }
    ]);
    await assert.rejects(state.auth.logout(), /未通過安全驗證/);
    assert.deepEqual(state.redirects, []);
    assert.equal(state.protectedArea.hidden, false);
});

test('登出 API 失敗時保留畫面並回報錯誤', async () => {
    const state = await loadStaffAuth([
        { status: 200, body: activeUser },
        { status: 200, body: { requestToken: 'csrf-token' } },
        { status: 503 }
    ]);
    await assert.rejects(state.auth.logout(), /登出服務暫時無法使用/);
    assert.deepEqual(state.redirects, []);
    assert.equal(state.protectedArea.hidden, false);
});

test('已有 Cookie 時重新登入會先取得並送出 CSRF token，且不讀取 Cookie 或 JWT', async () => {
    const source = read('js', 'staff-login.js');
    const state = await submitStaffLogin([
        { status: 200, body: { requestToken: 'csrf-token' } },
        { status: 200, body: { ...activeUser, permissions: [], expiresAt: '2026-10-15T00:30:00Z' } }
    ]);
    assert.match(source, /async function addCsrfForExistingSession/);
    assert.equal(state.calls[0].url, '/api/auth/csrf');
    assert.equal(state.calls[1].url, '/api/auth/staff-session');
    assert.equal(new Headers(state.calls[1].options.headers).get('X-EarlyCare-CSRF'), 'csrf-token');
    assert.deepEqual(state.redirects, ['/staff-dashboard.html']);
    assert.doesNotMatch(source, /document\.cookie|localStorage|sessionStorage|accessToken/);
});

test('醫療登入沿用同一 Base URL prefix，匿名 401 只略過 CSRF，不改走 JWT 登入', async () => {
    const state = await submitStaffLogin([
        { status: 401 }, { status: 200, body: { ...activeUser, expiresAt: '2026-10-24T00:30:00Z' } }
    ], { apiConfig: { apiBaseUrl: '/gateway' } });
    assert.deepEqual(state.calls.map(call => call.url), ['/gateway/api/auth/csrf', '/gateway/api/auth/staff-session']);
    assert.equal(new Headers(state.calls[1].options.headers).has('X-EarlyCare-CSRF'), false);
    assert.equal(state.calls[1].options.credentials, 'same-origin');
    assert.equal(state.calls[1].options.redirect, 'error');
    assert.deepEqual(state.redirects, ['/staff-dashboard.html']);
});

for (const status of [400, 403, 409, 429, 503]) {
    test(`登入 ${status} 不顯示任意後端訊息，也不導入 Demo`, async () => {
        const state = await submitStaffLogin([{ status: 401 }, { status, body: { message: 'SQL password=secret' } }]);
        assert.doesNotMatch(state.errorMessage.textContent, /SQL|secret/);
        assert.deepEqual(state.redirects, []); assert.equal(state.calls.length, 2);
    });
}

test('登入的 CSRF 503 或格式錯誤時不送密碼 POST', async () => {
    for (const response of [{ status: 503 }, { status: 200, body: {} }]) {
        const state = await submitStaffLogin([response]);
        assert.equal(state.calls.length, 1); assert.deepEqual(state.redirects, []);
    }
});

test('header delegates logout to the common session owner', () => {
    const source = read('js', 'staff-header.js');
    assert.match(source, /window\.StaffAuth\.logout\(\)/);
    assert.doesNotMatch(source, /window\.location\.assign\("staff-login\.html"\)/);
});

test('登出錯誤提示不再使用會被產品 CSS 隱藏的 demo class', () => {
    const page = read('staff-dashboard.html');
    const css = read('css', 'staff-header.css');
    assert.match(page, /id="staff-header-logout-feedback" class="header-logout-feedback"/);
    assert.match(css, /\.header-logout-feedback\s*\{\s*display: block;/);
    assert.match(css, /\.header-logout-feedback\[hidden\]\s*\{\s*display: none !important;/);
});
