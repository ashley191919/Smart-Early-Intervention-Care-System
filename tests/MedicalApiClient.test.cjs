'use strict';
// Isolated fetch/DOM adapters only. No API Program, secrets, sockets or MySQL.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const source = file => fs.readFileSync(path.join(root, file), 'utf8');
const ids = { case: '20000000-0000-4000-8000-000000000001', other: '20000000-0000-4000-8000-000000000002',
  task: '50000000-0000-4000-8000-000000000001', questionnaire: '30000000-0000-4000-8000-000000000001', version: '40000000-0000-4000-8000-000000000001' };
const user = { userId: '10000000-0000-4000-8000-000000000007', username: 'synthetic', role: 'CASE_MANAGER', permissions: ['case.read'] };
const caseRecord = { caseId: ids.case, caseCode: 'CASE-TEST-001', childName: '虛構測試幼兒', caseStatus: 'WAITING_FORM',
  updatedAtUtc: '2026-10-21T00:00:00Z', nationalId: 'synthetic-private', address: 'synthetic-private' };
const task = { taskId: ids.task, caseId: ids.case, questionnaireId: ids.questionnaire, questionnaireVersionId: ids.version,
  questionnaireTitle: '測試問卷', versionNumber: '1.0.0', respondentRole: 'PARENT', assignmentRound: 1, isRequired: true,
  taskStatus: 'SUBMITTED', createdAtUtc: '2026-10-20T00:00:00Z', updatedAtUtc: '2026-10-21T00:00:00Z', submittedAtUtc: '2026-10-21T00:00:00Z' };
const json = (body, status = 200) => new Response(JSON.stringify(body), { status });
const rejected = (status, code) => error => error.name === 'MedicalApiError' && error.status === status && (!code || error.code === code);
const tick = () => new Promise(resolve => setImmediate(resolve));

function fixture(responses, config = {}) {
  const calls = [];
  const transport = api.createTransport({ origin: 'https://medical.invalid', ...config, fetchImpl: async (url, options) => {
    calls.push({ url, options });
    const next = responses.shift();
    if (next instanceof Error) throw next;
    assert.ok(next, 'Unexpected network request'); return next;
  } });
  return { transport, calls, client: api.createClient({ cookieFetch: transport.fetch }) };
}

test('shared same-origin Base URL applies to auth and clinical requests with forced Cookie/cache/redirect safety', async () => {
  const f = fixture([json(user), json([caseRecord])], { apiBaseUrl: '/gateway/' });
  await f.transport.fetch('/api/auth/me', { credentials: 'include', redirect: 'follow', cache: 'force-cache' });
  await f.client.cases();
  assert.deepEqual(f.calls.map(c => c.url), ['/gateway/api/auth/me', '/gateway/api/cases?page=1&pageSize=10']);
  for (const c of f.calls) {
    assert.equal(c.options.credentials, 'same-origin'); assert.equal(c.options.cache, 'no-store'); assert.equal(c.options.redirect, 'error');
    assert.equal(c.options.headers.get('Accept'), 'application/json'); assert.equal(c.options.headers.has('Authorization'), false);
  }
});

test('unapproved origins, credentials, query/hash and network-path prefixes fail before fetch', () => {
  for (const apiBaseUrl of ['https://other.invalid', '//other.invalid', 'https://user:pass@medical.invalid', '/?token=x', '/#x',
    'https://medical.invalid//other.invalid', '/gateway/%2f/']) {
    assert.throws(() => api.createTransport({ origin: 'https://medical.invalid', apiBaseUrl, fetchImpl: () => assert.fail() }), rejected(0, 'API_ORIGIN_NOT_APPROVED'));
  }
  assert.throws(() => api.createTransport({ origin: 'file:///local' }), rejected(0));
});

test('transport rejects external, traversing and Demo paths without any network request', async () => {
  const f = fixture([]);
  for (const target of ['https://other.invalid/api/auth/logout', '//other.invalid/api/x', '/api/../x', '/api/%2e%2e/x', '/api/medical-demo/cases/DEMO-001/tasks'])
    await assert.rejects(f.transport.fetch(target), rejected(0, 'API_ORIGIN_NOT_APPROVED'));
  assert.equal(f.calls.length, 0);
});

test('Case and Task consumer accepts existing DTO subsets and strips unnecessary private fields/answers', async () => {
  const f = fixture([json([caseRecord]), json(caseRecord), json([{ ...task, answers: ['private'], observation: 'private' }])]);
  const list = await f.client.cases(); assert.equal(list.length, 1); assert.equal('nationalId' in list[0], false); assert.equal('address' in list[0], false);
  assert.equal((await f.client.caseDetail(ids.case)).caseId, ids.case);
  const tasks = await f.client.tasks(ids.case); assert.equal(tasks[0].taskStatus, 'SUBMITTED'); assert.equal('answers' in tasks[0], false); assert.equal('observation' in tasks[0], false);
  assert.equal('login' in f.client, false); assert.equal('logout' in f.client, false); assert.equal('me' in f.client, false);
});

test('empty arrays remain empty, with no synthetic replacement or retained previous data', async () => {
  const f = fixture([json([caseRecord]), json([]), json([])]);
  await f.client.cases(); assert.deepEqual(await f.client.cases(), []); assert.deepEqual(await f.client.tasks(ids.case), []);
});

for (const status of [400, 401, 403, 404, 409, 429, 503]) {
  test(`${status}: JSON/non-JSON errors retain status and never surface server secrets or return Demo`, async () => {
    for (const response of [json({ code: 'SERVICE_UNAVAILABLE', message: 'password=secret SQL' }, status), new Response('<h1>password=secret SQL</h1>', { status })]) {
      const f = fixture([response]);
      await assert.rejects(f.client.cases(), error => rejected(status)(error) && !/secret|SQL|<h1>/.test(error.message));
      assert.equal(f.calls.length, 1); assert.equal(f.calls[0].url.includes('demo'), false);
    }
  });
}

test('200 HTML/null/invalid shape is not a successful clinical response', async () => {
  for (const response of [new Response('<html>login</html>'), json(null), json({ items: [] })]) {
    const f = fixture([response]); await assert.rejects(f.client.cases(), rejected(0, 'INVALID_RESPONSE'));
  }
});

test('mismatched Case, duplicates and malformed task status/version/submission time fail closed', async () => {
  const f = fixture([json({ ...caseRecord, caseId: ids.other }), json([caseRecord, caseRecord]), json([task, task])]);
  await assert.rejects(f.client.caseDetail(ids.case), rejected(0, 'INVALID_RESPONSE'));
  await assert.rejects(f.client.cases(), rejected(0, 'INVALID_RESPONSE'));
  await assert.rejects(f.client.tasks(ids.case), rejected(0, 'INVALID_RESPONSE'));
  for (const patch of [{ caseId: ids.other }, { taskStatus: 'done' }, { taskStatus: 'PENDING' }, { questionnaireVersionId: '' },
    { assignmentRound: 0 }, { submittedAtUtc: null }, { respondentRole: 'ADMIN' }])
    assert.throws(() => api.taskDto({ ...task, ...patch }, ids.case), rejected(0, 'INVALID_RESPONSE'));
});

test('invalid IDs and paging do not transmit requests', async () => {
  const f = fixture([]);
  for (const value of ['DEMO-001', '../../auth', '00000000-0000-0000-0000-000000000000'])
    await assert.rejects(f.client.caseDetail(value), rejected(400));
  await assert.rejects(f.client.cases(0), rejected(400)); await assert.rejects(f.client.cases(1, 101), rejected(400));
  assert.equal(f.calls.length, 0);
});

test('network/timeout/cancellation are typed, safe and never retried', async () => {
  const f = fixture([new Error('secret SQL')]); await assert.rejects(f.client.cases(), rejected(0, 'NETWORK_ERROR')); assert.equal(f.calls.length, 1);
  const transport = api.createTransport({ origin: 'https://medical.invalid', timeoutMs: 10, fetchImpl: (url, { signal }) => new Promise((resolve, reject) => {
    if (signal.aborted) reject(new Error('aborted'));
    else signal.addEventListener('abort', () => reject(new Error('aborted')), { once: true });
  }) });
  await assert.rejects(transport.fetch('/api/auth/me'), rejected(0, 'REQUEST_TIMEOUT'));
  const controller = new AbortController(); controller.abort();
  await assert.rejects(transport.fetch('/api/auth/me', { signal: controller.signal }), { name: 'AbortError' });
});

function staffFixture(responses) {
  const calls = [], redirects = [];
  const area = { hidden: true, replaceChildren() { this.cleared = true; } };
  const window = { MedicalApiConfig: { apiBaseUrl: '/gateway', timeoutMs: 1000 }, location: { origin: 'https://medical.invalid', replace: url => redirects.push(url) }, addEventListener() {} };
  const context = vm.createContext({ window, URL, Headers, AbortController, DOMException, setTimeout, clearTimeout,
    CustomEvent: class { constructor(type) { this.type = type; } },
    document: { querySelectorAll: () => [area], querySelector: () => ({ hidden: true }), dispatchEvent() {} },
    fetch: async (url, options) => { calls.push({ url, options }); assert.ok(responses.length, 'Unexpected network request'); return responses.shift(); } });
  for (const script of ['medical-api.js', 'staff-auth.js']) vm.runInContext(source('js/' + script), context);
  return { context, calls, redirects, area, auth: window.StaffAuth };
}

test('browser default consumer delegates to existing StaffAuth rather than owning another Session', async () => {
  const s = staffFixture([json(user), json([caseRecord])]); await tick();
  const result = await vm.runInContext('window.MedicalApi.createClient().cases()', s.context);
  assert.equal(result[0].caseId, ids.case);
  assert.equal(s.calls[1].url, '/gateway/api/cases?page=1&pageSize=10');
  assert.equal(s.auth.getCurrentUser().userId, user.userId);
});

test('real StaffAuth owns Session and CSRF; unsafe request validates target before CSRF lookup', async () => {
  const s = staffFixture([json(user), json({ requestToken: 'synthetic-csrf' }), new Response(null, { status: 204 })]); await tick();
  await assert.rejects(s.auth.cookieFetch('https://other.invalid/api/auth/logout', { method: 'POST' }), rejected(0));
  assert.equal(s.calls.length, 1);
  await s.auth.logout();
  assert.deepEqual(s.calls.map(c => c.url), ['/gateway/api/auth/me', '/gateway/api/auth/csrf', '/gateway/api/auth/logout']);
  assert.equal(new Headers(s.calls[2].options.headers).get('X-EarlyCare-CSRF'), 'synthetic-csrf');
  assert.deepEqual(s.redirects, ['/staff-login.html']); assert.equal(s.area.cleared, true);
});

for (const status of [401, 403, 503]) {
  test(`clinical ${status} through actual StaffAuth revokes current permissions with no retry or Demo`, async () => {
    const s = staffFixture([json(user), json({ code: 'SERVICE_UNAVAILABLE' }, status)]); await tick();
    assert.equal(s.auth.hasPermission('case.read'), true);
    const client = api.createClient({ cookieFetch: (...args) => s.auth.cookieFetch(...args) });
    await assert.rejects(client.cases(), rejected(status)); assert.equal(s.auth.getCurrentUser(), null);
    assert.equal(s.auth.hasPermission('case.read'), false); assert.equal(s.calls.length, 2);
    assert.equal(s.redirects.length, status === 401 ? 1 : 0);
  });
}

test('missing/forbidden CSRF never sends logout, and cannot report success', async () => {
  for (const response of [json({}, 403), json({}), new Response('not JSON', { status: 503 })]) {
    const s = staffFixture([json(user), response]); await tick();
    await assert.rejects(s.auth.logout()); assert.equal(s.calls.length, 2); assert.deepEqual(s.redirects, []);
  }
});

test('all medical Session entry pages load config and transport once before Session owner, without changing parent/teacher assets', () => {
  const pages = ['staff-login.html', 'staff-dashboard.html', 'cases.html', 'case-detail.html', 'case-detail-empty.html', 'case-edit.html', 'response-detail.html'];
  for (const page of pages) {
    const html = source(page);
    assert.equal((html.match(/src="js\/medical-config\.js/g) || []).length, 1, page);
    assert.equal((html.match(/src="js\/medical-api\.js/g) || []).length, 1, page);
    assert.ok(html.indexOf('js/medical-config.js') < html.indexOf('js/medical-api.js'), page);
    assert.ok(html.indexOf('js/medical-api.js') < html.indexOf(page === 'staff-login.html' ? 'js/staff-login.js' : 'js/staff-auth.js'), page);
  }
  for (const file of ['js/medical-api.js', 'js/medical-config.js'])
    assert.doesNotMatch(source(file), /localStorage|sessionStorage|SharedDemo|document\.cookie|console\s*\./);
  assert.match(source('js/staff-login.js'), /new window\.MedicalApi\.MedicalApiError/);
});
