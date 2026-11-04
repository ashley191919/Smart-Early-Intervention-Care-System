import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import https from 'node:https';
import net from 'node:net';
import tls from 'node:tls';
import dgram from 'node:dgram';
import { readFileSync } from 'node:fs';

let networkAttempts = 0;
const forbidden = () => { networkAttempts++; throw new Error('NETWORK_FORBIDDEN'); };
// Install before importing the checker. No real network operation is performed.
http.request = http.get = http.createServer = forbidden;
https.request = https.get = https.createServer = forbidden;
net.connect = net.createConnection = net.createServer = forbidden;
net.Socket.prototype.connect = net.Server.prototype.listen = forbidden;
tls.connect = tls.createServer = dgram.createSocket = globalThis.fetch = forbidden;
const { check, format } = await import('./check.mjs');
const sample = name => JSON.parse(readFileSync(new URL(`./${name}.example.json`, import.meta.url), 'utf8'));
const fails = (config, code) => assert.ok(check(config).some(r => r.status === 'FAIL' && r.code === code), code);

for (const name of ['health', 'staff']) {
  test(`${name}: synthetic valid descriptor has no FAIL and always warns static-only`, () => {
    const rows = check(sample(name));
    assert.equal(rows.filter(r => r.status === 'FAIL').length, 0);
    assert.ok(rows.some(r => r.code === 'STATIC_ONLY' && r.status === 'WARN'));
    assert.ok(rows.some(r => r.status === 'PASS'));
  });
}

const cases = [
  ['unknown profile', c => { c.profile = 'production'; }, 'PROFILE'],
  ['environment mismatch', c => { c.dotnetEnvironment = 'Production'; }, 'ENVIRONMENT'],
  ['unsupported environment', c => { c.environment = c.dotnetEnvironment = 'Staging'; }, 'ENVIRONMENT'],
  ['IsolatedTest cannot enable staff', c => { c.environment = c.dotnetEnvironment = 'IsolatedTest'; }, 'STAFF_ENVIRONMENT'],
  ['HTTP staff', c => { c.frontendOrigin = c.apiOrigin = 'http://localhost:7127'; }, 'HTTPS'],
  ['cross port', c => { c.apiOrigin = 'https://localhost:7128'; }, 'SAME_ORIGIN'],
  ['host alias is different origin', c => { c.apiOrigin = 'https://127.0.0.1:7127'; }, 'SAME_ORIGIN'],
  ['non-loopback', c => { c.frontendOrigin = 'https://192.0.2.1:7127'; }, 'LOCAL_ORIGINS'],
  ['wildcard host', c => { c.apiOrigin = 'https://0.0.0.0:7127'; }, 'LOCAL_ORIGINS'],
  ['userinfo', c => { c.apiOrigin = 'https://synthetic@localhost:7127'; }, 'LOCAL_ORIGINS'],
  ['origin path', c => { c.apiOrigin += '/api'; }, 'LOCAL_ORIGINS'],
  ['origin query', c => { c.apiOrigin += '?x=1'; }, 'LOCAL_ORIGINS'],
  ['origin fragment', c => { c.apiOrigin += '#x'; }, 'LOCAL_ORIGINS'],
  ['origin whitespace', c => { c.apiOrigin = ` ${c.apiOrigin}`; }, 'LOCAL_ORIGINS'],
  ['normalized IP rejected', c => { c.apiOrigin = 'https://127.1:7127'; }, 'LOCAL_ORIGINS'],
  ['port overflow', c => { c.apiOrigin = 'https://localhost:65536'; }, 'PORTS'],
  ['zero port', c => { c.apiOrigin = 'https://localhost:0'; }, 'PORTS'],
  ['MySQL80 port', c => { c.apiOrigin = 'https://localhost:3306'; }, 'PORTS'],
  ['isolated MySQL port', c => { c.frontendOrigin = 'https://localhost:33326'; }, 'PORTS'],
  ['absolute base URL', c => { c.apiBaseUrl = 'https://localhost:7127'; }, 'BASE_URL'],
  ['protocol relative URL', c => { c.apiBaseUrl = '//localhost'; }, 'BASE_URL'],
  ['base traversal', c => { c.apiBaseUrl = '/gateway/../api'; }, 'BASE_URL'],
  ['encoded traversal', c => { c.apiBaseUrl = '/%2e%2e'; }, 'BASE_URL'],
  ['query base', c => { c.apiBaseUrl = '/api?x=1'; }, 'BASE_URL'],
  ['backslash base', c => { c.apiBaseUrl = '/api\\x'; }, 'BASE_URL'],
  ['no trusted origin', c => { c.staff.trustedOrigins = []; }, 'TRUSTED_ORIGINS'],
  ['wildcard trusted origin', c => { c.staff.trustedOrigins = ['*']; }, 'TRUSTED_ORIGINS'],
  ['HTTP trusted origin', c => { c.staff.trustedOrigins = ['http://localhost:7127']; }, 'TRUSTED_ORIGINS'],
  ['wrong trusted origin', c => { c.staff.trustedOrigins = ['https://localhost:7128']; }, 'TRUSTED_ORIGINS'],
  ['disabled staff cookie', c => { c.staff.enabled = false; }, 'STAFF_ENABLED'],
  ['insecure cookie', c => { c.staff.secure = false; }, 'COOKIE'],
  ['script readable cookie', c => { c.staff.httpOnly = false; }, 'COOKIE'],
  ['cookie SameSite relaxation', c => { c.staff.sameSite = 'None'; }, 'COOKIE'],
  ['cookie Domain', c => { c.staff.domain = 'localhost'; }, 'COOKIE'],
  ['cookie Path', c => { c.staff.path = '/api'; }, 'COOKIE'],
  ['cookie name', c => { c.staff.cookieName = 'other'; }, 'COOKIE'],
  ['cross origin credentials', c => { c.staff.credentials = 'include'; }, 'CREDENTIALS'],
  ['disabled CSRF', c => { c.staff.csrfEnabled = false; }, 'CSRF'],
  ['wrong CSRF header', c => { c.staff.csrfHeader = 'other'; }, 'CSRF'],
  ['missing setting', c => { c.requiredSettings.jwtSigningKeyConfigured = false; }, 'REQUIRED_SETTINGS'],
  ['setting value instead of boolean', c => { c.requiredSettings.databaseConnectionConfigured = 'SYNTHETIC_SECRET'; }, 'SETTINGS_SCHEMA'],
  ['missing staff field', c => { delete c.staff.csrfEnabled; }, 'STAFF_SCHEMA'],
  ['unknown setting key', c => { c.requiredSettings.password = 'SYNTHETIC_SECRET'; }, 'SETTINGS_SCHEMA'],
  ['unknown descriptor field', c => { c.connectionString = 'SYNTHETIC_SECRET'; }, 'DESCRIPTOR_SCHEMA'],
];
for (const [name, mutate, code] of cases) test(name, () => {
  const c = sample('staff'); mutate(c); fails(c, code);
});
for (const [name, mutate, code] of [
  ['wrong health environment', c => { c.environment = c.dotnetEnvironment = 'Development'; }, 'HEALTH_ENVIRONMENT'],
  ['wrong upstream', c => { c.apiOrigin = 'http://127.0.0.1:5188'; }, 'HEALTH_ROUTE'],
  ['health business route', c => { c.apiBaseUrl = '/api/cases'; }, 'HEALTH_ROUTE'],
  ['health cookie attempt', c => { c.staff = sample('staff').staff; }, 'HEALTH_NO_AUTH'],
]) test(name, () => { const c = sample('health'); mutate(c); fails(c, code); });
test('same-origin prefix is only a candidate, not routing validation', () => {
  const c = sample('staff'); c.apiBaseUrl = '/gateway';
  assert.equal(check(c).filter(x => x.status === 'FAIL').length, 0);
  assert.ok(check(c).some(x => x.code === 'ROUTING_UNVERIFIED' && x.status === 'WARN'));
});
test('malformed JSON-shaped descriptors fail closed', () => {
  for (const c of [null, [], {}, true, 'SYNTHETIC_SECRET']) fails(c, 'DESCRIPTOR_SCHEMA');
});
test('diagnostics never echo supplied values or unknown keys', () => {
  const sentinel = 'SYNTHETIC_SECRET_DO_NOT_PRINT';
  for (const mutate of [
    c => { c.apiOrigin = sentinel; },
    c => { c.staff.trustedOrigins = [sentinel]; },
    c => { c.staff.csrfHeader = sentinel; },
    c => { c.requiredSettings.jwtSigningKeyConfigured = sentinel; },
    c => { c[sentinel] = sentinel; },
  ]) {
    const c = sample('staff'); mutate(c);
    const rows = check(c);
    assert.ok(rows.some(x => x.status === 'FAIL'));
    assert.equal(format(rows).includes(sentinel), false);
    assert.equal(JSON.stringify(rows).includes(sentinel), false);
  }
});
test('no network attempted across the complete suite', () => assert.equal(networkAttempts, 0));
