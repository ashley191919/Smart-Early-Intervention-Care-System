'use strict';

// Browser-only acceptance fixture: all files are local; /api/auth/me is synthetic.
// Never runs API Program, reads secrets, issues credentials, writes data, or opens MySQL.
const http = require('node:http');
const { createPreviewServer } = require('./StaffLoginEntryPreviewServer.cjs');
function createPermissionPreviewServer() {
    const staticHandler = createPreviewServer().listeners('request')[0];
    return http.createServer((request, response) => {
        if (new URL(request.url, 'http://fixture.invalid').pathname !== '/api/auth/me') {
            void staticHandler(request, response); return;
        }
        // Scenario belongs to the explicitly isolated fixture URL, never a production login.
        const scenario = new URL(request.headers.referer || 'http://fixture.invalid').searchParams.get('permissionFixture');
        const permissions = scenario === 'write' ? ['case.status_history.read', 'case.status.update'] :
            scenario === 'read' ? ['case.status_history.read'] : scenario === 'invalid' ? ['case.status.update', null] : [];
        const status = scenario === 'expired' ? 401 : scenario === 'forbidden' ? 403 : scenario === 'unavailable' ? 503 : 200;
        function reply() {
            response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
            response.end(JSON.stringify({ userId: '10000000-0000-4000-8000-000000000001', username: 'fixture', role: 'MEDICAL_STAFF', permissions }));
        }
        if (scenario === 'pending') setTimeout(reply, 8000);
        else reply();
    });
}
module.exports = { createPermissionPreviewServer };
if (require.main === module) {
    const server = createPermissionPreviewServer();
    server.listen(0, '127.0.0.1', () => console.log(`Isolated permission UI fixture: http://127.0.0.1:${server.address().port}`));
    process.on('SIGINT', () => server.close());
}
