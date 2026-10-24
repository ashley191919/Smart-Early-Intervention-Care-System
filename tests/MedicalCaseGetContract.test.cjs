'use strict';
// Consumer contract supplied by the user for 11 commit 0e4e16939b2fb711190aa490ca80098f0615ff47.
// That commit is NOT locally available. These tests do not execute or verify its producer.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const model = require('../EarlyInterventionCare.Api/wwwroot/js/medical-case-model.js');
const { records } = require('./MedicalCaseFixtures.cjs');
const fields = ['caseId', 'caseCode', 'childName', 'birthDate', 'sex', 'nationalId', 'address', 'caseStatus', 'revision', 'createdAtUtc', 'updatedAtUtc'];
const record = { ...records[0], childName: '王測安', sex: 'MALE', revision: 4294967295 };
const rejected = status => error => error.name === 'MedicalApiError' && error.status === status;
function fakeApi({ status = 200, body, wrongId = false } = {}) {
  const calls = [];
  const transport = api.createTransport({ origin: 'https://case-test.invalid', fetchImpl: async (url, options) => {
    calls.push({ url, options });
    const parsed = new URL(url, 'https://case-test.invalid');
    const list = parsed.pathname === '/api/cases';
    let result = body === undefined ? list ? [record] : { ...record, ...(wrongId ? { caseId: records[1].caseId } : {}) } : body;
    return new Response(JSON.stringify(result), { status });
  } });
  return { calls, client: api.createClient({ cookieFetch: transport.fetch }) };
}

test('fixed full CaseDto[] and single CaseDto are consumed with unchanged route identity and exact uint metadata', async () => {
  const f = fakeApi(); const list = await f.client.cases(), detail = await f.client.caseDetail(record.caseId);
  assert.deepEqual(Object.keys(detail).sort(), fields.slice().sort()); assert.deepEqual(list[0], detail);
  assert.equal(detail.revision, 4294967295); assert.equal(detail.birthDate, '2021-05-12'); assert.equal(detail.sex, 'MALE');
  assert.equal(f.calls[1].url, '/api/cases/' + record.caseId);
});

test('default page=1/pageSize=20 and explicit page/pageSize up to 100 are transmitted as the only list parameters', async () => {
  const f = fakeApi(); await f.client.cases(); await f.client.cases(3, 100); await f.client.cases(2, 10); await f.client.cases(100001, 20);
  assert.deepEqual(f.calls.map(c => c.url), ['/api/cases?page=1&pageSize=20', '/api/cases?page=3&pageSize=100', '/api/cases?page=2&pageSize=10', '/api/cases?page=100001&pageSize=20']);
  for (const call of f.calls) {
    assert.equal(call.options.method, 'GET'); assert.equal(call.options.credentials, 'same-origin');
    assert.equal(call.options.redirect, 'error'); assert.equal(call.options.cache, 'no-store');
  }
});

test('200 [] is empty; paged envelopes with totalCount are not silently accepted as CaseDto[]', async () => {
  assert.deepEqual(await fakeApi({ body: [] }).client.cases(), []);
  await assert.rejects(fakeApi({ body: { items: [record], totalCount: 1000 } }).client.cases(), rejected(0));
  const store = model.createStore(model.createApiAdapter(fakeApi({ body: [] }).client)); await store.list();
  assert.equal(store.snapshot().phase, 'empty'); assert.equal(store.snapshot().pageSize, 20); assert.equal('totalCount' in store.snapshot(), false);
});

test('each of the eleven required DTO fields must be present; no partial record or invented replacement is displayed', async () => {
  for (const field of fields) {
    const missing = { ...record }; delete missing[field];
    await assert.rejects(fakeApi({ body: [missing] }).client.cases(), rejected(0), field);
    await assert.rejects(fakeApi({ body: missing }).client.caseDetail(record.caseId), rejected(0), field);
  }
});

test('revision obeys uint range and is never coerced from string/fraction/negative/overflow', () => {
  assert.equal(api.caseDto({ ...record, revision: 0 }).revision, 0);
  for (const revision of [-1, 0.5, '1', null, 4294967296, Number.MAX_SAFE_INTEGER])
    assert.throws(() => api.caseDto({ ...record, revision }), rejected(0));
});

test('DateOnly calendar dates and UTC timestamps are validated without inventing a local timezone', () => {
  assert.equal(api.caseDto({ ...record, birthDate: '2024-02-29', updatedAtUtc: '2026-10-24T01:00:00.1234567+00:00' }).birthDate, '2024-02-29');
  for (const birthDate of ['2023-02-29', '2021-02-30', '2021-5-12', '0000-01-01', null])
    assert.throws(() => api.caseDto({ ...record, birthDate }), rejected(0));
  for (const updatedAtUtc of ['2026-10-24T09:00:00+08:00', '2026-10-24T01:00:00', '2026-02-30T01:00:00Z', '2026-10-24T24:00:00Z'])
    assert.throws(() => api.caseDto({ ...record, updatedAtUtc }), rejected(0));
});

test('client redacts names, national IDs and addresses before ViewModel/storage; repeated validation never reveals them', async () => {
  const detail = await fakeApi().client.caseDetail(record.caseId);
  assert.equal(detail.childName, '王○安'); assert.equal(detail.nationalId, 'A12*******'); assert.equal(detail.address, '新北市○○區○○路');
  assert.deepEqual(api.caseDto(detail), detail);
  const vm = model.toViewModel(detail); assert.equal(vm.name, '王○安'); assert.equal(vm.birthDate, '2021/05/12'); assert.equal(vm.sex, '男');
  for (const privateValue of [record.childName, record.nationalId, record.address]) {
    assert.equal(JSON.stringify(detail).includes(privateValue), false); assert.equal(JSON.stringify(vm).includes(privateValue), false);
  }
  assert.equal(api.caseDto({ ...record, childName: '王安', nationalId: '', address: '' }).childName, '王○');
  assert.equal(api.caseDto({ ...record, childName: '安' }).childName, '○');
  assert.equal(api.caseDto({ ...record, address: '無法識別的虛構地址999號' }).address, '已遮罩');
});

test('unsupported UI fields remain missing; extra role/permission/organization fields confer nothing', () => {
  const value = api.caseDto({ ...record, organizationId: records[1].caseId, role: 'ADMIN', permissions: ['case.write'], contactName: 'private contact' });
  const vm = model.toViewModel(value);
  for (const field of ['contactName', 'relationship', 'phone', 'email']) assert.equal(vm[field], '未提供');
  for (const field of ['organizationId', 'role', 'permissions', 'canEdit']) assert.equal(field in value, false);
  assert.equal(vm.priority, null);
});

test('DateOnly never shifts when updated UTC time crosses the Taiwan calendar boundary', () => {
  const vm = model.toViewModel({ ...record, birthDate: '2024-02-29', updatedAtUtc: '2026-10-24T23:30:00Z' });
  assert.equal(vm.birthDate, '2024/02/29');
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Taipei', year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(vm.updatedAt));
  const taiwan = Object.fromEntries(parts.map(p => [p.type, p.value]));
  assert.deepEqual([taiwan.year, taiwan.month, taiwan.day, taiwan.hour, taiwan.minute], ['2026', '10', '25', '07', '30']);
  assert.equal(vm.createdAtUtc, record.createdAtUtc);
});

test('blank optional personal strings and missing non-contract UI fields render as missing, never undefined/null or made-up records', async () => {
  const f = fakeApi({ body: { ...record, nationalId: '', address: '   ', sex: 'UNKNOWN' } });
  const vm = model.toViewModel(await f.client.caseDetail(record.caseId));
  for (const field of ['nationalId', 'address', 'contactName', 'relationship', 'phone', 'email']) assert.equal(vm[field], '未提供');
  assert.equal(vm.sex, '未知'); assert.equal(vm.birthDate, '2021/05/12');
  assert.equal(Object.values(vm).includes(undefined), false);
});

test('null in required wire fields is a safe error state, not a rendering exception or reuse of a previous child', async () => {
  for (const field of ['birthDate', 'nationalId', 'address', 'revision', 'createdAtUtc', 'updatedAtUtc']) {
    const f = fakeApi({ body: { ...record, [field]: null } });
    const store = model.createStore(model.createApiAdapter(f.client)); await store.detail(record.caseId);
    assert.equal(store.snapshot().phase, 'error', field); assert.equal(store.snapshot().detail, null);
    assert.match(store.snapshot().error.message, /格式異常/);
  }
});

for (const status of [401, 403, 404, 503]) {
  test(`${status} list/detail errors reject once without Demo fallback or cached clinical records`, async () => {
    const f = fakeApi({ status, body: { code: 'FIXTURE_ERROR', message: 'private internal detail' } });
    await assert.rejects(f.client.cases(), rejected(status)); await assert.rejects(f.client.caseDetail(record.caseId), rejected(status));
    assert.equal(f.calls.length, 2); assert.equal(f.calls.some(c => /demo/i.test(c.url)), false);
    const store = model.createStore(model.createApiAdapter(f.client)); await store.detail(record.caseId);
    assert.equal(store.snapshot().detail, null); assert.equal(store.snapshot().error.status, status);
  });
}

test('UUID paths reject DEMO-001 and malformed IDs before network; a different Case UUID is never displayed', async () => {
  const f = fakeApi({ wrongId: true });
  for (const id of ['DEMO-001', '../other', '00000000-0000-0000-0000-000000000000'])
    await assert.rejects(f.client.caseDetail(id), rejected(400));
  assert.equal(f.calls.length, 0);
  await assert.rejects(f.client.caseDetail(record.caseId), rejected(0));
  const store = model.createStore(model.createApiAdapter(f.client)); await store.detail(record.caseId); assert.equal(store.snapshot().detail, null);
});
