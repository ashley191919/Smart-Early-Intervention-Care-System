'use strict';

// Static-only browser acceptance fixture. Never starts API Program, loads secrets,
// creates sessions, aliases missing production routes, or connects to a database.
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const root = path.resolve(__dirname, '../EarlyInterventionCare.Api/wwwroot');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8', '.png': 'image/png', '.svg': 'image/svg+xml',
    '.jpg': 'image/jpeg', '.woff2': 'font/woff2', '.ico': 'image/x-icon' };

function createPreviewServer() {
    return http.createServer(async (request, response) => {
        response.setHeader('Cache-Control', 'no-store');
        const pathname = new URL(request.url, 'http://127.0.0.1').pathname;
        if (pathname.startsWith('/api/')) {
            response.writeHead(503, { 'Content-Type': 'application/json; charset=utf-8' });
            response.end(JSON.stringify({ code: 'STATIC_PREVIEW_ONLY' }));
            return;
        }
        if (!['GET', 'HEAD'].includes(request.method)) {
            response.writeHead(405); response.end(); return;
        }
        try {
            const relative = decodeURIComponent(pathname === '/' ? '/index.html' : pathname);
            const file = path.resolve(root, '.' + relative);
            if (!file.startsWith(root + path.sep)) {
                response.writeHead(404); response.end(); return;
            }
            const body = await fs.readFile(file);
            response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
            response.end(request.method === 'HEAD' ? undefined : body);
        } catch {
            response.writeHead(404); response.end('Not found');
        }
    });
}

module.exports = { createPreviewServer };
if (require.main === module) {
    const server = createPreviewServer();
    server.listen(0, '127.0.0.1', () => console.log(`Static acceptance fixture: http://127.0.0.1:${server.address().port}`));
    process.on('SIGINT', () => server.close());
}
