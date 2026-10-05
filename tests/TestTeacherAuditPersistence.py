"""Audit lifecycle/restart/failure tests on the disposable port 33318 database only.
Build and migrate first. No credentials or event payloads are printed.
"""
import argparse
import http.cookiejar
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--assembly', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
mysql = r'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
base = 'http://127.0.0.1:5198'
env = os.environ.copy()
env['ConnectionStrings__DefaultConnection'] = 'Server=127.0.0.1;Port=33318;Database=earlycare_dev;User=root;Password=;'
process = None
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

def call(method, path, payload=None, headers=None):
    request = urllib.request.Request(base + path, method=method,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers={'Content-Type': 'application/json', **(headers or {})})
    try: response = client.open(request, timeout=15)
    except urllib.error.HTTPError as error: response = error
    data = response.read()
    return response.status, json.loads(data) if data else None

def sql(statement):
    result = subprocess.run([mysql, '--no-defaults', '--host=127.0.0.1', '--port=33318',
        '--user=root', '--database=earlycare_dev', '--batch', '--skip-column-names', '-e', statement],
        capture_output=True, text=True)
    if result.returncode: raise RuntimeError('Isolated SQL check failed; details suppressed.')
    return result.stdout.strip()

def stop():
    global process
    if process:
        process.terminate()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
        process = None

def start(environment='Development'):
    global process
    env['ASPNETCORE_ENVIRONMENT'] = environment
    process = subprocess.Popen(['dotnet', args.assembly, '--urls', base],
        cwd=root / 'EarlyInterventionCare.Api', env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(100):
        if process.poll() is not None: raise RuntimeError('Test server exited.')
        try:
            if call('GET', '/api/dev/teacher-workspace/task')[0] in (401, 404): return
        except (OSError, urllib.error.URLError): pass
        time.sleep(.1)
    raise RuntimeError('Test server did not start.')

def events(grant):
    status, result = call('GET', '/api/dev/audit-logs?grantId=' + grant['grantId'])
    assert status == 200
    return result['events']

try:
    start()
    prefix = '/api/dev/teacher-workspace'
    status, grant = call('POST', prefix + '/grants')
    assert status == 200
    assert call('POST', prefix + '/verify', {'code': grant['authorizationCode']})[0] == 200
    assert call('GET', prefix + '/task')[0] == 200
    assert call('POST', prefix + '/logout')[0] == 204
    assert call('POST', prefix + '/grants/' + grant['grantId'] + '/revoke')[0] == 200
    assert call('POST', prefix + '/verify', {'code': grant['authorizationCode']})[0] == 401
    assert call('GET', prefix + '/task')[0] == 401
    saved = events(grant)
    assert {e['action'] for e in saved} == {
        'TeacherWorkspace.Create', 'TeacherWorkspace.Verify', 'TeacherWorkspace.Logout',
        'TeacherWorkspace.Revoke', 'TeacherWorkspace.VerifyDenied'}
    expected_fields = {'eventId','occurredAtUtc','actorType','actorId','action',
        'resourceType','resourceId','result','requestCorrelationId'}
    assert all(set(e) == expected_fields and e['resourceId'] == grant['grantId'] for e in saved)
    assert all(e['occurredAtUtc'].endswith(('+00:00', 'Z')) for e in saved)
    assert grant['authorizationCode'] not in json.dumps(saved)
    stop(); start()
    assert {e['eventId'] for e in events(grant)} == {e['eventId'] for e in saved}
    assert call('GET', '/api/dev/audit-logs?limit=0')[0] == 400
    assert call('GET', '/api/dev/audit-logs', headers={'Host': 'untrusted.example:5198'})[0] == 403
    print('PASS: lifecycle events persist across restart; resource filter, UTC, safe metadata and local access checked.')
    replacement = call('POST', prefix + '/grants')[1]
    current = call('POST', prefix + '/grants')[1]
    assert 'TeacherWorkspace.Replace' in {e['action'] for e in events(replacement)}
    # Only this disposable database is touched. Restore the table even when an assertion fails.
    sql('RENAME TABLE audit_logs TO audit_logs_test_unavailable')
    try:
        assert call('POST', prefix + '/grants/' + current['grantId'] + '/revoke')[0] == 200
        assert sql("SELECT grant_status FROM teacher_grants WHERE grant_id='" + current['grantId'] + "'") == 'REVOKED'
        status, error = call('GET', '/api/dev/audit-logs')
        assert status == 503 and error['code'] == 'AUDIT_UNAVAILABLE'
    finally:
        sql('RENAME TABLE audit_logs_test_unavailable TO audit_logs')
    print('PASS: audit storage failure leaves committed revocation intact and query returns a safe 503.')
    stop(); start('Production')
    assert call('GET', '/api/dev/audit-logs')[0] == 404
    assert call('POST', prefix + '/grants')[0] == 404
    print('PASS: Production blocks development audit and grant endpoints.')
finally:
    stop()
