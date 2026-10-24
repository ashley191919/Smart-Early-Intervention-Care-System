'use strict';
// Static-only loopback server. NO API proxy, TLS bypass, synthetic login, CORS or database.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { root: sourceRoot, assets } = require('./assets.cjs');
const { readAsset } = require('./package.cjs');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.png': 'image/png', '.json': 'application/json; charset=utf-8' };
function createStaticServer({ root = sourceRoot } = {}) {
  const base = fs.realpathSync(root);
  return http.createServer((req, res) => {
    const fail = (status, code) => { res.writeHead(status, { 'Content-Type': types['.json'] }); res.end(req.method === 'HEAD' ? undefined : JSON.stringify({ code })); };
    res.setHeader('Cache-Control', 'no-store'); res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Referrer-Policy', 'no-referrer');
    if (!/^(127\.0\.0\.1|localhost):\d+$/.test(req.headers.host || '')) return fail(403, 'LOOPBACK_ONLY');
    let pathname;
    try { pathname = decodeURIComponent(new URL(req.url, 'http://127.0.0.1').pathname); } catch { return fail(400, 'INVALID_PATH'); }
    if (/(?:^|\/)api(?:\/|$)/.test(pathname)) return fail(503, 'API_NOT_CONNECTED');
    if (!['GET', 'HEAD'].includes(req.method)) return fail(405, 'METHOD_NOT_ALLOWED');
    const asset = pathname === '/' ? 'staff-login.html' : pathname.slice(1);
    if (![...assets, 'manifest.json'].includes(asset)) return fail(404, 'ASSET_NOT_FOUND');
    try {
      const bytes = readAsset(base, asset);
      res.writeHead(200, { 'Content-Type': types[path.extname(asset)] }); res.end(req.method === 'HEAD' ? undefined : bytes);
    } catch { fail(404, 'ASSET_NOT_FOUND'); }
  });
}
module.exports = { createStaticServer };
if (require.main === module) {
  const args = process.argv.slice(2), allowed = ['--root', '--port'];
  for (let i = 0; i < args.length; i += 2) if (!allowed.includes(args[i]) || !args[i + 1] || args[i + 1].startsWith('--')) throw new Error('INVALID_ARGUMENTS');
  const get = key => args.includes(key) ? args[args.indexOf(key) + 1] : undefined;
  const port = Number(get('--port') || 5200);
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('INVALID_PORT');
  const server = createStaticServer({ root: get('--root') });
  server.listen(port, '127.0.0.1', () => console.log(`Static-only (API disconnected): http://127.0.0.1:${server.address().port}/staff-login.html`));
  process.on('SIGINT', () => server.close());
}
