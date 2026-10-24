'use strict';
// Synthetic browser/HTTP acceptance host only. No production authentication, Program or database.
const http = require('node:http');
const { createMedicalDemoPreviewServer } = require('./MedicalDemoPreviewServer.cjs');
const { records } = require('./MedicalCaseFixtures.cjs');
const taskFixture = require('./MedicalTaskFixtures.cjs');
function createMedicalCasePreviewServer() {
  const existing = createMedicalDemoPreviewServer('Development');
  const handler = existing.listeners('request')[0];
  return http.createServer((req, res) => {
    const url = new URL(req.url, 'http://isolated.invalid');
    const referer = new URL(req.headers.referer || 'http://isolated.invalid');
    const scenario = referer.searchParams.get('scenario');
    const reply = (status, body) => { res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(body)); };
    if (url.pathname === '/api/cases' || url.pathname.startsWith('/api/cases/')) {
      if (req.method !== 'GET') return reply(405, {});
      const taskPath = url.pathname.match(/^\/api\/cases\/([^/]+)\/tasks(\/completion)?$/);
      if (taskPath) {
        const taskScenario = referer.searchParams.get('taskScenario');
        const failure = { expired: 401, forbidden: 403, missing: 404, unavailable: 503 }[taskScenario];
        if (failure) return reply(failure, { code: 'ISOLATED_TASK_FAILURE' });
        const found = records.find(record => record.caseId === taskPath[1]);
        if (!found) return reply(404, {});
        if (taskPath[2]) {
          if (taskScenario === 'completion-unavailable') return reply(503, {});
          return reply(200, taskScenario === 'empty' ? { ...taskFixture.completion, caseId: found.caseId, totalTaskCount: 0,
            requiredTaskCount: 0, requiredSubmittedCount: 0, requiredPendingCount: 0, requiredInProgressCount: 0 } :
            { ...taskFixture.completion, caseId: found.caseId });
        }
        return reply(200, taskScenario === 'empty' ? [] : taskFixture.tasks.map(task => ({ ...task,
          caseId: taskScenario === 'wrong-case' ? records[1].caseId : found.caseId })));
      }
      const status = { expired: 401, forbidden: 403, missing: 404, unavailable: 503 }[scenario];
      if (status) return reply(status, { code: 'ISOLATED_TEST_FAILURE' });
      if (url.pathname === '/api/cases') {
        const page = Number(url.searchParams.get('page') || 1), size = Number(url.searchParams.get('pageSize') || 20);
        return reply(200, scenario === 'empty' ? [] : records.slice((page - 1) * size, page * size));
      }
      const found = records.find(record => record.caseId === url.pathname.split('/')[3]);
      if (found && scenario === 'blank-fields') return reply(200, { ...found, nationalId: '', address: '', sex: 'UNKNOWN' });
      if (found && scenario === 'null-fields') return reply(200, { ...found, nationalId: null });
      if (found && scenario === 'utc-boundary') return reply(200, { ...found, birthDate: '2024-02-29', updatedAtUtc: '2026-10-24T23:30:00Z' });
      return found ? reply(200, scenario === 'wrong-id' ? records[1] : found) : reply(404, {});
    }
    handler(req, res);
  });
}
module.exports = { createMedicalCasePreviewServer };
if (require.main === module) {
  const server = createMedicalCasePreviewServer();
  server.listen(0, '127.0.0.1', () => console.log('Isolated Case fixture: http://127.0.0.1:' + server.address().port + '/cases.html?source=api'));
}
