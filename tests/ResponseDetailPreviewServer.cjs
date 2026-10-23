'use strict';
// Isolated browser fixtures only. Never starts the real API, reads credentials or connects to MySQL.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { createPreviewServer } = require('./StaffLoginEntryPreviewServer.cjs');
const snap = JSON.parse(fs.readFileSync(path.join(__dirname, '../EarlyInterventionCare.Api/Development/SnapQuestionnaire.json'), 'utf8'));
function makeFixture(scenario) {
    return { tasks: scenario === 'missing' ? [] : [{ id: 'response-ui-snap', formId: 'snap', role: 'teacher', name: snap.title,
        caseId: 'DEMO-001', round: 2, version: 'demo-1', status: scenario === 'unfinished' ? 'draft' : 'done', revision: 1,
        draft: { questions: snap.questions, options: snap.options,
            answers: Object.fromEntries(snap.questions.map((_, i) => [i, i % snap.options.length])),
            submittedAt: scenario === 'unfinished' ? null : '2026-10-19T02:30:00Z',
            note: '課堂活動中能在提醒後繼續參與。\n此為獨立瀏覽器驗收資料。' } }], grant: null };
}
function createResponsePreviewServer() {
    const staticHandler = createPreviewServer().listeners('request')[0];
    return http.createServer((request, response) => {
        const url = new URL(request.url, 'http://fixture.invalid');
        if (url.pathname === '/__response-fixture.js') {
            const referer = new URL(request.headers.referer || 'http://fixture.invalid');
            response.writeHead(200, { 'Content-Type': 'text/javascript; charset=utf-8', 'Cache-Control': 'no-store' });
            response.end('localStorage.setItem("earlycare.frontend.v1", ' + JSON.stringify(JSON.stringify(makeFixture(referer.searchParams.get('fixture')))) + ');');
        } else if (['/case-detail.html', '/response-detail.html'].includes(url.pathname)) {
            const html = fs.readFileSync(path.join(__dirname, '../EarlyInterventionCare.Api/wwwroot', url.pathname.slice(1)), 'utf8');
            response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
            response.end(html.replace('<script src="/js/shared-demo.js" defer></script>',
                '<script src="/__response-fixture.js" defer></script><script src="/js/shared-demo.js" defer></script>'));
        } else void staticHandler(request, response);
    });
}
module.exports = { createResponsePreviewServer };
if (require.main === module) {
    const server = createResponsePreviewServer();
    server.listen(0, '127.0.0.1', () => console.log(`Isolated response UI fixture: http://127.0.0.1:${server.address().port}/response-detail.html?taskId=response-ui-snap`));
    process.on('SIGINT', () => server.close());
}
