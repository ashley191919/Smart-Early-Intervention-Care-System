"""Persistence checks on an agent-owned disposable MySQL instance (port 33318 only).
No codes, cookies, passwords or connection strings are printed or written to disk.
Build and seed the isolated database before running; never points at user port 3306.
"""
import concurrent.futures
import http.cookiejar
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
mysql = r'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
base = 'http://127.0.0.1:5197/api/dev/teacher-workspace'
env = os.environ.copy()
env['ConnectionStrings__DefaultConnection'] = 'Server=127.0.0.1;Port=33318;Database=earlycare_dev;User=root;Password=;'
env['ASPNETCORE_ENVIRONMENT'] = 'Development'
process = None

def client():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

def call(method, path, payload=None, opener=None):
    request = urllib.request.Request(base + path, method=method, data=json.dumps(payload).encode() if payload is not None else None, headers={'Content-Type': 'application/json'})
    try:
        response = (opener or client()).open(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    data = response.read()
    return response.status, json.loads(data) if data else None

def sql(statement):
    result = subprocess.run([mysql, '--no-defaults', '--host=127.0.0.1', '--port=33318', '--user=root', '--database=earlycare_dev', '--batch', '--skip-column-names', '-e', statement], capture_output=True, text=True)
    if result.returncode: raise RuntimeError('Isolated SQL check failed (details suppressed).')
    return result.stdout.strip()

def stop():
    global process
    if process:
        process.terminate()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
        process = None

def start():
    global process
    process = subprocess.Popen(['dotnet', str(root / 'EarlyInterventionCare.Api/bin/Debug/net8.0/EarlyInterventionCare.Api.dll'), '--urls', 'http://127.0.0.1:5197'], cwd=root / 'EarlyInterventionCare.Api', env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(100):
        if process.poll() is not None: raise RuntimeError('Test server could not start.')
        try:
            if call('GET', '/task')[0] == 401: return
        except (OSError, urllib.error.URLError): pass
        time.sleep(.1)
    raise RuntimeError('Test server did not become ready.')

try:
    start()
    a = client()
    grant = call('POST', '/grants')[1]
    assert call('POST', '/verify', {'code': grant['authorizationCode']}, a)[0] == 200
    assert call('GET', '/task', opener=a)[0] == 200
    stop(); start()
    assert call('GET', '/task', opener=a)[0] == 200, 'session persisted across restart'
    b = client()
    assert call('POST', '/verify', {'code': grant['authorizationCode']}, b)[0] == 200
    assert call('GET', '/task', opener=a)[0] == 401, 'old session revoked on re-verification'
    assert call('GET', '/task', opener=b)[0] == 200
    assert call('POST', '/grants/' + grant['grantId'] + '/revoke')[0] == 200
    stop(); start()
    assert call('POST', '/verify', {'code': grant['authorizationCode']}, b)[0] == 401
    assert call('GET', '/task', opener=b)[0] == 401
    print('PASS: active grant/session survive restart; re-verification replaces session; revocation survives restart.')
    grant = call('POST', '/grants')[1]
    assert call('POST', '/verify', {'code': grant['authorizationCode']}, b)[0] == 200
    sql("UPDATE teacher_sessions SET session_expires_at_utc=UTC_TIMESTAMP()-INTERVAL 1 SECOND WHERE grant_id='" + grant['grantId'] + "'")
    assert call('GET', '/task', opener=b)[0] == 401
    assert call('POST', '/verify', {'code': grant['authorizationCode']}, b)[0] == 200
    assert call('POST', '/logout', opener=b)[0] == 204
    assert call('GET', '/task', opener=b)[0] == 401
    assert call('POST', '/verify', {'code': grant['authorizationCode']}, b)[0] == 200
    replacement = call('POST', '/grants')[1]
    assert call('GET', '/task', opener=b)[0] == 401
    assert call('POST', '/verify', {'code': grant['authorizationCode']})[0] == 401
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        responses = list(executor.map(lambda _: call('POST', '/grants'), range(4)))
    assert all(status == 200 for status, _ in responses)
    assert sql("SELECT COUNT(*) FROM teacher_grants WHERE grant_status='ACTIVE'") == '1'
    print('PASS: expiry requires original-code re-verification; logout keeps grant; replacement and concurrent creation keep one active grant.')
    # Simulate a terminal state only; this is not a questionnaire submission test.
    sql("UPDATE teacher_grants SET grant_status='USED',used_at_utc=UTC_TIMESTAMP() WHERE grant_status='ACTIVE'")
    used = sql("SELECT grant_id FROM teacher_grants WHERE grant_status='USED' LIMIT 1")
    assert call('POST', '/grants/' + used + '/revoke')[0] == 409
    assert sql("SELECT grant_status FROM teacher_grants WHERE grant_id='" + used + "'") == 'USED'
    print('PASS: simulated USED cannot be overwritten by revoke; no submission feature is claimed.')
finally:
    stop()
