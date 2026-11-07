'use strict';
// Consumer checks against unchanged legacy GET sources read at candidate
// 159c27f7790a77eca1ab2d746aac9fd7202b6688. No producer/RBAC/MySQL acceptance is implied.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const model = require('../EarlyInterventionCare.Api/wwwroot/js/medical-task-model.js');
const view = require('../EarlyInterventionCare.Api/wwwroot/js/medical-task-view.js');
const { caseId, tasks, completion } = require('./MedicalTaskFixtures.cjs');
const otherId = '20000000-0000-4000-8000-000000000002';
function fixture({ status = 200, rows = tasks, stats = completion, completionStatus = status } = {}) {
  const calls = [];
  const transport = api.createTransport({ origin: 'https://isolated.invalid', apiBaseUrl: '/gateway', fetchImpl: async (url, options) => {
    calls.push({ url, options }); const summary = url.endsWith('/completion');
    const code = summary ? completionStatus : status;
    return new Response(JSON.stringify(code === 200 ? summary ? stats : rows : { code: 'ISOLATED_ERROR', message: 'secret internal detail' }), { status: code });
  } });
  const client = api.createClient({ cookieFetch: transport.fetch });
  return { calls, client, store: model.createStore(model.createApiAdapter(client)) };
}
function deferred() { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; }
test('GET Task array and completion use the shared transport, fixed paths and safe cookie options', async () => {
  const f = fixture(); await f.store.load(caseId); const s = f.store.snapshot();
  assert.equal(s.phase, 'ready'); assert.deepEqual(s.rows.map(t => t.statusLabel), ['待填', '進行中', '已提交', '已取消']);
  assert.equal(s.rows[0].role, '家長'); assert.equal(s.rows[1].role, '教師'); assert.equal(s.rows[2].round, 3);
  assert.deepEqual(s.completion, completion);
  assert.deepEqual(f.calls.map(c => c.url).sort(), [`/gateway/api/cases/${caseId}/tasks`, `/gateway/api/cases/${caseId}/tasks/completion`]);
  for (const { options } of f.calls) {
    assert.equal(options.method, 'GET'); assert.equal(options.credentials, 'same-origin');
    assert.equal(options.cache, 'no-store'); assert.equal(options.redirect, 'error');
  }
});
test('200 [] yields empty state; no synthetic Task or locally inferred completion replaces the server', async () => {
  const f = fixture({ rows: [], stats: { ...completion, totalTaskCount: 0, requiredTaskCount: 0, requiredSubmittedCount: 0,
    requiredPendingCount: 0, requiredInProgressCount: 0, allRequiredSubmitted: false } });
  await f.store.load(caseId); assert.equal(f.store.snapshot().phase, 'empty'); assert.deepEqual(f.store.snapshot().rows, []);
  assert.equal(f.store.snapshot().completion.allRequiredSubmitted, false);
});
for (const status of [401, 403, 404, 503]) test(`${status}: Task and completion errors clear data, retain status and never fall back to Demo`, async () => {
  const f = fixture({ status }); await f.store.load(caseId); const s = f.store.snapshot();
  assert.equal(s.error.status, status); assert.equal(s.completionError.status, status); assert.deepEqual(s.rows, []); assert.equal(s.completion, null);
  assert.equal(f.calls.length, 2); assert.equal(f.calls.some(c => /demo/.test(c.url)), false);
  assert.equal(JSON.stringify(s).includes('secret internal detail'), false);
});
test('cross-case tasks, duplicate tasks and invalid statuses fail closed even with a direct Fake Adapter', async () => {
  for (const rows of [[{ ...tasks[0], caseId: otherId }], [tasks[0], tasks[0]], [{ ...tasks[0], taskStatus: 'ASSIGNED' }], { items: tasks }]) {
    const store = model.createStore({ list: async () => rows, completion: async () => completion }); await store.load(caseId);
    assert.equal(store.snapshot().phase, 'error'); assert.deepEqual(store.snapshot().rows, []);
  }
});
test('cross-case completion is rejected independently of valid Task rows', async () => {
  const f = fixture({ stats: { ...completion, caseId: otherId } }); await f.store.load(caseId);
  assert.equal(f.store.snapshot().phase, 'ready'); assert.equal(f.store.snapshot().completion, null);
  assert.equal(f.store.snapshot().completionPhase, 'error');
});
test('Demo/malformed Case IDs fail before either API request', async () => {
  const f = fixture(); for (const id of ['DEMO-001', '../other', '', null]) {
    await f.store.load(id); assert.equal(f.store.snapshot().error.status, 404);
    await assert.rejects(f.client.tasks(id), e => e.status === 400);
    await assert.rejects(f.client.taskCompletion(id), e => e.status === 400);
  }
  assert.equal(f.calls.length, 0);
});
test('Task DTO validates all wire fields and strips answers, scores, credentials and extra permissions', async () => {
  const dto = { ...tasks[0], answers: ['secret'], score: 99, token: 'secret', permissions: ['ADMIN'] };
  const value = api.taskDto(dto, caseId); assert.deepEqual(value, tasks[0]);
  for (const key of Object.keys(tasks[0])) {
    const missing = { ...tasks[0] }; delete missing[key]; assert.throws(() => api.taskDto(missing, caseId));
  }
  for (const patch of [{ assignmentRound: 4294967296 }, { assignmentRound: 1.1 }, { createdAtUtc: '2026-02-30T00:00:00Z' },
    { updatedAtUtc: '2026-10-24T01:00:00' }, { submittedAtUtc: '2026-10-24T01:00:00Z' }, { respondentRole: 'ADMIN' }])
    assert.throws(() => api.taskDto({ ...tasks[0], ...patch }, caseId));
  assert.equal(api.taskDto({ ...tasks[0], assignmentRound: 4294967295 }, caseId).assignmentRound, 4294967295);
  assert.throws(() => api.taskDto({ ...tasks[2], submittedAtUtc: null }, caseId));
});
test('Task dates display Taiwan time; null submittedAt remains missing and never means submitted', () => {
  assert.equal(model.toViewModel(tasks[0], caseId).submittedAt, '未提供');
  assert.match(model.toViewModel(tasks[2], caseId).submittedAt, /2026\/10\/25/);
  assert.equal(model.toViewModel(tasks[3], caseId).statusLabel, '已取消');
});
test('completion validates fields but does not invent effective rounds, percentages or cancellation denominators', async () => {
  for (const key of Object.keys(completion)) {
    const missing = { ...completion }; delete missing[key]; assert.throws(() => api.completionDto(missing, caseId));
  }
  for (const n of [-1, 1.5, '2', null, 2147483648]) assert.throws(() => api.completionDto({ ...completion, totalTaskCount: n }, caseId));
  const f = fixture({ stats: { ...completion, totalTaskCount: 40, requiredTaskCount: 30, requiredSubmittedCount: 10 } });
  await f.store.load(caseId); assert.equal(f.store.snapshot().completion.requiredTaskCount, 30);
  assert.equal('percent' in f.store.snapshot(), false); assert.equal('currentRound' in f.store.snapshot(), false);
});
test('completion 503 cannot cause locally calculated counts; valid tasks stay independently readable', async () => {
  const f = fixture({ completionStatus: 503 }); await f.store.load(caseId);
  assert.equal(f.store.snapshot().phase, 'ready'); assert.equal(f.store.snapshot().completion, null);
  assert.equal(f.store.snapshot().completionError.status, 503);
});

test('legacy completion preserves mixed rounds and required CANCELLED tasks without choosing an effective round', async () => {
  const rows = [tasks[0], tasks[2], { ...tasks[3], isRequired: true }, { ...tasks[1], isRequired: false }];
  const stats = { ...completion, requiredInProgressCount: 0 };
  const f = fixture({ rows, stats }); await f.store.load(caseId);
  const state = f.store.snapshot();
  assert.deepEqual(state.rows.map(row => row.round), [1, 3, 4, 2]);
  assert.equal(state.rows[2].status, 'CANCELLED'); assert.equal(state.rows[2].required, '必要');
  assert.equal(state.rows[3].required, '非必要'); assert.deepEqual(state.completion, stats);
  assert.equal(state.completion.allRequiredSubmitted, false);
  for (const key of ['effectiveRound', 'currentRound', 'optionalCount', 'cancelledRequiredCount', 'decision'])
    assert.equal(key in state.completion, false);
  assert.ok(f.calls.every(call => !call.url.includes('?')));
});

test('optional-only tasks remain visible while zero-required completion stays the explicit server value', async () => {
  const stats = { caseId, totalTaskCount: 1, requiredTaskCount: 0, requiredSubmittedCount: 0,
    requiredPendingCount: 0, requiredInProgressCount: 0, allRequiredSubmitted: false };
  const f = fixture({ rows: [{ ...tasks[0], isRequired: false }], stats }); await f.store.load(caseId);
  assert.equal(f.store.snapshot().phase, 'ready'); assert.equal(f.store.snapshot().rows[0].required, '非必要');
  assert.deepEqual(f.store.snapshot().completion, stats);
});

test('server allRequiredSubmitted is displayed with pending optional tasks without becoming a Workflow decision', async () => {
  const stats = { caseId, totalTaskCount: 2, requiredTaskCount: 1, requiredSubmittedCount: 1,
    requiredPendingCount: 0, requiredInProgressCount: 0, allRequiredSubmitted: true };
  const f = fixture({ rows: [tasks[2], { ...tasks[0], isRequired: false }], stats }); await f.store.load(caseId);
  const state = f.store.snapshot();
  assert.equal(state.rows[1].status, 'PENDING'); assert.equal(state.rows[1].required, '非必要');
  assert.deepEqual(state.completion, stats);
  assert.equal('caseStatus' in state, false); assert.equal('decision' in state, false);
  assert.ok(f.calls.every(call => call.options.method === 'GET'));
});

test('the internal candidate round DTO cannot silently replace the legacy HTTP completion contract', async () => {
  const candidate = { caseId, effectiveRound: 3, roundRevision: '1', requiredCount: 1, submittedRequiredCount: 1,
    pendingRequiredCount: 0, inProgressRequiredCount: 0, cancelledRequiredCount: 0, optionalCount: 1,
    allRequiredSubmitted: true, decision: 'Complete', pendingReasons: [], source: 'TransactionalRoundSnapshot',
    observedAtUtc: '2026-11-08T00:00:00Z', isCommitted: false };
  const f = fixture({ stats: candidate }); await f.store.load(caseId);
  assert.equal(f.store.snapshot().completionPhase, 'error'); assert.equal(f.store.snapshot().completion, null);
  assert.match(f.store.snapshot().completionError.message, /格式異常/);
});
test('a new Case selection clears rows and late list/completion cannot overwrite it', async () => {
  const lateList = deferred(), lateStats = deferred();
  const store = model.createStore({ list: id => id === caseId ? lateList.promise : tasks.map(t => ({ ...t, caseId: id })),
    completion: id => id === caseId ? lateStats.promise : { ...completion, caseId: id } });
  const old = store.load(caseId); const next = store.load(otherId); assert.deepEqual(store.snapshot().rows, []); await next;
  lateList.resolve(tasks); lateStats.resolve(completion); await old;
  assert.equal(store.snapshot().caseId, otherId); assert.ok(store.snapshot().rows.every(t => t.caseId === otherId));
  assert.equal(store.snapshot().completion.caseId, otherId);
});
test('pagehide/session invalidation prevents late results resurrecting Task data', async () => {
  const late = deferred(); let allowed = true;
  const store = model.createStore({ list: () => late.promise, completion: async () => completion }, { canRead: () => allowed });
  const pending = store.load(caseId); store.invalidate(); allowed = false; late.resolve(tasks); await pending;
  assert.deepEqual(store.snapshot().rows, []); assert.equal(store.snapshot().completion, null);
  await store.load(caseId); assert.equal(store.snapshot().error.status, 401);
});
test('401 from either endpoint clears both datasets and blocks the other delayed response', async () => {
  const late = deferred();
  const store = model.createStore({ list: () => late.promise, completion: async () => { throw new api.MedicalApiError(401); } });
  const pending = store.load(caseId); await Promise.resolve(); late.resolve(tasks); await pending;
  assert.deepEqual(store.snapshot().rows, []); assert.equal(store.snapshot().completion, null); assert.equal(store.snapshot().error.status, 401);
});
test('permission invalidation clears data immediately but preserves the triggering HTTP status for the safe error screen', async () => {
  for (const status of [0, 403, 503]) {
    const late = deferred(); let allowed = true;
    const store = model.createStore({ list: async () => { await late.promise; allowed = false; store.clearData(); throw new api.MedicalApiError(status); },
      completion: async () => completion }, { canRead: () => allowed });
    const pending = store.load(caseId); await Promise.resolve(); late.resolve(); await pending;
    assert.equal(store.snapshot().error.status, status); assert.deepEqual(store.snapshot().rows, []); assert.equal(store.snapshot().completion, null);
  }
});
// Minimal read/render DOM fixture: textContent is intentionally not interpreted as HTML.
class Element {
  constructor(tag, ownerDocument) { this.tag = tag; this.ownerDocument = ownerDocument; this.children = []; this.attributes = {}; this.text = ''; }
  set textContent(value) { this.text = String(value); this.children = []; }
  get textContent() { return this.text + this.children.map(c => c.textContent).join(''); }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.text = ''; this.children = nodes; }
  setAttribute(key, value) { this.attributes[key] = value; }
  addEventListener(name, fn) { (this.events ||= {})[name] = fn; }
}
const document = { createElement(tag) { return new Element(tag, document); } };
const all = node => [node, ...node.children.flatMap(all)];
test('renderer shows server statistics, real status labels, roles and safe text; no result or Demo links are invented', async () => {
  const f = fixture({ rows: [{ ...tasks[2], questionnaireTitle: '<img onerror=secret>' }] }); await f.store.load(caseId);
  const container = document.createElement('div'), summary = document.createElement('strong');
  const render = view.mount(container, summary, () => {}); render(f.store.snapshot());
  assert.equal(summary.textContent, '必要已提交 1 / 3'); assert.match(container.textContent, /已提交/);
  assert.match(container.textContent, /不代表評估完成或個案結案/); assert.match(container.textContent, /<img onerror=secret>/);
  assert.equal(all(container).some(e => ['a', 'img'].includes(e.tag)), false);
  render({ ...f.store.snapshot(), phase: 'error', rows: [], error: { message: '服務不可用' }, completionPhase: 'error', completion: null });
  assert.equal(container.textContent.includes('<img'), false); assert.equal(summary.textContent, '未提供');
});
test('renderer covers loading, empty and retry states without stale tables or fabricated zero counts', () => {
  const container = document.createElement('div'), summary = document.createElement('strong'); let retries = 0;
  const render = view.mount(container, summary, () => retries++);
  render({ phase: 'loading', rows: [], completionPhase: 'loading' }); assert.match(container.textContent, /正在載入/); assert.equal(summary.textContent, '載入中…');
  render({ phase: 'empty', rows: [], completionPhase: 'error', completionError: { message: '無權限' } });
  assert.match(container.textContent, /尚未指派/); assert.equal(summary.textContent, '未提供');
  all(container).find(e => e.tag === 'button').events.click(); assert.equal(retries, 1);
});
test('source wiring preserves API/Demo gates and does not add storage, a second HTTP client or backend policy edits', () => {
  const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
  const read = p => fs.readFileSync(path.join(root, p), 'utf8');
  for (const p of ['js/medical-task-model.js', 'js/medical-task-view.js', 'js/medical-case-detail.js'])
    assert.doesNotMatch(read(p), /\bfetch\s*\(|localStorage|sessionStorage|SharedDemo|MedicalDemo/);
  for (const p of ['case-detail.html', 'case-detail-empty.html']) {
    assert.ok(read(p).indexOf('js/medical-task-model.js') < read(p).indexOf('js/medical-case-detail.js'));
    assert.ok(read(p).indexOf('js/medical-task-view.js') < read(p).indexOf('js/medical-case-detail.js'));
  }
  assert.match(read('js/system-demo-ui.js'), /MedicalCaseMode\?\.mode==='api'\)return/);
  assert.match(read('js/medical-case-detail.js'), /taskStore\.invalidate/);
});
test('isolated HTTP host serves fixed Task paths and error/empty fixtures without a database', async () => {
  const { createMedicalCasePreviewServer } = require('./MedicalCasePreviewServer.cjs');
  const server = createMedicalCasePreviewServer(); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const origin = 'http://127.0.0.1:' + server.address().port;
    for (const [scenario, expected] of [['', 200], ['empty', 200], ['expired', 401], ['forbidden', 403], ['missing', 404], ['unavailable', 503]]) {
      for (const suffix of ['', '/completion']) {
        const response = await fetch(origin + `/api/cases/${caseId}/tasks` + suffix,
          { headers: { Referer: origin + '/case-detail.html?source=api&taskScenario=' + scenario } });
        assert.equal(response.status, expected); const data = await response.json();
        if (!scenario) assert.deepEqual(data, suffix ? completion : tasks);
        if (scenario === 'empty' && !suffix) assert.deepEqual(data, []);
      }
    }
  } finally { await new Promise(resolve => server.close(resolve)); }
});

test('actual Client and Task Store consume the synthetic HTTP host without any Demo fallback', async () => {
  const { createMedicalCasePreviewServer } = require('./MedicalCasePreviewServer.cjs');
  const server = createMedicalCasePreviewServer(); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const origin = 'http://127.0.0.1:' + server.address().port;
    for (const [scenario, status] of [['', 200], ['empty', 200], ['expired', 401], ['forbidden', 403], ['missing', 404], ['unavailable', 503]]) {
      const calls = [];
      const transport = api.createTransport({ origin, fetchImpl: (url, options) => {
        calls.push({ url, options });
        const headers = new Headers(options.headers);
        // Test-host scenario selection only; production code never sends this fixture parameter.
        headers.set('Referer', origin + '/case-detail.html?source=api&taskScenario=' + scenario);
        return fetch(origin + url, { ...options, headers });
      } });
      const store = model.createStore(model.createApiAdapter(api.createClient({ cookieFetch: transport.fetch })));
      await store.load(caseId); const state = store.snapshot();
      if (status === 200) {
        assert.equal(state.phase, scenario ? 'empty' : 'ready'); assert.equal(state.completionPhase, 'ready');
        assert.equal(state.rows.length, scenario ? 0 : 4);
      } else {
        assert.equal(state.error.status, status); assert.equal(state.completionError.status, status);
        assert.deepEqual(state.rows, []); assert.equal(state.completion, null);
      }
      assert.deepEqual(calls.map(call => call.url).sort(), [`/api/cases/${caseId}/tasks`, `/api/cases/${caseId}/tasks/completion`]);
      for (const call of calls) {
        assert.equal(call.options.method, 'GET'); assert.equal(call.options.credentials, 'same-origin');
        assert.equal(call.options.headers.has('Authorization'), false); assert.equal(call.options.headers.has('Cookie'), false);
      }
    }
  } finally { await new Promise(resolve => server.close(resolve)); }
});
