"""Decode generated QR and check it contains the exact fragment invitation.
Run against Development only. Credentials stay in process memory and are not printed.
Requires pillow and zxing-cpp in the selected Python runtime.
"""
import argparse
import base64
import io
import json
import urllib.error
import urllib.parse
import urllib.request
from PIL import Image
import zxingcpp

parser = argparse.ArgumentParser()
parser.add_argument('--development-url', default='http://127.0.0.1:5196')
args = parser.parse_args()
base = args.development_url.rstrip('/')

def call(method, path, payload=None, headers=None):
    request = urllib.request.Request(base + '/api/dev/teacher-workspace' + path, method=method,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers={'Content-Type':'application/json', **(headers or {})})
    try: response = urllib.request.urlopen(request, timeout=10)
    except urllib.error.HTTPError as error: response = error
    content = response.read()
    return response.status, response.headers, json.loads(content) if content else None

status, headers, grant = call('POST', '/grants')
assert status == 200 and 'no-store' in headers['Cache-Control']
try:
    url = urllib.parse.urlsplit(grant['invitationUrl'])
    assert url.scheme + '://' + url.netloc == base
    assert url.path == '/' and url.query == 'role=teacher'
    assert urllib.parse.parse_qs(url.fragment)['code'] == [grant['authorizationCode']]
    png = base64.b64decode(grant['qrCodeDataUrl'].split(',',1)[1], validate=True)
    result = zxingcpp.read_barcode(Image.open(io.BytesIO(png)))
    assert result is not None and result.text == grant['invitationUrl']
    assert call('POST','/verify',{'code':grant['authorizationCode']})[0] == 200
    assert call('POST','/grants/'+grant['grantId']+'/revoke')[0] == 200
    assert call('POST','/verify',{'code':grant['authorizationCode']})[0] == 401
    assert call('POST','/grants',headers={'Host':'example.invalid'})[0] == 400
    print('PASS: QR decodes to exact same-origin invitation; fragment-only code; no-store; verify/revoke; nonlocal Host rejected.')
finally:
    call('POST','/grants/'+grant['grantId']+'/revoke')
