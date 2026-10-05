"""HTTP checks for synthetic stage-one teacher grants; never prints codes/cookies."""
import argparse
import http.cookiejar
import json
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--development-url', default='http://127.0.0.1:5193')
parser.add_argument('--production-url')
args = parser.parse_args()
base = args.development_url.rstrip('/') + '/api/dev/teacher-workspace'
jar = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

def call(method, path, payload=None, opener=client, prefix=base):
    data = json.dumps(payload).encode() if payload is not None else None
    request = urllib.request.Request(prefix + path, data=data, method=method,
                                     headers={'Content-Type': 'application/json'})
    try:
        response = opener.open(request)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read()
    try:
        body = json.loads(raw) if raw else None
    except json.JSONDecodeError:
        body = None
    return response.status, response.headers, body

status, _, body = call('GET', '/task')
assert status == 401 and 'patient' not in body
assert call('POST', '/verify', {'code': 'NOT-A-VALID-CODE'})[0] == 401
assert call('POST', '/verify', {'code': ''})[0] == 400
status, headers, grant = call('POST', '/grants')
assert status == 200 and 'no-store' in headers['Cache-Control']
code = grant['authorizationCode']
assert len(code) == 19 and len(code.replace('-', '')) == 16
status, headers, verified = call('POST', '/verify', {'code': code.lower(), 'caseId': 'other-case'})
assert status == 200 and 'authorizationCode' not in verified
assert 'httponly' in headers['Set-Cookie'].lower() and 'samesite=strict' in headers['Set-Cookie'].lower()
assert verified['workspaceUrl'] == '/teacher-workspace.html?mode=authorized'
status, headers, task = call('GET', '/task?caseId=other-case&questionnaireId=clancy')
assert status == 200 and 'no-store' in headers['Cache-Control']
assert task['grantId'] == grant['grantId'] and task['patient']['caseId'] == '10000000-0000-4000-8000-000000000001'
assert len(task['questionnaires']) == 1
q = task['questionnaires'][0]
assert q['id'] == 'snap' and q['versionId'] == '30000000-0000-4000-8000-000000000001'
assert len(q['questions']) == 26 and q['options'] == ['完全沒有', '有一點點', '還算不少', '非常的多']
anonymous = urllib.request.build_opener()
assert call('GET', '/task', opener=anonymous)[0] == 401
assert call('POST', '/grants/' + grant['grantId'] + '/revoke')[0] == 200
assert call('GET', '/task')[0] == 401
assert call('POST', '/verify', {'code': code})[0] == 401
assert call('POST', '/grants/' + grant['grantId'] + '/revoke')[0] == 200
grant2 = call('POST', '/grants')[2]
assert call('POST', '/verify', {'code': grant2['authorizationCode']})[0] == 200
assert call('POST', '/logout')[0] == 204
assert call('GET', '/task')[0] == 401
print('PASS: anonymous/invalid/blank/revoked rejection, generated codes, scoped task and versions, cookie flags, no-store, logout.')

if args.production_url:
    prod = args.production_url.rstrip('/') + '/api/dev/teacher-workspace'
    for method, path, payload in [('POST', '/grants', None), ('POST', '/verify', {'code': code}),
                                   ('GET', '/task', None), ('POST', '/logout', None),
                                   ('POST', '/grants/' + grant['grantId'] + '/revoke', None)]:
        assert call(method, path, payload, opener=anonymous, prefix=prod)[0] == 404
    print('PASS: production rejects every development workspace route.')
