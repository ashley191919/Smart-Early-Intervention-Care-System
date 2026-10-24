const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const source = name => fs.readFileSync(path.join(root, name), 'utf8');
const user = { userId: '10000000-0000-4000-8000-000000000001', username: 'fixture', role: 'MEDICAL_STAFF' };
const tick = () => new Promise(resolve => setImmediate(resolve));
function deferred() { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; }

function setup() {
    const responses = [];
    const listeners = new Map();
    function on(type, handler) { if (!listeners.has(type)) listeners.set(type, []); listeners.get(type).push(handler); }
    function dispatch(event) { for (const handler of listeners.get(event.type) || []) handler(event); }
    function control(permission, mode = '') {
        const node = { disabled: true, hidden: mode === 'hide', dataset: { staffPermission: permission, permissionHint: permission, permissionMode: mode },
            attrs: {}, setAttribute(name, value) { this.attrs[name] = value; }, closest() { return this; } };
        return node;
    }
    const update = control('case.status.update');
    // There is no case status history UI yet; this is a test-only consumer of the shared contract.
    const history = control('case.status_history.read', 'hide');
    const unknown = control('case.create');
    const controls = [update, history, unknown];
    const hints = new Map(controls.map(c => [c.dataset.permissionHint, { textContent: '', hidden: false }]));
    const area = { hidden: true, replaceChildren() { this.cleared = true; } };
    const error = { hidden: true, textContent: '' };
    const redirects = [];
    const document = {
        querySelectorAll(s) { return s === '[data-staff-protected]' ? [area] : s === '[data-staff-permission]' ? controls : []; },
        querySelector() { return error; }, getElementById(id) { return hints.get(id); },
        addEventListener: on, dispatchEvent: dispatch
    };
    const window = { addEventListener: on, location: { origin: 'https://fixture.invalid', replace(url) { redirects.push(url); } } };
    const pending = deferred(); responses.push(pending.promise);
    const context = { document, window, Headers, URL, AbortController, DOMException, setTimeout, clearTimeout, CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init?.detail; } },
        fetch: async () => { const response = await responses.shift(); if (response instanceof Error) throw response; return response; } };
    for (const name of ['medical-api.js', 'staff-auth.js', 'staff-permissions.js']) vm.runInNewContext(source('js/' + name), context);
    async function login(body = user, status = 200) {
        pending.resolve(new Response(JSON.stringify(body), { status })); await tick();
    }
    return { window, auth: window.StaffAuth, update, history, unknown, hints, area, error, redirects, responses, dispatch, login };
}

test('pending /api/auth/me keeps controls disabled/hidden and hasPermission false', () => {
    const s = setup(); assert.equal(s.auth.getPermissionState(), 'pending');
    assert.equal(s.update.disabled, true); assert.equal(s.history.hidden, true);
    assert.equal(s.auth.hasPermission('case.status.update'), false);
    assert.match(s.hints.get('case.status.update').textContent, /確認操作權限/);
});

for (const role of ['MEDICAL_STAFF', 'CASE_MANAGER']) {
    test(`${role} with history only can see history but cannot update`, async () => {
        const s = setup(); await s.login({ ...user, role, permissions: ['case.status_history.read'] });
        assert.equal(s.update.disabled, true); assert.equal(s.history.hidden, false);
        assert.equal(s.auth.hasPermission('case.status.update'), false);
        assert.match(s.hints.get('case.status.update').textContent, /沒有此操作/);
    });
    test(`${role} with both exact permissions enables both controls`, async () => {
        const s = setup(); await s.login({ ...user, role, permissions: ['case.status_history.read', 'case.status.update'] });
        assert.equal(s.update.disabled, false); assert.equal(s.history.hidden, false);
        assert.equal(s.hints.get('case.status.update').hidden, true);
    });
}

for (const [label, permissions] of [['empty', []], ['missing', undefined], ['object', {}], ['string', 'case.status.update'],
    ['mixed list', ['case.status.update', null]], ['padded', ['case.status.update ']], ['wrong case', ['CASE.status.update']]]) {
    test(`${label} permissions fail closed, regardless of role`, async () => {
        const s = setup(); await s.login({ ...user, role: 'CASE_MANAGER', permissions });
        assert.equal(s.update.disabled, true); assert.equal(s.history.hidden, true);
    });
}

test('unregistered code never enables a control, even if returned in permissions', async () => {
    const s = setup(); await s.login({ ...user, permissions: ['case.create', 'case.status.update'] });
    assert.equal(s.unknown.disabled, true); assert.equal(s.auth.hasPermission('case.create'), false);
    assert.equal(s.update.disabled, false);
});

for (const status of [401, 403, 503]) {
    test(`/api/auth/me ${status} keeps all operations denied`, async () => {
        const s = setup(); await s.login({}, status);
        assert.equal(s.update.disabled, true); assert.equal(s.history.hidden, true); assert.equal(s.area.hidden, true);
        if (status === 401) assert.deepEqual(s.redirects, ['/staff-login.html']);
        else assert.equal(s.error.hidden, false);
    });
}

test('CSRF lookup failure revokes existing UI permission and never sends the write request', async () => {
    const s = setup(); await s.login({ ...user, permissions: ['case.status.update'] });
    s.responses.push(new Response(null, { status: 403 }));
    await assert.rejects(s.auth.cookieFetch('/api/cases/test/status', { method: 'PATCH' }), /安全驗證/);
    assert.equal(s.update.disabled, true); assert.equal(s.auth.hasPermission('case.status.update'), false);
    assert.equal(s.responses.length, 0);
});

test('network failure does not allow controls', async () => {
    const s = setup(); s.responses.push(new Error('offline'));
    await s.login({ ...user, permissions: ['case.status.update'] });
    await s.auth.requireStaffSession(); assert.equal(s.update.disabled, true);
});

for (const status of [401, 403, 503]) {
    test(`a later API ${status} invalidates previously granted operations`, async () => {
        const s = setup(); await s.login({ ...user, permissions: ['case.status.update'] });
        assert.equal(s.update.disabled, false);
        s.responses.push(new Response(null, { status })); await s.auth.cookieFetch('/api/cases/test/status-history');
        assert.equal(s.update.disabled, true); assert.equal(s.auth.hasPermission('case.status.update'), false);
        if (status === 401) assert.equal(s.redirects.at(-1), '/staff-login.html');
        if (status === 403) assert.match(s.hints.get('case.status.update').textContent, /沒有此操作的權限/);
    });
}

test('refresh pending, stale response, and bfcache never restore expired permissions', async () => {
    const s = setup(); await s.login({ ...user, permissions: ['case.status.update'] });
    const later = deferred(); s.responses.push(later.promise);
    const refresh = s.auth.requireStaffSession(); assert.equal(s.update.disabled, true);
    s.dispatch({ type: 'pagehide' });
    later.resolve(new Response(JSON.stringify({ ...user, permissions: ['case.status.update'] })));
    await refresh; assert.equal(s.update.disabled, true);
    s.responses.push(new Response(JSON.stringify({ ...user, permissions: [] })));
    s.dispatch({ type: 'pageshow', persisted: true }); await tick();
    assert.equal(s.update.disabled, true); assert.equal(s.auth.getPermissionState(), 'ready');
});

test('readonly consumer cannot mutate the authenticated permission snapshot or forge event detail', async () => {
    const s = setup(); await s.login({ ...user, permissions: ['case.status_history.read'] });
    assert.throws(() => s.auth.getCurrentUser().permissions.push('case.status.update'));
    s.dispatch({ type: 'staff-authenticated', detail: { ...user, permissions: ['case.status.update'] } });
    assert.equal(s.update.disabled, true);
});

test('denied controls stop activation and allowed controls keep their existing handler', async () => {
    const s = setup(); let stopped = false;
    const event = { type: 'click', target: s.update, preventDefault() { stopped = true; }, stopImmediatePropagation() {} };
    s.dispatch(event); assert.equal(stopped, true);
    await s.login({ ...user, permissions: ['case.status.update'] }); stopped = false;
    s.dispatch(event); assert.equal(stopped, false);
});

test('status UI defaults disabled and auth scripts load before its demo consumer', () => {
    const page = source('case-edit.html');
    assert.match(page, /<select[^>]*id="status"[^>]*disabled[^>]*data-staff-permission="case.status.update"/);
    assert.ok(page.indexOf('js/staff-auth.js') < page.indexOf('js/staff-permissions.js'));
    assert.ok(page.indexOf('js/staff-permissions.js') < page.indexOf('js/case-edit.js'));
    assert.match(source('css/staff-dashboard.css'), /\[data-staff-protected\]\[hidden\]\s*\{\s*display:\s*none !important/);
    assert.doesNotMatch(source('js/case-edit.js'), /fetch\(/);
});
