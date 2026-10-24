'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const vm = require('node:vm');
const { assets, root } = require('../tools/medical-frontend/assets.cjs');
const { config, buildPackage } = require('../tools/medical-frontend/package.cjs');
const { createStaticServer } = require('../tools/medical-frontend/server.cjs');
const { createPackageFixtureServer } = require('./MedicalFrontendPackagePreview.cjs');
const api = require('../EarlyInterventionCare.Api/wwwroot/js/medical-api.js');
const parent = fs.mkdtempSync(path.join(os.tmpdir(), 'medical-package-test-'));
// Keep small test outputs for inspection; never remove user paths or existing releases.
const output = path.join(parent, 'release'), manifest = buildPackage(output);
const read = name => fs.readFileSync(path.join(output, name), 'utf8');
async function serve(server, action) {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try { await action('http://127.0.0.1:' + server.address().port); } finally { await new Promise(resolve => server.close(resolve)); }
}
test('package contains only current medical allowlist and deterministic hashes, with production API mode by default', () => {
  assert.equal(manifest.assets.length, assets.length); assert.equal(manifest.config.caseSource, 'api');
  const second = path.join(parent, 'identical'); assert.deepEqual(buildPackage(second), manifest);
  for (const entry of manifest.assets) {
    const bytes = fs.readFileSync(path.join(output, entry.path));
    assert.equal(bytes.length, entry.bytes); assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'), entry.sha256);
    assert.doesNotMatch(entry.path, /(?:^|\/)(?:tests|Development|Data|Controllers|Services|node_modules|\.git)(?:\/|$)|\.cs$|\.sql$|appsettings|\.db$|fixture/i);
  }
  const files = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? files(path.join(dir, e.name)) : [path.relative(output, path.join(dir, e.name)).replaceAll('\\', '/')]);
  assert.deepEqual(files(output).sort(), [...assets, 'manifest.json'].sort());
  const window = {}; vm.runInNewContext(read('js/medical-config.js'), { window });
  assert.equal(window.MedicalApiConfig.caseSource, 'api'); assert.equal(window.MedicalApiConfig.apiBaseUrl, '');
  assert.equal(window.MedicalConfig, undefined);
});
test('publisher refuses existing output before changing any files', () => {
  const before = read('manifest.json'); assert.throws(() => buildPackage(output), /OUTPUT_ALREADY_EXISTS/);
  assert.equal(read('manifest.json'), before);
});
test('configuration permits only same-origin prefixes and explicit source modes; source config stays unchanged', () => {
  const original = fs.readFileSync(path.join(root, 'js/medical-config.js'), 'utf8');
  for (const apiBaseUrl of ['https://other.invalid', '//other.invalid', '/gateway?x=1', '/a/../api', '/a%2fb', '/a\\b', '/a#x', '/a//b'])
    assert.throws(() => config({ apiBaseUrl }), /INVALID/);
  assert.throws(() => config({ caseSource: 'production-demo-fallback' }), /INVALID/);
  const explicit = path.join(parent, 'explicit-demo');
  assert.equal(buildPackage(explicit, { apiBaseUrl: '/gateway/', caseSource: 'demo' }).config.apiBaseUrl, '/gateway');
  assert.equal(fs.readFileSync(path.join(root, 'js/medical-config.js'), 'utf8'), original);
});
test('all HTML assets and CSS URL dependencies resolve, including dynamically loaded Case Demo data', () => {
  for (const file of assets.filter(x => x.endsWith('.html'))) {
    const document = read(file);
    for (const match of document.matchAll(/(?:src|href)="([^"#?]+\.(?:js|css|png))(?:[^"\s]*)"/g)) {
      const target = path.posix.normalize(path.posix.join(path.posix.dirname(file), match[1].replace(/^\//, '')));
      assert.ok(assets.includes(target), `${file}: ${target}`);
    }
  }
  for (const file of assets.filter(x => x.endsWith('.css'))) for (const match of read(file).matchAll(/url\(\s*["']?([^)'"\s]+)/g)) {
    if (match[1].startsWith('data:')) continue;
    assert.ok(assets.includes(path.posix.normalize(path.posix.join(path.posix.dirname(file), match[1]))), `${file}: ${match[1]}`);
  }
  assert.ok(assets.includes('js/case-demo-data.js')); assert.ok(assets.includes('js/medical-task-view.js'));
  for (const file of assets.filter(x => /staff-auth|medical-api|medical-demo-client|shared-demo/.test(x)))
    assert.equal(read(file), fs.readFileSync(path.join(root, file), 'utf8'));
  assert.equal(assets.includes('index.html'), false); assert.equal(assets.includes('teacher-workspace.html'), false);
});
test('static host serves every package asset with safe MIME/cache headers, no API or directory exposure', async () => {
  await serve(createStaticServer({ root: output }), async origin => {
    for (const file of [...assets, 'manifest.json']) {
      const response = await fetch(origin + '/' + file); assert.equal(response.status, 200, file);
      assert.equal(response.headers.get('cache-control'), 'no-store'); assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
      assert.equal(response.headers.get('access-control-allow-origin'), null); assert.equal(response.headers.get('set-cookie'), null);
      assert.equal(Buffer.compare(Buffer.from(await response.arrayBuffer()), fs.readFileSync(path.join(output, file))), 0);
    }
    assert.equal((await fetch(origin + '/')).status, 200);
    const head = await fetch(origin + '/staff-login.html', { method: 'HEAD' }); assert.equal(await head.text(), '');
    for (const file of ['/.git/config', '/appsettings.json', '/tests/MedicalTaskFixtures.cjs', '/images/', '/%2e%2e%2fProgram.cs'])
      assert.equal((await fetch(origin + file)).status, 404);
    assert.equal((await fetch(origin + '/staff-login.html', { method: 'POST' })).status, 405);
    for (const route of ['/api/auth/me', '/api/auth/staff-session', '/gateway/api/cases']) {
      const response = await fetch(origin + route); assert.equal(response.status, 503); assert.equal((await response.json()).code, 'API_NOT_CONNECTED');
    }
    const transport = api.createTransport({ origin, fetchImpl: (url, options) => fetch(origin + url, options) });
    await assert.rejects(api.createClient({ cookieFetch: transport.fetch }).cases(), error => error.status === 503);
  });
});
test('explicit test-only host reads Case/Task fixture from published pages; Demo response still contains 26 questions', async () => {
  await serve(createPackageFixtureServer(output), async origin => {
    const transport = api.createTransport({ origin, fetchImpl: (url, options) => fetch(origin + url, options) });
    const client = api.createClient({ cookieFetch: transport.fetch }), rows = await client.cases();
    assert.equal(rows.length, 12); assert.equal((await client.tasks(rows[0].caseId)).length, 4);
    assert.equal((await client.taskCompletion(rows[0].caseId)).requiredSubmittedCount, 1);
    const summaries = await (await fetch(origin + '/api/medical-demo/cases/DEMO-001/tasks')).json();
    const task = await (await fetch(origin + '/api/medical-demo/cases/DEMO-001/tasks/' + summaries[0].id)).json();
    assert.equal(task.draft.questions.length, 26);
    assert.equal((await fetch(origin + '/js/medical-demo-fixtures.js')).status, 404);
  });
  await serve(createPackageFixtureServer(output, { taskScenario: 'unavailable' }), async origin => {
    const response = await fetch(origin + '/api/cases/20000000-0000-4000-8000-000000000001/tasks');
    assert.equal(response.status, 503); assert.equal((await response.json()).code, 'ISOLATED_TASK_FAILURE');
    assert.equal((await fetch(origin + '/api/cases/20000000-0000-4000-8000-000000000001')).status, 200);
  });
});
