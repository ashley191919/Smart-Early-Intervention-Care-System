'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const model = require('../EarlyInterventionCare.Api/wwwroot/js/medical-case-model.js');
const demo = require('../EarlyInterventionCare.Api/wwwroot/js/case-demo-data.js');
const { records, FakeCaseAdapter } = require('./MedicalCaseFixtures.cjs');
const root = path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
function deferred() { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; }

test('route separates explicit API selection and defaults to existing Demo without permitting a downgrade', () => {
  assert.equal(model.route('').mode, 'demo');
  assert.equal(model.route('', { caseSource: 'api' }).mode, 'api');
  assert.equal(model.route('?source=api').mode, 'api');
  for (const search of ['?caseId=' + records[0].caseId, '?caseId=DEMO-001', '?id=DEMO-001', '?source=demo', '?source=fake', '?source=api&source=demo', '?caseId=a&caseId=b'])
    assert.equal(model.route(search).mode, 'api');
  assert.equal(model.route('?source=demo', { caseSource: 'api' }).invalid, true);
});

test('Fake Case adapter loads a page without inventing a global total, then loads another page', async () => {
  const adapter = new FakeCaseAdapter(), store = model.createStore(adapter);
  const pending = store.list(); assert.equal(store.snapshot().phase, 'loading'); await pending;
  assert.equal(store.snapshot().rows.length, 10); assert.equal(store.snapshot().hasNext, true);
  assert.equal('total' in store.snapshot(), false);
  await store.list(2); assert.equal(store.snapshot().page, 2); assert.equal(store.snapshot().rows.length, 2); assert.equal(store.snapshot().hasNext, false);
});

test('Fake Case detail maps only known fields; missing personal, organization, priority and contact data are not fabricated', async () => {
  const store = model.createStore(new FakeCaseAdapter()); await store.detail(records[3].caseId);
  const detail = store.snapshot().detail; assert.equal(detail.name, records[3].childName); assert.equal(detail.statusLabel, '待填表');
  for (const field of ['sex', 'birthDate', 'nationalId', 'address', 'contactName', 'relationship', 'phone', 'email']) assert.equal(detail[field], '未提供');
  assert.equal(detail.priority, null); assert.equal('organizationId' in detail, false); assert.equal('canEdit' in detail, false);
  assert.equal(detail.detailHref, 'case-detail.html?source=api&caseId=' + records[3].caseId);
});

test('empty lists are successful empty states, not replacement Demo rows', async () => {
  const adapter = new FakeCaseAdapter(); adapter.records = [];
  const store = model.createStore(adapter); await store.list(); assert.equal(store.snapshot().phase, 'empty'); assert.deepEqual(store.snapshot().rows, []);
});

for (const status of [401, 403, 404, 503]) {
  test(`${status}: list and detail errors clear all previous records, keep safe status and never invoke another source`, async () => {
    const adapter = new FakeCaseAdapter(), store = model.createStore(adapter);
    await store.list(); await store.detail(records[0].caseId); adapter.status = status;
    await store.detail(records[1].caseId);
    let state = store.snapshot(); assert.equal(state.phase, 'error'); assert.equal(state.error.status, status); assert.equal(state.detail, null); assert.deepEqual(state.rows, []);
    await store.list(); state = store.snapshot(); assert.equal(state.error.status, status); assert.deepEqual(state.rows, []);
    assert.equal(adapter.calls.length, 4); assert.equal(state.error.message.includes('SYNTHETIC'), false);
  });
}

test('wrong Case ID response fails closed rather than displaying another child', async () => {
  const adapter = new FakeCaseAdapter(); adapter.wrongId = true;
  const store = model.createStore(adapter); await store.detail(records[0].caseId);
  assert.equal(store.snapshot().phase, 'error'); assert.equal(store.snapshot().detail, null);
  assert.match(store.snapshot().error.message, /格式異常/);
});

test('malformed/duplicate Case DTOs are rejected by the adapter boundary even when a Fake bypasses HTTP validation', async () => {
  for (const rows of [[records[0], records[0]], [{ ...records[0], caseStatus: 'IN_PROGRESS' }], { items: records }]) {
    const store = model.createStore({ mode: 'api', list: async () => rows }); await store.list();
    assert.equal(store.snapshot().phase, 'error'); assert.deepEqual(store.snapshot().rows, []);
  }
});

test('new detail selection clears previous child immediately and an older late response cannot overwrite it', async () => {
  const late = deferred();
  const store = model.createStore({ mode: 'api', detail: id => id === records[0].caseId ? late.promise : Promise.resolve(records[1]) });
  const old = store.detail(records[0].caseId); assert.equal(store.snapshot().detail, null);
  await store.detail(records[1].caseId); late.resolve(records[0]); await old;
  assert.equal(store.snapshot().detail.id, records[1].caseId);
});

test('pagehide invalidation and Session loss cannot resurrect data from delayed replies', async () => {
  let allowed = true; const late = deferred();
  const store = model.createStore({ mode: 'api', detail: () => late.promise }, { canRead: () => allowed });
  const pending = store.detail(records[0].caseId); store.invalidate(); allowed = false; late.resolve(records[0]); await pending;
  assert.equal(store.snapshot().detail, null); await store.detail(records[0].caseId); assert.equal(store.snapshot().error.status, 401);
});

test('search/filter/sort operate on the loaded page only and FORM_COMPLETED does not mean Case closure', async () => {
  const store = model.createStore(new FakeCaseAdapter()); await store.list(); const rows = store.snapshot().rows;
  assert.equal(model.selectRows(rows, { mode: 'api', search: 'CASE-TEST-012' }).length, 0);
  assert.equal(model.selectRows(rows, { mode: 'api', status: 'COMPLETED' }).length, 1);
  assert.equal(model.selectRows(rows, { mode: 'api', status: 'FORM_COMPLETED' })[0].statusLabel, '表單已完成');
  assert.equal(model.selectRows(rows, { mode: 'api', sort: 'oldest' })[0].id, records[9].caseId);
  assert.match(read('cases.html'), /搜尋、篩選與排序僅套用目前載入頁面，不是全資料庫搜尋/);
  assert.match(read('js/cases.js'), /本頁取得/); assert.doesNotMatch(read('js/cases.js'), /data\.total/);
});

test('unchanged 24 Demo rows retain priority sorting, three status filters and the two matching detail routes', async () => {
  assert.equal(demo.length, 24); const store = model.createStore(model.createDemoAdapter(demo)); await store.list();
  assert.equal(store.snapshot().rows.length, 24);
  const rows = model.selectRows(store.snapshot().rows); assert.equal(rows[0].priority, 'high');
  assert.ok(model.selectRows(rows, { status: 'pending-contact' }).some(row => row.id === 'DEMO-024'));
  assert.ok(demo.some(row => row.id === 'DEMO-001')); assert.match(read('js/cases.js'), /case-detail-empty\.html/);
});

test('formal adapter consumes the existing MedicalApi Client contract, with no second HTTP implementation', async () => {
  const calls = [];
  const client = api.createClient({ cookieFetch: async url => { calls.push(url); return new Response(JSON.stringify(url.includes('?') ? [records[0]] : records[0])); } });
  const store = model.createStore(model.createApiAdapter(client)); await store.list(); await store.detail(records[0].caseId);
  assert.deepEqual(calls, ['/api/cases?page=1&pageSize=10', '/api/cases/' + records[0].caseId]);
  for (const file of ['js/medical-case-model.js', 'js/medical-case-source.js', 'js/medical-case-detail.js', 'js/cases.js'])
    assert.doesNotMatch(read(file), /\bfetch\s*\(|localStorage|sessionStorage|organizationId\s*:/);
});

test('source gate precedes Demo scripts and Session on all Case pages; API paths suppress legacy clinical Demo content', () => {
  for (const file of ['cases.html', 'case-detail.html', 'case-detail-empty.html']) {
    const html = read(file); assert.ok(html.indexOf('js/medical-case-source.js') < html.indexOf('/js/shared-demo.js'));
    assert.ok(html.indexOf('js/medical-case-source.js') < html.indexOf('js/staff-auth.js'));
  }
  assert.match(read('js/system-demo-ui.js'), /MedicalCaseMode\?\.mode==='api'\)return/);
  assert.match(read('js/staff-header.js'), /MedicalCaseMode\?\.mode === "api"\) return \[\]/);
  assert.match(read('js/medical-case-source.js'), /main\.replaceChildren/);
  assert.match(read('js/medical-case-detail.js'), /store\.invalidate/);
  assert.doesNotMatch(read('js/case-demo-data.js'), /answers\s*:|SNAP_IV_Q|optionValue|draft\s*:/);
});
