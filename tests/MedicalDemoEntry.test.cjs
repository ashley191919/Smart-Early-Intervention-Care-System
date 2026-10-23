'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot/js/medical-demo-client.js'), 'utf8');
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, '../EarlyInterventionCare.Api/Development/MedicalDemoFixture.json'), 'utf8'));
const query = '?source=medical-demo&caseId=DEMO-001&taskId=' + fixture.id;
const settle = () => new Promise(setImmediate);
function mount({ status = 200, authenticated = true, delayed = false } = {}) {
    let session = authenticated ? { userId: 'isolated-medical' } : null;
    const handlers = new Map(), calls = [], redirects = [], pending = [];
    let rechecks = 0;
    const document = { addEventListener: (name, handler) => handlers.set(name, handler),
        dispatchEvent: event => handlers.get(event.type)?.(event) };
    const window = { StaffAuth: { getCurrentUser: () => session, requireStaffSession: async () => { rechecks++; } },
        location: { assign: value => redirects.push(value) }, addEventListener: (name, handler) => handlers.set(name, handler) };
    const reply = url => ({ ok: status === 200, status, json: async () => url.endsWith('/tasks') ? [{ ...fixture, draft: undefined }] : fixture });
    vm.runInNewContext(source, { document, window, URLSearchParams, Event, fetch: async (url, options) => {
        calls.push({ url, options }); return delayed ? new Promise(resolve => pending.push(() => resolve(reply(url)))) : reply(url);
    } });
    return { calls, redirects, pending, handlers, window, get rechecks() { return rechecks; },
        authenticate() { session = { userId: 'isolated-medical' }; handlers.get('staff-authenticated')(); },
        expire() { session = null; handlers.get('staff-permissions-changed')(); } };
}
test('normal case entry cannot request answers before authentication and waits for the server', async () => {
    const f = mount({ authenticated: false, delayed: true });
    await assert.rejects(f.window.MedicalDemo.resolve([], query)); assert.equal(f.calls.length, 0);
    f.authenticate(); const pending = f.window.MedicalDemo.resolve([], query);
    assert.equal(f.calls.length, 1); f.pending.shift()();
    assert.equal((await pending).task.draft.questions.length, 26);
    assert.deepEqual(f.redirects, []);
});
test('normal case list and original-answer link work without a Demo switch or additional redirect', async () => {
    const f = mount(); const entries = await f.window.MedicalDemo.records([], 'DEMO-001', '');
    const href = f.window.MedicalDemo.responseHref(entries[0], '');
    assert.equal(href.includes('medicalDemo='), false);
    assert.equal((await f.window.MedicalDemo.resolve([], new URL(href, 'http://isolated.invalid').search)).task.draft.questions.length, 26);
    assert.deepEqual(f.redirects, []);
    assert.equal(f.calls.length, 2);
    assert.equal(f.calls[1].options.credentials, 'same-origin'); assert.equal(f.calls[1].options.cache, 'no-store');
});
for (const status of [401, 403, 404, 503]) test(`server ${status} never supplies fallback answers through normal case navigation`, async () => {
    const f = mount({ status }); await assert.rejects(f.window.MedicalDemo.resolve([], query));
    assert.deepEqual(f.redirects, []); assert.equal(f.rechecks, status === 401 ? 1 : 0);
});
test('expired session invalidates pending answers and a late successful lookup cannot restore them', async () => {
    const f = mount({ delayed: true }); const pending = f.window.MedicalDemo.resolve([], query);
    f.expire(); f.pending.shift()(); await assert.rejects(pending, /Stale/); assert.deepEqual(f.redirects, []);
});
test('pagehide revokes pending answers without storing state', async () => {
    const f = mount({ delayed: true }); const pending = f.window.MedicalDemo.resolve([], query);
    f.handlers.get('pagehide')(); f.pending.shift()(); await assert.rejects(pending, /Stale/);
    assert.doesNotMatch(source, /localStorage|sessionStorage/);
});
test('Production 404 preserves existing local tasks but never inserts a synthetic result', async () => {
    const f = mount({ status: 404 });
    const tasks = [{ id: 'existing', caseId: 'DEMO-001', draft: { answers: { 0: 2 } } }];
    const before = JSON.stringify(tasks), entries = await f.window.MedicalDemo.records(tasks, 'DEMO-001', '');
    assert.equal(entries.length, 1); assert.equal(entries[0].source, 'local-demo');
    assert.equal(JSON.stringify(tasks), before);
});
