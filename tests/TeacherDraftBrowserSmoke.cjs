'use strict';
// Isolated headless Edge + loopback fixture server. Never contacts the API or any database.
const assert=require('node:assert/strict');
const fs=require('node:fs');const path=require('node:path');const os=require('node:os');
const http=require('node:http');const {spawn}=require('node:child_process');
const root=path.resolve(__dirname,'../EarlyInterventionCare.Api/wwwroot');
const snap=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../EarlyInterventionCare.Api/Development/SnapQuestionnaire.json'),'utf8'));
const taskId='40000000-0000-4000-8000-000000000001',versionId='30000000-0000-4000-8000-000000000001';
const form={id:'snap',taskId,versionId,questionnaireId:'20000000-0000-4000-8000-000000000001',title:snap.title,
  desc:'26 題 · 開發測試',options:snap.options,instructions:snap.instructions,source:snap.source,color:'',groups:{0:'注意力不足',9:'過動、衝動',18:'反抗對立'},
  questions:snap.questions,questionIds:snap.questions.map((_,i)=>`SNAP_IV_Q${String(i+1).padStart(2,'0')}`),optionValues:['0','1','2','3']};
const task={grantId:'50000000-0000-4000-8000-000000000001',grantStatus:'ACTIVE',taskId,
  patient:{caseId:'10000000-0000-4000-8000-000000000001',displayName:'測試幼兒（虛構）',sex:'未提供',age:4,caseCode:'CASE-DEMO-001'},questionnaires:[form],notice:'synthetic fixture'};
const empty=()=>({taskId,questionnaireVersionId:versionId,revision:0,respondentName:null,filledOn:null,answers:[],observation:null,updatedAtUtc:null,taskStatus:'PENDING'});
let draft=empty(),mode='normal',writes=0,lastSubmit=null,lastSubmitHeaders=null,gets=0;
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const temporary=fs.mkdtempSync(path.join(os.tmpdir(),'earlycare-draft-browser-'));
const server=http.createServer(async(req,res)=>{
  try{
    const url=new URL(req.url,'http://localhost');
    const json=(value,status=200)=>{res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store'});res.end(JSON.stringify(value));};
    if(url.pathname.startsWith('/api/')){
      if(!req.headers.cookie?.includes('EarlyCare.DevTeacher=synthetic-browser-fixture'))return json({message:'session required'},401);
      if(url.pathname==='/api/dev/teacher-workspace/task')return json(task);
      if(url.pathname===`/api/dev/teacher-workspace/tasks/${taskId}/draft`){
        if(req.method==='GET'){gets++;return json(draft);}
        writes++;const chunks=[];for await(const chunk of req)chunks.push(chunk);const body=JSON.parse(Buffer.concat(chunks).toString());
        if(req.headers['x-teacher-draft']!=='1')return json({message:'header required'},403);
        if(mode==='fail')return json({code:'SAVE_UNAVAILABLE',message:'虛構暫時故障'},503);
        if(mode==='conflict'){
          draft={...draft,revision:draft.revision+1,respondentName:'另一個視窗的虛構教師',answers:[{questionId:form.questionIds[0],optionValue:'3'}]};
          mode='normal';return json({code:'DRAFT_CONFLICT',message:'草稿已有較新版本'},409);
        }
        if(body.expectedRevision!==draft.revision)return json({code:'DRAFT_CONFLICT',message:'stale revision'},409);
        await sleep(80); // Exercise saving status/disabled controls.
        draft={taskId,questionnaireVersionId:versionId,revision:draft.revision+1,respondentName:body.respondentName,
          filledOn:body.filledOn,answers:body.answers,observation:body.observation,updatedAtUtc:'2026-10-09T00:00:00Z',taskStatus:'IN_PROGRESS'};
        return json(draft);
      }
      if(url.pathname===`/api/dev/teacher-workspace/tasks/${taskId}/submit`){
        const chunks=[];for await(const chunk of req)chunks.push(chunk);lastSubmit=JSON.parse(Buffer.concat(chunks).toString());lastSubmitHeaders=req.headers;
        return json({responseId:'60000000-0000-4000-8000-000000000001',taskId,questionnaireVersionId:versionId,
          submittedAtUtc:'2026-10-09T00:01:00Z',grantStatus:'USED',replayed:false});
      }
      return json({message:'fixture route not available'},404);
    }
    const file=path.resolve(root,'.'+decodeURIComponent(url.pathname));
    if(!file.startsWith(root+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404);res.end();return;}
    const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8'};
    res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream','Cache-Control':'no-store',
      'Set-Cookie':'EarlyCare.DevTeacher=synthetic-browser-fixture; HttpOnly; SameSite=Strict; Path=/api/dev/teacher-workspace'});
    res.end(fs.readFileSync(file));
  }catch{res.writeHead(500);res.end();}
});
class CDP{
  constructor(socket){this.socket=socket;this.next=1;this.pending=new Map();this.errors=[];
    socket.addEventListener('message',event=>{const message=JSON.parse(event.data);
      if(message.id){const pending=this.pending.get(message.id);this.pending.delete(message.id);
        if(!pending)return;clearTimeout(pending.timeout);
        if(message.error)pending.reject(new Error(message.error.message));else pending.resolve(message.result);}
      if(message.method==='Runtime.exceptionThrown')this.errors.push(message.params.exceptionDetails.exception?.description||message.params.exceptionDetails.text);
    });
  }
  send(method,params={}){return new Promise((resolve,reject)=>{const id=this.next++;
    const timeout=setTimeout(()=>{this.pending.delete(id);reject(new Error('Browser command timeout: '+method));},10000);
    this.pending.set(id,{resolve,reject,timeout});this.socket.send(JSON.stringify({id,method,params}));});}
  async evaluate(expression){const result=await this.send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
    if(result.exceptionDetails)throw new Error(result.exceptionDetails.exception?.description||result.exceptionDetails.text);return result.result.value;}
  async until(expression){for(let i=0;i<160;i++){if(await this.evaluate(expression))return;await sleep(40);}throw new Error('UI timeout: '+expression);}
  async screenshot(name){const result=await this.send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
    fs.writeFileSync(path.join(temporary,name+'.png'),Buffer.from(result.data,'base64'));}
}
let processHandle,cdp;
(async()=>{
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const port=server.address().port;
  const edge=process.env.TEACHER_TEST_BROWSER||'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
  processHandle=spawn(edge,['--headless=new','--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-sync','--disable-extensions',
    '--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+path.join(temporary,'profile'),'about:blank'],{windowsHide:true,stdio:'ignore'});
  let launchError;processHandle.on('error',error=>launchError=error);
  const active=path.join(temporary,'profile','DevToolsActivePort');
  for(let i=0;i<200&&!fs.existsSync(active);i++){if(launchError)throw launchError;await sleep(50);}
  if(!fs.existsSync(active))throw new Error('Isolated headless browser did not start.');
  const debugPort=fs.readFileSync(active,'utf8').split('\n')[0];
  const target=await(await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`,{method:'PUT'})).json();
  const socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
  cdp=new CDP(socket);await cdp.send('Page.enable');await cdp.send('Runtime.enable');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  const url=`http://127.0.0.1:${port}/teacher-workspace.html?mode=authorized`;
  await cdp.send('Page.navigate',{url});await cdp.until("!!document.querySelector('[data-form=snap]')");
  await cdp.screenshot('home-1440');
  const open=async()=>{await cdp.evaluate("document.querySelector('[data-form=snap]').click()");await cdp.until("!document.getElementById('form-page').hidden && !!document.querySelector('input[name=q25]')");};
  const edit=async(expression)=>cdp.evaluate(expression+";document.getElementById('questionnaire').dispatchEvent(new Event('input',{bubbles:true}));");
  await open();assert.equal(gets,1);assert.equal(await cdp.evaluate("document.getElementById('respondent-date').value"),'');
  await edit("document.querySelector('input[name=q0][value=\"2\"]').checked=true;document.getElementById('observation').value='虛構觀察'");
  await cdp.evaluate("document.getElementById('save-draft').click()");
  await cdp.until("document.getElementById('save-state').textContent.includes('已保存至 DB')");
  assert.equal(draft.revision,1);assert.equal(draft.taskStatus,'IN_PROGRESS');assert.equal(draft.answers.length,1);
  await cdp.evaluate("document.querySelector('.content').scrollTop=document.querySelector('.content').scrollHeight");await cdp.screenshot('saved-1440');
  await cdp.send('Page.reload',{ignoreCache:true});await cdp.until("!!document.querySelector('[data-form=snap]')");await open();
  assert.equal(await cdp.evaluate("document.querySelector('input[name=q0][value=\"2\"]').checked"),true);
  assert.equal(await cdp.evaluate("document.getElementById('observation').value"),'虛構觀察');assert.equal(gets,2);
  mode='conflict';await edit("document.getElementById('respondent-name').value='保留目前输入'");
  await cdp.evaluate("document.getElementById('save-draft').click()");await cdp.until("!document.getElementById('draft-notice').hidden");
  const previousWrites=writes;assert.equal(await cdp.evaluate("document.getElementById('respondent-name').value"),'保留目前输入');
  await cdp.evaluate("document.getElementById('save-draft').click()");await sleep(80);assert.equal(writes,previousWrites);
  await cdp.evaluate("document.getElementById('reload-draft').click()");await cdp.until("document.getElementById('reload-draft-dialog').open");
  const rect=await cdp.evaluate("(()=>{const r=document.getElementById('reload-draft-dialog').getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom};})()");
  assert.ok(rect.x>=0&&rect.y>=0&&rect.right<=1440&&rect.bottom<=900);await cdp.screenshot('conflict-dialog-1440');
  await cdp.evaluate("document.getElementById('cancel-reload-draft').click()");assert.equal(gets,2);
  await cdp.evaluate("document.getElementById('reload-draft').click();document.getElementById('confirm-reload-draft').click()");
  await cdp.until("document.getElementById('respondent-name').value==='另一個視窗的虛構教師'");
  assert.equal(await cdp.evaluate("document.querySelector('input[name=q0][value=\"3\"]').checked"),true);
  mode='fail';await edit("document.querySelector('input[name=q1][value=\"1\"]').checked=true");
  await cdp.evaluate("document.getElementById('save-draft').click()");await cdp.until("document.getElementById('save-state').textContent.includes('儲存失敗')");
  assert.equal(draft.revision,2);assert.equal(await cdp.evaluate("document.querySelector('input[name=q1][value=\"1\"]').checked"),true);
  mode='normal';await cdp.evaluate("document.getElementById('save-draft').click()");await cdp.until("document.getElementById('save-state').textContent.includes('已保存至 DB')");
  assert.equal(draft.revision,3);assert.equal(draft.answers.length,2);
  await cdp.evaluate("document.querySelector('.content').scrollTop=document.querySelector('.content').scrollHeight");
  const metrics=await cdp.evaluate("({width:innerWidth,height:innerHeight,documentWidth:document.documentElement.scrollWidth,mainWidth:document.querySelector('.content').clientWidth,mainScrollWidth:document.querySelector('.content').scrollWidth,mainScrollHeight:document.querySelector('.content').scrollHeight,mainHeight:document.querySelector('.content').clientHeight})");
  assert.equal(metrics.width,1440);assert.equal(metrics.height,900);assert.ok(metrics.documentWidth<=1440&&metrics.mainScrollWidth<=metrics.mainWidth);assert.ok(metrics.mainScrollHeight>metrics.mainHeight);
  await cdp.screenshot('restored-1440');
  const writesBeforeSubmit=writes;
  await edit("document.getElementById('respondent-name').value='虛構教師';document.getElementById('respondent-date').value='2026-10-09';for(let i=0;i<26;i++)document.querySelector('input[name=q'+i+'][value=\"1\"]').checked=true");
  await cdp.evaluate("document.getElementById('questionnaire').requestSubmit()");await cdp.until("document.getElementById('confirm-dialog').open");
  await cdp.evaluate("document.getElementById('confirm-submit').click()");await cdp.until("!document.getElementById('success-page').hidden");
  assert.equal(writes,writesBeforeSubmit);assert.deepEqual(Object.keys(lastSubmit).sort(),['questionnaireVersionId','respondentName','filledOn','answers','observation'].sort());
  assert.equal(lastSubmit.answers.length,26);assert.equal(lastSubmit.answers[0].questionId,'SNAP_IV_Q01');assert.equal(lastSubmit.answers[0].optionValue,'1');
  assert.equal(lastSubmitHeaders['x-teacher-submission'],'1');assert.match(lastSubmitHeaders['idempotency-key'],/^[0-9a-f-]{36}$/i);
  await cdp.screenshot('success-1440');assert.deepEqual(cdp.errors,[]);
  console.log('PASS: isolated browser HTTP fixture; partial save, refresh restore, conflict/no overwrite, confirmed reload, failed save, unchanged submit payload, 1440x900 no horizontal overflow.');
  console.log('Screenshots: '+temporary);
})().catch(error=>{console.error(error.stack);process.exitCode=1;}).finally(async()=>{
  if(cdp){try{await cdp.send('Emulation.clearDeviceMetricsOverride');await cdp.send('Browser.close');}catch{}cdp.socket.close();}
  if(processHandle&&!processHandle.killed)processHandle.kill();server.close();
});
