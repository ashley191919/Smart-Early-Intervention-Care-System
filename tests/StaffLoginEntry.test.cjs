'use strict';

// Node VM + explicit event/DOM doubles, NOT a browser or real authentication test.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createPreviewServer } = require('./StaffLoginEntryPreviewServer.cjs');
const root = path.resolve(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

class Element {
    constructor(id) {
        this.id = id; this.handlers = new Map(); this.attributes = {}; this.value = '';
        this.hidden = false; this.textContent = ''; this.resetCount = 0;
    }
    addEventListener(type, handler, options) {
        const handlers = this.handlers.get(type) || [];
        handlers.push({ handler, capture: options === true || options?.capture === true });
        this.handlers.set(type, handlers);
    }
    setAttribute(key, value) { this.attributes[key] = value; }
    removeAttribute(key) { delete this.attributes[key]; }
    getAttribute(key) { return this.attributes[key]; }
    reset() { this.resetCount++; }
    focus() { this.focused = true; }
    querySelector(selector) { return this.queries?.[selector] || null; }
    querySelectorAll(selector) { return this.queryLists?.[selector] || []; }
}

function fixture({ search = '', pathname = '/index.html', scripts = ['login.js', 'system-demo-ui.js'] } = {}) {
    const ids = new Map();
    const element = id => { const value = new Element(id); ids.set(id, value); return value; };
    const document = new Element('document');
    const window = new Element('window');
    const redirects = [], events = [], network = [];
    const location = { pathname, search, assign: url => redirects.push(url), replace: url => redirects.push(url) };
    window.location = location;
    const roles = ['parent', 'teacher', 'medical'];
    const tabs = roles.map(role => { const tab = element('tab-' + role); tab.setAttribute('aria-controls', 'panel-' + role); return tab; });
    const panels = roles.map(role => {
        const panel = element('panel-' + role), form = element(role + '-form'), status = element(role + '-status');
        panel.hidden = role !== 'parent';
        panel.queries = { form }; panel.queryLists = { '.form-status': [status], '[aria-invalid]': [] };
        form.queries = { '.form-status': status }; return panel;
    });
    const password = element('staff-password'); password.value = 'synthetic-legacy-value';
    const account = element('staff-account'); account.value = 'synthetic-account';
    const parentPhone = element('parent-phone'); parentPhone.value = 'synthetic-parent-input';
    const teacherCode = element('teacher-code'); teacherCode.value = 'synthetic-teacher-input';
    const link = element('medical-link'); link.href = '/staff-login.html';
    ids.get('medical-form').queries['a[href="/staff-login.html"]'] = link;
    for (const id of ['info-dialog', 'phone-error', 'faq-section', 'open-faq', 'close-dialog', 'dialog-done', 'dialog-title', 'dialog-content']) element(id);
    ids.get('info-dialog').showModal = () => {};
    ids.get('info-dialog').close = () => {};
    document.querySelector = selector => selector === '#medical-form #staff-password' ? password : ids.get(selector.slice(1)) || null;
    document.querySelectorAll = selector => selector === '.role-tab' ? tabs : selector === '.role-panel' ? panels : [];
    document.dispatchEvent = event => { events.push(event); };
    document.createElement = () => new Element('created');
    const context = vm.createContext({ document, window, location, URLSearchParams,
        CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init?.detail; } },
        fetch: (...args) => { network.push(args); throw new Error('Unexpected API call from a legacy entry'); },
        localStorage: { setItem() { throw new Error('Unexpected credential persistence'); } },
        sessionStorage: { setItem() { throw new Error('Unexpected credential persistence'); } }
    });
    for (const script of scripts) vm.runInContext(read('js/' + script), context, { filename: script });
    function emit(target, type, properties = {}) {
        const event = { type, target, defaultPrevented: false, stopped: false,
            preventDefault() { this.defaultPrevented = true; },
            stopImmediatePropagation() { this.stopped = true; }, ...properties };
        const invoke = (host, capture) => {
            for (const entry of host.handlers.get(type) || []) {
                if (entry.capture !== capture) continue;
                entry.handler(event); if (event.stopped) break;
            }
        };
        if (target !== document && target !== window) invoke(document, true);
        if (!event.stopped) invoke(target, false);
        if (!event.stopped && target !== document && target !== window) invoke(document, false);
        return event;
    }
    return { ids, tabs, panels, password, account, parentPhone, teacherCode, link,
        document, window, redirects, events, network, emit };
}

test('preview meta refresh and no-script link both target staff login', () => {
    const html = read('frontend-preview.html');
    assert.match(html, /http-equiv="refresh" content="0;url=\/staff-login\.html"/);
    assert.match(html, /href="\/staff-login\.html"/);
    assert.doesNotMatch(html, /role=medical|staff-dashboard/);
});

test('index medical workspace link targets login; entry scripts load once with fresh versions', () => {
    const html = read('index.html');
    const medical = html.match(/<form id="medical-form">([\s\S]*?)<\/form>/)[1];
    assert.match(medical, /href="\/staff-login\.html"/);
    assert.doesNotMatch(medical, /staff-dashboard/);
    for (const script of ['login.js', 'system-demo-ui.js']) {
        const urls = [...html.matchAll(/<script\b[^>]*src="([^"]+)"[^>]*>/g)].map(match => match[1]);
        assert.equal(urls.filter(url => url.startsWith('/js/' + script + '?')).length, 1);
        assert.ok(urls.includes('/js/' + script + '?v=20261019-staff-entry'));
    }
});

test('medical role click clears only the legacy password and redirects once', () => {
    const f = fixture(); f.password.value = 'synthetic-value';
    f.emit(f.ids.get('tab-medical'), 'click');
    assert.deepEqual(f.redirects, ['/staff-login.html']);
    assert.equal(f.password.value, ''); assert.equal(f.account.value, 'synthetic-account');
    assert.equal(f.parentPhone.value, 'synthetic-parent-input');
    assert.equal(f.teacherCode.value, 'synthetic-teacher-input');
    assert.equal(f.network.length, 0);
});

for (const pathname of ['/', '/index.html']) {
    test(pathname + '?role=medical redirects directly without a login API call', () => {
        const f = fixture({ pathname, search: '?role=medical' });
        assert.deepEqual(f.redirects, ['/staff-login.html']); assert.equal(f.network.length, 0);
    });
}

for (const role of ['parent', 'teacher']) {
    test(role + ' role remains selectable without medical redirection', () => {
        const f = fixture(); f.emit(f.ids.get('tab-' + role), 'click');
        assert.deepEqual(f.redirects, []);
        assert.equal(f.ids.get('panel-' + role).hidden, false);
        assert.equal(f.ids.get('tab-' + role).getAttribute('aria-selected'), 'true');
        assert.equal(f.events.at(-1).detail, 'tab-' + role);
    });
    test('?role=' + role + ' retains the intended shared login panel', () => {
        const f = fixture({ search: '?role=' + role });
        assert.deepEqual(f.redirects, []); assert.equal(f.ids.get('panel-' + role).hidden, false);
    });
    test(role + ' form handler runs once and is not intercepted by the medical scripts', () => {
        const f = fixture(); let submits = 0;
        const form = f.ids.get(role + '-form');
        form.addEventListener('submit', event => { submits++; event.preventDefault(); });
        f.emit(form, 'submit');
        assert.equal(submits, 1); assert.deepEqual(f.redirects, []);
    });
}

for (const [tab, key] of [['teacher', 'ArrowRight'], ['parent', 'ArrowLeft'], ['parent', 'End']]) {
    test('keyboard ' + tab + ' ' + key + ' reaches staff login once', () => {
        const f = fixture(); f.password.value = 'synthetic-value';
        const event = f.emit(f.ids.get('tab-' + tab), 'keydown', { key });
        assert.equal(event.defaultPrevented, true); assert.equal(f.password.value, '');
        assert.deepEqual(f.redirects, ['/staff-login.html']);
    });
}

test('keyboard navigation between parent and teacher retains normal panel selection', () => {
    const f = fixture(); f.emit(f.ids.get('tab-parent'), 'keydown', { key: 'ArrowRight' });
    assert.deepEqual(f.redirects, []); assert.equal(f.ids.get('tab-teacher').focused, true);
    assert.equal(f.ids.get('panel-teacher').hidden, false);
});

for (const scripts of [['login.js', 'system-demo-ui.js'], ['system-demo-ui.js', 'login.js']]) {
    test('legacy form has one submit owner: ' + scripts.join(' then '), () => {
        const f = fixture({ scripts }); f.password.value = 'demo';
        const form = f.ids.get('medical-form');
        assert.equal(form.handlers.get('submit').length, 1);
        const event = f.emit(form, 'submit');
        assert.equal(event.defaultPrevented, true); assert.equal(f.password.value, '');
        assert.deepEqual(f.redirects, ['/staff-login.html']); assert.equal(f.network.length, 0);
    });
}

test('medical link clears the password without cancelling native link navigation', () => {
    const f = fixture(); f.password.value = 'synthetic-value';
    const event = f.emit(f.link, 'click');
    assert.equal(f.password.value, ''); assert.equal(event.defaultPrevented, false);
    assert.equal(f.link.href, '/staff-login.html'); assert.deepEqual(f.redirects, []);
});

for (const type of ['pagehide', 'pageshow']) {
    test(type + ' clears a restored legacy password without changing other form inputs', () => {
        const f = fixture(); f.password.value = 'synthetic-restored-value';
        f.emit(f.window, type, { persisted: true });
        assert.equal(f.password.value, ''); assert.equal(f.account.value, 'synthetic-account');
        assert.equal(f.parentPhone.value, 'synthetic-parent-input');
        assert.equal(f.teacherCode.value, 'synthetic-teacher-input'); assert.deepEqual(f.redirects, []);
    });
}

test('initial load clears a restored legacy password', () => assert.equal(fixture().password.value, ''));

test('unrelated links and login-style buttons are not intercepted', () => {
    const f = fixture(); const other = new Element('unrelated-link');
    const event = f.emit(other, 'click');
    assert.equal(event.defaultPrevented, false); assert.deepEqual(f.redirects, []);
    assert.equal(f.document.handlers.get('click')?.length || 0, 0);
});

test('fixture UI has no login interception and preview fallback cannot enter the dashboard', () => {
    const source = read('js/system-demo-ui.js');
    assert.doesNotMatch(source, /function medicalLogin|password\.value|unit\.value|account\.value/);
    assert.doesNotMatch(source, /location\.(assign|replace)\(['"]\/staff-dashboard\.html/);
    assert.equal(fixture({ pathname: '/frontend-preview.html', scripts: ['system-demo-ui.js'] }).redirects[0], '/staff-login.html');
});

test('legacy entry scripts neither call authentication APIs nor persist credentials', () => {
    for (const file of ['js/login.js', 'js/system-demo-ui.js']) {
        assert.doesNotMatch(read(file), /\bfetch\s*\(|localStorage|sessionStorage|document\.cookie|accessToken/);
    }
});

test('dedicated staff flow still uses cookie login, CSRF and authenticated me, not these legacy scripts', () => {
    const page = read('staff-login.html'), login = read('js/staff-login.js'), auth = read('js/staff-auth.js');
    assert.match(page, /src="js\/staff-login\.js\?v=/);
    assert.doesNotMatch(page, /src="[^\"]*(?:system-demo-ui|\/login)\.js/);
    assert.match(login, /fetch\("\/api\/auth\/staff-session"/);
    assert.match(login, /fetch\("\/api\/auth\/csrf"/); assert.match(login, /X-EarlyCare-CSRF/);
    assert.match(login, /credentials:\s*"same-origin"/);
    assert.match(auth, /fetch\("\/api\/auth\/me"/);
    assert.match(auth, /"CASE_MANAGER", "MEDICAL_STAFF"/);
    assert.doesNotMatch(login + auth, /localStorage|sessionStorage|document\.cookie/);
});

test('static HTTP fixture serves real entries, reports missing login.html, and never emulates API authentication', async () => {
    const server = createPreviewServer();
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    try {
        const origin = 'http://127.0.0.1:' + server.address().port;
        for (const file of ['/', '/index.html', '/frontend-preview.html', '/staff-login.html'])
            assert.equal((await fetch(origin + file)).status, 200);
        for (const file of ['/login.html', '/login.html?role=medical'])
            assert.equal((await fetch(origin + file)).status, 404);
        const result = await fetch(origin + '/api/auth/staff-session', { method: 'POST' });
        assert.equal(result.status, 503); assert.equal((await result.json()).code, 'STATIC_PREVIEW_ONLY');
        assert.equal(result.headers.get('set-cookie'), null);
    } finally { await new Promise(resolve => server.close(resolve)); }
});
