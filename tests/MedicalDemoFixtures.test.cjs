'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.join(__dirname, '../EarlyInterventionCare.Api');
const read = file => fs.readFileSync(path.join(root, 'wwwroot', file), 'utf8');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-demo-client.js');
const fixture = JSON.parse(fs.readFileSync(path.join(root, 'Development/MedicalDemoFixture.json'), 'utf8'));
const mode = '?medicalDemo=1';
const query = mode + '&source=medical-demo&caseId=DEMO-001&taskId=' + api.taskId;
function setup(status = 200) {
    const calls = []; let session = { userId: 'medical-test' }, expired = 0;
    const client = api.createClient({ getSession: () => session, expired: () => { expired++; session = null; },
        request: async (url, options) => {
            calls.push({ url, options });
            return { status, ok: status === 200, json: async () => url.endsWith('/availability') ? { enabled: true, label: api.label } :
                url.endsWith('/DEMO-024/tasks') ? [] : url.endsWith('/tasks') ? [{ ...fixture, draft: undefined }] : fixture };
        } });
    return { client, calls, clear: () => { session = null; client.invalidate(); }, get expired() { return expired; } };
}
test('synthetic answers exist only outside wwwroot and match the previous 26-question snapshot', () => {
    assert.equal(fs.existsSync(path.join(root, 'wwwroot/js/medical-demo-fixtures.js')), false);
    const snap = JSON.parse(fs.readFileSync(path.join(root, 'Development/SnapQuestionnaire.json'), 'utf8'));
    assert.deepEqual(fixture.draft.questions, snap.questions); assert.deepEqual(fixture.draft.options, snap.options);
    assert.equal(Object.keys(fixture.draft.answers).length, 26);
    snap.questions.forEach((_, i) => assert.equal(fixture.draft.answers[i], i % snap.options.length));
    assert.equal(fixture.scoring, undefined); assert.equal(fixture.draft.totalScore, undefined);
    for (const text of [fixture.draft.questions[0], fixture.draft.note, fixture.draft.respondent])
        assert.equal(read('js/medical-demo-client.js').includes(text), false);
});
test('normal DEMO-001 navigation requires the server but no query switch; unrelated cases never request Demo', async () => {
    const s = setup(); assert.equal((await s.client.records([], 'DEMO-001', '')).length, 1);
    assert.equal(s.calls[0].url, '/api/medical-demo/cases/DEMO-001/tasks');
    assert.equal(s.calls[0].options.credentials, 'same-origin'); assert.equal(s.calls[0].options.cache, 'no-store');
    const other = setup(); assert.deepEqual(await other.client.records([], 'REAL-CASE', ''), []); assert.equal(other.calls.length, 0);
});
for (const status of [401, 403, 404, 503]) test(`server ${status} cannot enable an entry or return fallback answers`, async () => {
    const s = setup(status);
    await assert.rejects(s.client.availability()); await assert.rejects(s.client.resolve([], query));
    assert.equal(s.expired, status === 401 ? 1 : 0);
});
test('anonymous and invalidated sessions cannot request answers', async () => {
    const s = setup(); s.clear(); await assert.rejects(s.client.resolve([], query)); assert.equal(s.calls.length, 0);
});
test('foreign case, absent switch, wrong source and duplicate parameters are refused before fetching', async () => {
    for (const search of [query.replace('DEMO-001', 'DEMO-024'), query.replace('medicalDemo=1', 'medicalDemo=0'),
        query.replace('source=medical-demo', 'source=api'), query + '&taskId=other', query + '&source=medical-demo',
        query + '&caseId=DEMO-001', query.replace(api.taskId, 'unknown')]) {
        const s = setup(); assert.equal(await s.client.resolve([], search), null); assert.equal(s.calls.length, 0);
    }
});
test('case-scoped display preserves local tasks and DEMO-024 is empty', async () => {
    const tasks = [{ id: 'local', caseId: 'DEMO-001', status: 'pending', draft: null },
        { id: 'other', caseId: 'DEMO-002', status: 'done' }];
    const before = JSON.stringify(tasks), s = setup();
    const entries = await s.client.records(tasks, 'DEMO-001', mode);
    assert.equal(entries.length, 2); assert.strictEqual(entries[0].task, tasks[0]);
    assert.deepEqual(await s.client.records(tasks, 'DEMO-024', mode), []); assert.equal(JSON.stringify(tasks), before);
    assert.doesNotMatch(read('js/medical-demo-client.js'), /localStorage|sessionStorage|\.save\(|\.reset\(/);
});
test('returned detail is source-bound; normal legacy task links stay readable', async () => {
    const s = setup(); assert.equal((await s.client.resolve([], query)).task.draft.questions.length, 26);
    const local = { id: 'local', caseId: 'DEMO-001' };
    assert.strictEqual((await s.client.resolve([local], '?taskId=local')).task, local);
    assert.equal(await s.client.resolve([local], '?taskId=local&caseId=DEMO-024'), null);
    const href = api.responseHref({ task: fixture, source: 'medical-demo' }, mode);
    assert.equal((await s.client.resolve([], new URL(href, 'http://fixture.invalid').search)).task.id, fixture.id);
    assert.equal(api.caseHref('DEMO-001', ''), 'case-detail.html#shared-tasks');
    assert.equal(new URL(href, 'http://fixture.invalid').searchParams.has('medicalDemo'), false);
});
test('session invalidation rejects an in-flight response instead of restoring old answers', async () => {
    let complete;
    const client = api.createClient({ getSession: () => ({ userId: 'test' }), request: () => new Promise(resolve => { complete = resolve; }) });
    const pending = client.resolve([], query); client.invalidate();
    complete({ ok: true, status: 200, json: async () => fixture }); await assert.rejects(pending, /Stale/);
});
async function listPage(tasks, pathname = '/case-detail.html', search = '') {
    const s = setup(), elements = new Map(), counters = { strong: {}, small: {} };
    const target = { replaceChildren: panel => elements.set('panel', panel) };
    const document = { querySelector: selector => ({ '#main-content': {}, '#detail-panel-forms': target,
        '.detail-summary-card:nth-child(2)': { querySelector: tag => counters[tag] }, '.detail-demo-note': {} })[selector] || null,
        createElement: () => ({}), addEventListener() {} };
    const window = { MedicalDemo: s.client, StaffAuth: { getCurrentUser: () => ({ userId: 'test' }) },
        SharedDemo: { tasks: () => tasks }, addEventListener() {} };
    vm.runInNewContext(read('js/system-demo-ui.js'), { window, document, location: { pathname, search, hash: '' }, URLSearchParams });
    await new Promise(setImmediate); return { panel: elements.get('panel'), counters };
}
test('actual list displays server-confirmed demo result plus existing tasks and preserves empty case', async () => {
    const local = [{ id: 'local', caseId: 'DEMO-001', name: '原任務', role: 'teacher', status: 'pending' }];
    const full = await listPage(local); assert.equal(full.counters.strong.textContent, '1 / 2');
    assert.match(full.panel.innerHTML, /Demo 模擬資料/); assert.match(full.panel.innerHTML, /source=medical-demo/);
    const empty = await listPage(local, '/case-detail-empty.html'); assert.match(empty.panel.innerHTML, /目前尚未指派任何表單/);
    assert.doesNotMatch(empty.panel.innerHTML, /data-demo-result|SNAP-IV|王○安/);
});
test('medical pages use existing staff guard and versioned client, never public fixture script', () => {
    for (const page of ['staff-dashboard.html', 'cases.html', 'case-detail.html', 'case-detail-empty.html', 'response-detail.html']) {
        const html = read(page);
        assert.match(html, /data-staff-protected hidden/); assert.match(html, /src="js\/staff-auth.js\?/);
        if (!['staff-dashboard.html', 'cases.html'].includes(page)) assert.match(html, /medical-demo-client.js\?v=20261023-medical-case-demo2/);
        assert.doesNotMatch(html, /medical-demo-fixtures.js|data-medical-demo-entry|查看示範個案/);
    }
});
