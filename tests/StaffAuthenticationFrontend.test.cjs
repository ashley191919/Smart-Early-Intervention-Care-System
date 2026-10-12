const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..', 'EarlyInterventionCare.Api', 'wwwroot');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

test('staff login uses the cookie session endpoint without exposing a JWT', () => {
    const source = read('js', 'staff-login.js');
    assert.match(source, /fetch\("\/api\/auth\/staff-session"/);
    assert.doesNotMatch(source, /fetch\("\/api\/auth\/login"/);
    assert.doesNotMatch(source, /accessTokenInMemory|body\.accessToken|localStorage|sessionStorage|console\s*\./);
    assert.match(source, /credentials:\s*"same-origin"/);
    assert.match(source, /location\.assign\("\/staff-dashboard\.html"\)/);
});

test('dashboard loads the common cookie authentication guard before exposing its workspace', () => {
    const page = read('staff-dashboard.html');
    assert.match(page, /data-staff-protected hidden/);
    assert.match(page, /data-staff-auth-error/);
    assert.match(page, /js\/staff-auth\.js/);
});

test('common staff authentication uses me, csrf and logout without script-readable token storage', () => {
    const source = read('js', 'staff-auth.js');
    assert.match(source, /fetch\("\/api\/auth\/me"/);
    assert.match(source, /fetch\("\/api\/auth\/csrf"/);
    assert.match(source, /X-EarlyCare-CSRF/);
    assert.match(source, /cookieFetch\("\/api\/auth\/logout", \{ method: "POST" \}\)/);
    assert.match(source, /credentials:\s*"same-origin"/);
    assert.match(source, /window\.location\.replace\("\/staff-login\.html"\)/);
    assert.doesNotMatch(source, /localStorage|sessionStorage|document\.cookie|Authorization\s*:/);
});

test('header delegates logout to the common session owner', () => {
    const source = read('js', 'staff-header.js');
    assert.match(source, /window\.StaffAuth\.logout\(\)/);
    assert.doesNotMatch(source, /window\.location\.assign\("staff-login\.html"\)/);
});
