import concurrent.futures
import copy
import datetime
import uuid
"""Submission, idempotency, rollback and revoke-race tests on disposable port 33318 only.
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
base = 'http://127.0.0.1:5199'
env = os.environ.copy()
env['ConnectionStrings__DefaultConnection'] = 'Server=127.0.0.1;Port=33318;Database=earlycare_dev;User=root;Password=;'
process = None
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

def call(method, path, payload=None, headers=None, opener=None):
    request = urllib.request.Request(base + path, method=method,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers={'Content-Type': 'application/json', **(headers or {})})
    try: response = (opener or client).open(request, timeout=15)
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

prefix = '/api/dev/teacher-workspace'
def setup():
    status, grant = call('POST', prefix + '/grants')
    assert status == 200, 'create'
    assert call('POST', prefix + '/verify', {'code':grant['authorizationCode']})[0] == 200
    status, task = call('GET', prefix + '/task')
    assert status == 200
    form = task['questionnaires'][0]
    body = {'questionnaireVersionId':form['versionId'], 'respondentName':'虛構測試老師',
        'filledOn':'2026-10-05', 'observation':'虛構測試觀察',
        'answers':[{'questionId':q,'optionValue':'1'} for q in form['questionIds']]}
    return grant, body

def submit(grant, body, key=None, opener=None, extra=None):
    return call('POST', prefix+'/tasks/'+grant['taskId']+'/submit',body,
        {'X-Teacher-Submission':'1','Idempotency-Key':key or str(uuid.uuid4()), **(extra or {})}, opener)

def state(grant):
    return sql("SELECT g.grant_status,t.task_status,(SELECT COUNT(*) FROM questionnaire_responses r WHERE r.task_id=t.task_id) FROM teacher_grants g JOIN teacher_grant_tasks l ON l.grant_id=g.grant_id JOIN case_questionnaires t ON t.task_id=l.task_id WHERE g.grant_id='"+grant['grantId']+"'")

try:
    start()
    grant, body = setup()
    invalids=[]
    bad=copy.deepcopy(body);bad['answers'].pop();invalids.append(bad)
    bad=copy.deepcopy(body);bad['answers'][0]['optionValue']='9';invalids.append(bad)
    bad=copy.deepcopy(body);bad['answers'][0]['questionId']='UNKNOWN';invalids.append(bad)
    bad=copy.deepcopy(body);bad['answers'][0]=bad['answers'][1];invalids.append(bad)
    bad=copy.deepcopy(body);bad['respondentName']=' ';invalids.append(bad)
    bad=copy.deepcopy(body);bad['filledOn']='2099-01-01';invalids.append(bad)
    for bad in invalids: assert submit(grant,bad)[0] == 400
    bad=copy.deepcopy(body);bad['questionnaireVersionId']=str(uuid.uuid4())
    assert submit(grant,bad)[0] == 409
    assert call('POST',prefix+'/tasks/'+str(uuid.uuid4())+'/submit',body,{'X-Teacher-Submission':'1','Idempotency-Key':str(uuid.uuid4())})[0] == 404
    assert submit(grant,body,extra={'Origin':'https://untrusted.example'})[0] == 403
    assert call('POST',prefix+'/tasks/'+grant['taskId']+'/submit',body)[0] == 403
    assert state(grant) == 'ACTIVE\tPENDING\t0'
    sql("CREATE TRIGGER test_submission_failure BEFORE INSERT ON questionnaire_responses FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Isolated save failure'")
    try:
        assert submit(grant,body)[0] == 503
        assert state(grant) == 'ACTIVE\tPENDING\t0'
    finally: sql('DROP TRIGGER test_submission_failure')
    print('PASS: missing/illegal/duplicate/unknown answers, version/scope/origin checks; failed save rolls back response/task/grant.')
    key=str(uuid.uuid4())
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        results=list(pool.map(lambda _:submit(grant,body,key),range(6)))
    assert all(s==200 for s,r in results)
    assert len({r['responseId'] for s,r in results})==1
    assert sum(not r['replayed'] for s,r in results)==1
    receipt=results[0][1]
    assert state(grant) == 'USED\tSUBMITTED\t1'
    assert sql("SELECT JSON_LENGTH(answers_json) FROM questionnaire_responses WHERE task_id='"+grant['taskId']+"'") == '26'
    assert sql("SELECT COUNT(*) FROM teacher_sessions WHERE grant_id='"+grant['grantId']+"' AND session_status='ACTIVE'") == '0'
    assert call('GET',prefix+'/task')[0]==401
    # Canonical order and surrounding whitespace do not create a new response.
    ordered=copy.deepcopy(body);ordered['answers'].reverse();ordered['respondentName']=' '+body['respondentName']+' '
    assert submit(grant,ordered,key)[1]['responseId']==receipt['responseId']
    changed=copy.deepcopy(body);changed['answers'][0]['optionValue']='2'
    assert submit(grant,changed,key)[0]==409
    assert submit(grant,body)[0]==401
    assert call('POST',prefix+'/grants/'+grant['grantId']+'/revoke')[0]==409
    stop();start()
    assert submit(grant,body,key)[1]['responseId']==receipt['responseId']
    assert state(grant)=='USED\tSUBMITTED\t1'
    assert call('POST',prefix+'/logout')[0]==204
    assert submit(grant,body,key)[0]==401
    print('PASS: six simultaneous duplicate requests create one immutable response; USED blocks reading/new writes; receipt survives restart and logout disables it.')
    grant,body=setup()
    assert call('POST',prefix+'/grants/'+grant['grantId']+'/revoke')[0]==200
    assert submit(grant,body)[0]==401
    assert state(grant)=='REVOKED\tPENDING\t0'
    for _ in range(6):
        grant,body=setup()
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            a=pool.submit(submit,grant,body)
            b=pool.submit(call,'POST',prefix+'/grants/'+grant['grantId']+'/revoke')
            sent,revoked=a.result(),b.result()
        outcome=state(grant)
        assert (sent[0]==200 and revoked[0]==409 and outcome=='USED\tSUBMITTED\t1') or (sent[0]==401 and revoked[0]==200 and outcome=='REVOKED\tPENDING\t0')
    print('PASS: revoke-first refuses submission; six concurrent submit/revoke races have only valid terminal outcomes.')
    grant,body=setup();key=str(uuid.uuid4())
    assert submit(grant,body,key)[0]==200
    sql("UPDATE teacher_sessions SET receipt_expires_at_utc=UTC_TIMESTAMP()-INTERVAL 1 SECOND WHERE grant_id='"+grant['grantId']+"'")
    assert submit(grant,body,key)[0]==401
    assert call('POST',prefix+'/verify',{'code':grant['authorizationCode']})[0]==401
    stop();start('Production')
    assert submit(grant,body)[0]==404
    print('PASS: receipt expiry, USED code rejection and Production endpoint isolation.')
finally:
    stop()
