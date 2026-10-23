'use strict';
// Synthetic browser-only host. Does not run production Program, issue real credentials or access MySQL.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { createPreviewServer } = require('./StaffLoginEntryPreviewServer.cjs');
const task = JSON.parse(fs.readFileSync(path.join(__dirname, '../EarlyInterventionCare.Api/Development/MedicalDemoFixture.json'), 'utf8'));
function createMedicalDemoPreviewServer(environment = 'Development', { requireLogin = false } = {}) {
    const staticHandler = createPreviewServer().listeners('request')[0];
    return http.createServer((request, response) => {
        const url = new URL(request.url, 'http://isolated.invalid');
        const referrer = new URL(request.headers.referer || 'http://isolated.invalid');
        const session = referrer.searchParams.get('session');
        const active = !requireLogin || (request.headers.cookie || '').split(';').some(value => value.trim() === 'isolated-medical-demo=active');
        const user = { userId: '00000000-0000-0000-0000-000000000001', username: '隔離測試醫療人員', role: 'CASE_MANAGER', permissions: [] };
        const reply = (status, body) => {
            response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
            response.end(JSON.stringify(body));
        };
        if (url.pathname === '/js/medical-demo-fixtures.js') return reply(404, {});
        if (requireLogin && url.pathname === '/api/auth/staff-session' && request.method === 'POST') {
            // Only fixed, synthetic browser-test values; not the application's authentication logic.
            let body = '';
            request.on('data', chunk => { body += chunk; });
            request.on('end', () => {
                let fields; try { fields = JSON.parse(body); } catch { return reply(400, {}); }
                if (fields.organizationCode !== 'OFFLINE_DEMO' || fields.username !== 'demo.staff' || fields.password !== 'only-synthetic-browser-fixture')
                    return reply(401, { code: 'ISOLATED_TEST_ONLY' });
                response.setHeader('Set-Cookie', 'isolated-medical-demo=active; Path=/; HttpOnly; SameSite=Strict');
                reply(200, { ...user, message: '隔離測試登入', expiresAt: new Date(Date.now() + 3600000).toISOString() });
            });
            return;
        }
        if (requireLogin && url.pathname === '/api/auth/csrf') return reply(active ? 200 : 401, active ? { requestToken: 'isolated-test-only' } : {});
        if (url.pathname === '/api/auth/me') return reply(!active || session === 'expired' ? 401 : 200, user);
        if (url.pathname.startsWith('/api/medical-demo')) {
            if (environment !== 'Development') return reply(404, {});
            if (!active || session === 'expired') return reply(401, {});
            if (session === 'forbidden') return reply(403, {});
            if (url.pathname === '/api/medical-demo/availability') return reply(200, { enabled: true, label: 'Demo 模擬資料' });
            if (url.pathname === '/api/medical-demo/cases/DEMO-024/tasks') return reply(200, []);
            if (url.pathname === '/api/medical-demo/cases/DEMO-001/tasks') { const { draft, ...summary } = task; return reply(200, [summary]); }
            if (url.pathname === '/api/medical-demo/cases/DEMO-001/tasks/' + task.id) return reply(200, task);
            return reply(404, {});
        }
        void staticHandler(request, response);
    });
}
module.exports = { createMedicalDemoPreviewServer };
if (require.main === module) {
    const environment = process.argv.includes('--production') ? 'Production' : 'Development';
    const requireLogin = process.argv.includes('--require-login');
    const server = createMedicalDemoPreviewServer(environment, { requireLogin });
    server.listen(0, '127.0.0.1', () => console.log(`Isolated ${environment} medical demo UI: http://127.0.0.1:${server.address().port}/${requireLogin ? 'staff-login.html' : 'staff-dashboard.html'}`));
    process.on('SIGINT', () => server.close());
}
