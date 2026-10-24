'use strict';
// Explicit test-only host for a published directory, never copied into that directory.
const http = require('node:http');
const { createStaticServer } = require('../tools/medical-frontend/server.cjs');
const { createMedicalCasePreviewServer } = require('./MedicalCasePreviewServer.cjs');
function createPackageFixtureServer(root, { taskScenario = '' } = {}) {
  if (!['', 'empty', 'expired', 'forbidden', 'missing', 'unavailable', 'wrong-case', 'completion-unavailable'].includes(taskScenario))
    throw new Error('INVALID_FIXTURE_SCENARIO');
  const staticHandler = createStaticServer({ root }).listeners('request')[0];
  const fixtureHandler = createMedicalCasePreviewServer().listeners('request')[0];
  return http.createServer((req, res) => {
    if (!/^(127\.0\.0\.1|localhost):\d+$/.test(req.headers.host || '')) { res.writeHead(403); res.end(); return; }
    const url = new URL(req.url, 'http://127.0.0.1');
    const index = url.pathname.indexOf('/api/');
    if (index >= 0) {
      req.url = url.pathname.slice(index) + url.search;
      // Test-server-owned scenario: production no-referrer policy stays intact.
      if (taskScenario) req.headers.referer = 'http://isolated.invalid/?taskScenario=' + taskScenario;
      return fixtureHandler(req, res);
    }
    staticHandler(req, res);
  });
}
module.exports = { createPackageFixtureServer };
if (require.main === module) {
  const args = process.argv.slice(2), index = args.indexOf('--root');
  if (index < 0 || !args[index + 1]) throw new Error('Usage: node tests/MedicalFrontendPackagePreview.cjs --root PACKAGE_DIRECTORY');
  const scenarioIndex = args.indexOf('--task-scenario');
  if (scenarioIndex >= 0 && !args[scenarioIndex + 1]) throw new Error('INVALID_FIXTURE_SCENARIO');
  const server = createPackageFixtureServer(args[index + 1], { taskScenario: scenarioIndex < 0 ? '' : args[scenarioIndex + 1] });
  server.listen(0, '127.0.0.1', () => console.log(`SYNTHETIC HTTP FIXTURE (not real authentication): http://127.0.0.1:${server.address().port}/staff-login.html`));
  process.on('SIGINT', () => server.close());
}
