import http from 'node:http';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// Fixed infrastructure probe. No environment/CLI overrides or generic proxy.
const host = '127.0.0.1';
const port = 5187;
const origin = 'http://127.0.0.1:5187';
const assets = new Map([
  ['/', ['text/html; charset=utf-8', readFileSync(new URL('./index.html', import.meta.url))]],
  ['/probe.js', ['text/javascript; charset=utf-8', readFileSync(new URL('./probe.js', import.meta.url))]],
]);
const headers = {
  'Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'Content-Security-Policy': "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
};
function send(res, status, body, type = 'application/json; charset=utf-8') {
  if (res.destroyed || res.writableEnded) return;
  res.writeHead(status, { ...headers, 'Content-Type': type });
  res.end(body);
}
const error = (res, status, code) => send(res, status, JSON.stringify({ code }));

function proxyHealth(res, { request, schedule, cancel }) {
  let finished = false;
  let upstream;
  let chunks = [];
  let bytes = 0;
  const stopUpstream = () => {
    if (upstream && !upstream.destroyed) upstream.destroy();
  };
  const finish = (status, body) => {
    if (finished) return;
    finished = true;
    chunks = [];
    cancel(deadline);
    send(res, status, body);
  };
  const fail = (status, code) => {
    if (finished) return;
    finish(status, JSON.stringify({ code }));
    stopUpstream();
  };
  const deadline = schedule(() => fail(504, 'UPSTREAM_TIMEOUT'), 5000);
  const disconnect = () => {
    finished = true;
    chunks = [];
    cancel(deadline);
    stopUpstream();
  };
  res.on('close', disconnect);
  res.on('error', disconnect);
  // Construct outbound headers from scratch: never copy request headers, cookies,
  // authorization, origin, query, body or forwarding headers. No redirects/retries.
  try {
    upstream = request({
    hostname: '127.0.0.1', port: 5186, path: '/health/local', method: 'GET',
    agent: false, headers: { Accept: 'application/json', Connection: 'close' },
  }, response => {
    // Install these BEFORE any header rejection, resume, or request destruction.
    // Keep error listeners after completion to absorb late stream errors safely.
    response.on('error', () => fail(502, 'UPSTREAM_UNAVAILABLE'));
    response.on('aborted', () => fail(502, 'UPSTREAM_UNAVAILABLE'));
    response.on('close', () => {
      if (!response.complete) fail(502, 'UPSTREAM_UNAVAILABLE');
    });
    if (finished) { stopUpstream(); return; }
    if (response.statusCode !== 200 ||
        !/^application\/json(?:\s*;|$)/i.test(response.headers['content-type'] ?? '') ||
        response.headers['set-cookie'] || response.headers['content-encoding']) {
      fail(502, 'UPSTREAM_RESPONSE_REJECTED');
      return;
    }
    response.on('data', chunk => {
      if (finished) return;
      bytes += chunk.length;
      if (bytes > 4096) { fail(502, 'UPSTREAM_RESPONSE_REJECTED'); return; }
      chunks.push(chunk);
    });
    response.on('end', () => {
      if (finished) return;
      const body = Buffer.concat(chunks).toString('utf8');
      try {
        const value = JSON.parse(body);
        if (!value || Array.isArray(value) || Object.keys(value).length !== 2 ||
            value.status !== 'alive' || value.databaseChecked !== false) throw new Error();
      } catch { fail(502, 'UPSTREAM_RESPONSE_REJECTED'); return; }
      // Return actual validated upstream body, not a manufactured success fixture.
      finish(200, body);
    });
  });
  upstream.on('error', () => fail(502, 'UPSTREAM_UNAVAILABLE'));
  if (finished) stopUpstream();
  else upstream.end();
  } catch { fail(502, 'UPSTREAM_UNAVAILABLE'); }
}

// Injection is only an in-process test seam; it cannot change target or routes.
// Importing this module never creates a server, listener, or HTTP request.
export function createRequestHandler({ request = http.request, schedule = setTimeout, cancel = clearTimeout } = {}) {
  return (req, res) => {
  if (req.socket.remoteAddress !== host || req.headers.host !== '127.0.0.1:5187' ||
      (req.headers.origin !== undefined && req.headers.origin !== origin) ||
      (req.headers['sec-fetch-site'] !== undefined &&
       !['same-origin', 'none'].includes(req.headers['sec-fetch-site']))) {
    error(res, 403, 'REQUEST_ORIGIN_REJECTED'); return;
  }
  if (req.method !== 'GET') { error(res, 405, 'METHOD_REJECTED'); return; }
  if (req.headers.cookie !== undefined || req.headers.authorization !== undefined ||
      req.headers['proxy-authorization'] !== undefined ||
      req.headers['transfer-encoding'] !== undefined ||
      (req.headers['content-length'] !== undefined && req.headers['content-length'] !== '0')) {
    error(res, 400, 'REQUEST_METADATA_REJECTED'); return;
  }
  // Compare raw request-target exactly: query strings, absolute URLs and encoded
  // alternate paths are not normalized into accepted routes.
  if (req.url === '/health/local') { proxyHealth(res, { request, schedule, cancel }); return; }
  const asset = assets.get(req.url);
  if (!asset) { error(res, 404, 'ROUTE_REJECTED'); return; }
  send(res, 200, asset[1], asset[0]);
  };
}

function start() {
if (process.argv.length !== 2) {
  console.error('PROBE_ARGUMENTS_REJECTED');
  process.exitCode = 1;
  return;
}
const server = http.createServer({ maxHeaderSize: 8192 }, createRequestHandler());
server.requestTimeout = 10000;
server.headersTimeout = 5000;
server.keepAliveTimeout = 1000;
server.maxRequestsPerSocket = 10;
server.on('connect', (_req, socket) => socket.destroy());
server.on('upgrade', (_req, socket) => socket.destroy());
server.on('clientError', (_err, socket) => socket.destroy());
server.on('error', () => {
  console.error('PROBE_LISTENER_FAILED');
  process.exitCode = 1;
});
let stopping = false;
function stop() {
  if (stopping) return;
  stopping = true;
  console.log('PROBE_STOPPING');
  const timer = setTimeout(() => {
    console.error('PROBE_STOP_TIMEOUT');
    server.closeAllConnections();
    process.exitCode = 1;
  }, 5000);
  server.close(() => { clearTimeout(timer); console.log('PROBE_STOPPED'); });
  server.closeIdleConnections();
}
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
server.listen({ host, port, exclusive: true }, () => {
  console.log(`PROBE_READY pid=${process.pid} url=${origin}`);
});
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) start();
