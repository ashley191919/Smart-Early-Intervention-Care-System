'use strict';
// Synthetic browser/HTTP acceptance host only. No production authentication, Program or database.
const http = require('node:http');
const { createMedicalDemoPreviewServer } = require('./MedicalDemoPreviewServer.cjs');
const { records } = require('./MedicalCaseFixtures.cjs');
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
      const status = { expired: 401, forbidden: 403, missing: 404, unavailable: 503 }[scenario];
      if (status) return reply(status, { code: 'ISOLATED_TEST_FAILURE' });
      if (url.pathname === '/api/cases') {
        const page = Number(url.searchParams.get('page') || 1), size = Number(url.searchParams.get('pageSize') || 10);
        return reply(200, scenario === 'empty' ? [] : records.slice((page - 1) * size, page * size));
      }
      const found = records.find(record => record.caseId === url.pathname.split('/')[3]);
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
