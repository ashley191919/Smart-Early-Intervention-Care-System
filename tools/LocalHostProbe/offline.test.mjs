import { test, mock } from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import dgram from 'node:dgram';

// Fail before any real HTTP server or socket creation. These traps are installed
// BEFORE importing production code and remain active for the entire test file.
let networkAttempts = 0;
function forbiddenNetwork() { networkAttempts++; throw new Error('NETWORK_FORBIDDEN'); }
mock.method(http, 'createServer', forbiddenNetwork);
mock.method(http, 'request', forbiddenNetwork);
mock.method(http, 'get', forbiddenNetwork);
mock.method(https, 'request', forbiddenNetwork);
mock.method(https, 'get', forbiddenNetwork);
mock.method(net, 'createServer', forbiddenNetwork);
mock.method(net, 'createConnection', forbiddenNetwork);
mock.method(net, 'connect', forbiddenNetwork);
mock.method(net.Server.prototype, 'listen', forbiddenNetwork);
mock.method(net.Socket.prototype, 'connect', forbiddenNetwork);
mock.method(dgram, 'createSocket', forbiddenNetwork);
mock.method(globalThis, 'fetch', forbiddenNetwork);
const { createRequestHandler } = await import('./server.mjs');

class ClientResponse extends EventEmitter {
  destroyed = false;
  writableEnded = false;
  writes = [];
  bodies = [];
  writeHead(status, headers) {
    assert.equal(this.writableEnded, false);
    this.writes.push({ status, headers });
  }
  end(body) {
    assert.equal(this.writableEnded, false);
    this.bodies.push(body);
    this.writableEnded = true;
  }
}

function fixture({ requestThrows = false, endThrows = false, synchronousResponse = false } = {}) {
  const res = new ClientResponse();
  const upstream = new EventEmitter();
  const response = new EventEmitter();
  response.statusCode = 200;
  response.headers = { 'content-type': 'application/json; charset=utf-8' };
  response.complete = false;
  upstream.destroyed = false;
  let destroys = 0;
  let ends = 0;
  let requests = 0;
  let target;
  let receive;
  let responseDelivered = false;
  const timers = [];
  upstream.destroy = () => {
    destroys++;
    upstream.destroyed = true;
    // Deliberately raise stream errors DURING destroy, including rejection paths.
    if (responseDelivered) {
      response.emit('aborted');
      response.emit('error', new Error('synthetic response destroy'));
      response.emit('close');
    }
    upstream.emit('error', new Error('synthetic request destroy'));
  };
  upstream.end = () => { ends++; if (endThrows) throw new Error('synthetic end error'); };
  const handler = createRequestHandler({
    request(options, callback) {
      requests++;
      target = options;
      if (requestThrows) throw new Error('synthetic request error');
      receive = callback;
      if (synchronousResponse) {
        response.statusCode = 503;
        responseDelivered = true;
        callback(response);
      }
      return upstream;
    },
    schedule(callback, ms) { const timer = { callback, ms, cancelled: false }; timers.push(timer); return timer; },
    cancel(timer) { timer.cancelled = true; },
  });
  const req = { method: 'GET', url: '/health/local',
    headers: { host: '127.0.0.1:5187' }, socket: { remoteAddress: '127.0.0.1' } };
  return { res, req, response, upstream, timers, handler,
    start() { handler(req, res); },
    respond() { assert.ok(receive); responseDelivered = true; receive(response); },
    data(body) { response.emit('data', Buffer.from(body)); },
    end() { response.complete = true; response.emit('end'); response.emit('close'); },
    get requests() { return requests; }, get destroys() { return destroys; },
    get ends() { return ends; }, get target() { return target; },
  };
}
const health = '{ "status": "alive", "databaseChecked": false }';
function expectReply(f, status, code) {
  assert.equal(f.res.writes.length, 1);
  assert.equal(f.res.bodies.length, 1);
  assert.equal(f.res.writes[0].status, status);
  assert.equal(f.res.writes[0].headers['Cache-Control'], 'no-store');
  if (code) assert.deepEqual(JSON.parse(f.res.bodies[0]), { code });
  assert.ok(f.timers.every(t => t.cancelled));
}
function lateEvents(f) {
  f.response.emit('error', new Error('late synthetic response error'));
  f.response.emit('aborted');
  f.response.emit('close');
  f.response.emit('data', Buffer.alloc(9000));
  f.response.emit('end');
  f.upstream.emit('error', new Error('late synthetic request error'));
  for (const timer of f.timers) timer.callback(); // even an already queued callback
}

test('module import does not create a listener/socket or make a request', () => {
  assert.equal(networkAttempts, 0);
});

for (const [name, modify] of [
  ['non-200', r => { r.statusCode = 503; }],
  ['redirect', r => { r.statusCode = 302; r.headers.location = 'http://example.invalid'; }],
  ['missing content type', r => { r.headers = {}; }],
  ['wrong content type', r => { r.headers['content-type'] = 'text/plain'; }],
  ['Set-Cookie', r => { r.headers['set-cookie'] = ['synthetic=value']; }],
  ['encoded response', r => { r.headers['content-encoding'] = 'gzip'; }],
]) {
  test(`early reject ${name}: destroy-triggered error/aborted are handled once`, () => {
    const f = fixture(); f.start(); modify(f.response);
    assert.doesNotThrow(() => f.respond());
    lateEvents(f);
    expectReply(f, 502, 'UPSTREAM_RESPONSE_REJECTED');
    assert.equal(f.destroys, 1);
  });
}

for (const event of ['error', 'aborted', 'close']) {
  test(`upstream response ${event} safely fails`, () => {
    const f = fixture(); f.start(); f.respond(); f.data('{');
    assert.doesNotThrow(() => f.response.emit(event, new Error('synthetic')));
    lateEvents(f);
    expectReply(f, 502, 'UPSTREAM_UNAVAILABLE');
    assert.equal(f.destroys, 1);
  });
}

test('request error before response safely fails and accepts a late response', () => {
  const f = fixture(); f.start();
  // No response callback has run: only request listeners exist initially.
  f.upstream.destroy = () => { f.upstream.destroyed = true; };
  f.upstream.emit('error', new Error('synthetic refused'));
  f.respond(); lateEvents(f);
  expectReply(f, 502, 'UPSTREAM_UNAVAILABLE');
});

for (const beforeHeaders of [true, false]) {
  test(`five-second absolute timeout ${beforeHeaders ? 'before headers' : 'during body'}`, () => {
    const f = fixture(); f.start();
    // Only emit response stream errors if the callback has attached listeners.
    if (beforeHeaders) f.upstream.destroy = () => { f.upstream.destroyed = true; };
    else { f.respond(); f.data('{'); }
    assert.equal(f.timers[0].ms, 5000);
    f.timers[0].callback();
    if (beforeHeaders) f.respond();
    lateEvents(f);
    expectReply(f, 504, 'UPSTREAM_TIMEOUT');
  });
}

for (const body of ['not-json', '{}', 'null', '[]',
  '{"status":"alive","databaseChecked":true}',
  '{"status":"alive","databaseChecked":false,"extra":"synthetic"}']) {
  test(`unexpected JSON is rejected: ${body}`, () => {
    const f = fixture(); f.start(); f.respond(); f.data(body); f.end();
    lateEvents(f);
    expectReply(f, 502, 'UPSTREAM_RESPONSE_REJECTED');
  });
}

test('body above 4096 bytes is rejected and late chunks are ignored', () => {
  const f = fixture(); f.start(); f.respond();
  f.data(' '.repeat(4096)); f.data('x'); lateEvents(f);
  expectReply(f, 502, 'UPSTREAM_RESPONSE_REJECTED');
  assert.equal(f.destroys, 1);
});
test('4096-byte valid JSON is allowed and actual upstream text is preserved', () => {
  const f = fixture(); f.start(); f.respond();
  const body = health.padEnd(4096, ' ');
  f.data(body); f.end(); lateEvents(f);
  expectReply(f, 200);
  assert.equal(f.res.bodies[0], body);
});
test('success followed by repeated end/error/abort/timeout sends one success', () => {
  const f = fixture(); f.start(); f.respond(); f.data(health); f.end(); lateEvents(f);
  expectReply(f, 200);
  assert.equal(f.res.bodies[0], health);
});

for (const event of ['close', 'error']) {
  test(`downstream ${event} cancels without writing a response`, () => {
    const f = fixture(); f.start(); f.respond();
    f.res.destroyed = true;
    f.res.emit(event, new Error('synthetic downstream'));
    lateEvents(f);
    assert.equal(f.res.writes.length, 0);
    assert.equal(f.res.bodies.length, 0);
    assert.equal(f.destroys, 1);
    assert.ok(f.timers.every(t => t.cancelled));
  });
}
for (const option of ['requestThrows', 'endThrows', 'synchronousResponse']) {
  test(`${option}: synchronous substitute failure does not escape`, () => {
    const f = fixture({ [option]: true });
    assert.doesNotThrow(() => f.start());
    expectReply(f, 502, option === 'synchronousResponse' ? 'UPSTREAM_RESPONSE_REJECTED' : 'UPSTREAM_UNAVAILABLE');
  });
}

for (const [name, mutate, status, code] of [
  ['POST', q => { q.method = 'POST'; }, 405, 'METHOD_REJECTED'],
  ['other API', q => { q.url = '/api/cases'; }, 404, 'ROUTE_REJECTED'],
  ['query', q => { q.url = '/health/local?target=http://example.invalid'; }, 404, 'ROUTE_REJECTED'],
  ['absolute URL', q => { q.url = 'http://127.0.0.1:5186/health/local'; }, 404, 'ROUTE_REJECTED'],
  ['encoded path', q => { q.url = '/health/%6cocal'; }, 404, 'ROUTE_REJECTED'],
  ['Cookie', q => { q.headers.cookie = 'synthetic=value'; }, 400, 'REQUEST_METADATA_REJECTED'],
  ['Authorization', q => { q.headers.authorization = 'synthetic'; }, 400, 'REQUEST_METADATA_REJECTED'],
  ['Proxy-Authorization', q => { q.headers['proxy-authorization'] = 'synthetic'; }, 400, 'REQUEST_METADATA_REJECTED'],
  ['body', q => { q.headers['content-length'] = '1'; }, 400, 'REQUEST_METADATA_REJECTED'],
  ['transfer encoding', q => { q.headers['transfer-encoding'] = 'chunked'; }, 400, 'REQUEST_METADATA_REJECTED'],
  ['other Host', q => { q.headers.host = 'example.invalid'; }, 403, 'REQUEST_ORIGIN_REJECTED'],
  ['other Origin', q => { q.headers.origin = 'http://example.invalid'; }, 403, 'REQUEST_ORIGIN_REJECTED'],
  ['cross-site', q => { q.headers['sec-fetch-site'] = 'cross-site'; }, 403, 'REQUEST_ORIGIN_REJECTED'],
  ['non-loopback peer', q => { q.socket.remoteAddress = '192.0.2.1'; }, 403, 'REQUEST_ORIGIN_REJECTED'],
]) {
  test(`reject ${name} without making any upstream request`, () => {
    const f = fixture(); mutate(f.req); f.start();
    expectReply(f, status, code);
    assert.equal(f.requests, 0);
  });
}
test('fixed upstream and fresh headers ignore caller forwarding/target metadata', () => {
  const f = fixture();
  f.req.headers['x-forwarded-host'] = 'example.invalid';
  f.req.headers['x-target'] = 'http://example.invalid';
  f.start();
  assert.deepEqual(f.target, {
    hostname: '127.0.0.1', port: 5186, path: '/health/local', method: 'GET',
    agent: false, headers: { Accept: 'application/json', Connection: 'close' },
  });
  f.respond(); f.data(health); f.end(); expectReply(f, 200);
});
test('no network API was attempted throughout the offline suite', () => {
  assert.equal(networkAttempts, 0);
});
