const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const {createClient,summarize,latestRequest}=require('../EarlyInterventionCare.Api/wwwroot/js/parent-account-client.js');
const {loadChildren}=require('../EarlyInterventionCare.Api/wwwroot/js/parent-account-client.js');
const {loadDefinition}=require('../EarlyInterventionCare.Api/wwwroot/js/parent-account-client.js');

const definitionTaskId='a0000000-0000-4000-8000-000000000001';
function definition(){return {taskId:definitionTaskId,questionnaireVersionId:'b0000000-0000-4000-8000-000000000002',title:'原指派問卷',versionNumber:'1',instructions:'說明',questions:[{questionId:'q1',order:1,text:'題目',type:'singleChoice',requiredOnSubmit:true,options:[{value:'0',label:'沒有'}]}]};}
test('definition GET trusts task route only, with no user/version/case supplied by frontend',async()=>{
 const expected=definition();let calls=0;
 const actual=await loadDefinition(async path=>{calls++;assert.equal(path,`/api/parent/tasks/${definitionTaskId}/questionnaire`);return expected;},definitionTaskId);
 assert.equal(actual,expected);assert.equal(calls,1);
});
test('invalid task identifiers never initiate requests',async()=>{
 let calls=0;await assert.rejects(loadDefinition(async()=>{calls++;},'../cases/other'),/識別/);assert.equal(calls,0);
});
test('definition rejects wrong task and malformed questions rather than displaying cached/fallback forms',async()=>{
 for(const change of [d=>{d.taskId='other';},d=>{d.questions=[];},d=>{d.questions[0].options=null;},d=>{d.questions[0].type='unsupported';},d=>{d.instructions={text:'wrong'};}]){
  const body=definition();change(body);await assert.rejects(loadDefinition(async()=>body,definitionTaskId),/格式/);
 }
});
test('definition authorization errors propagate without fallback or retry',async()=>{
 for(const status of [401,403,404,503]){let calls=0;await assert.rejects(loadDefinition(async()=>{calls++;throw Object.assign(new Error('denied'),{status});},definitionTaskId),e=>e.status===status);assert.equal(calls,1);}
});
test('definition loaded after logout is discarded by authenticated client',async()=>{
 const state=session();let reply;const api=createClient(state,()=>new Promise(resolve=>{reply=resolve;}));
 const pending=loadDefinition(api,definitionTaskId);state.signOut();reply({status:200,ok:true,json:async()=>definition()});
 await assert.rejects(pending,e=>e.code==='SESSION_CHANGED');
});
test('refreshing paginated child scope excludes revoked children from previous response',async()=>{
 const old=Array.from({length:100},(_,i)=>({caseId:'old-'+i}));let phase=0;
 const api=async path=>phase===0?(path.includes('page=1&')?old:[{caseId:'revoked'}]):[{caseId:'still-bound'}];
 assert.equal((await loadChildren(api)).length,101);phase=1;assert.deepEqual(await loadChildren(api),[{caseId:'still-bound'}]);
});
test('invalid child list fails instead of retaining cached medical data',async()=>{
 await assert.rejects(loadChildren(async()=>({caseId:'not-a-list'})),/孩子資料/);
});
function session(value='parent-token'){return {value,token(){return this.value;},signOut(){this.value=null;}};}

test('non-JSON access denial retains status so the workspace can clear protected data',async()=>{
 for(const status of [403,404]){
  const state=session();let calls=0;
  const api=createClient(state,async()=>{calls++;return {status,ok:false,json:async()=>{throw new SyntaxError('HTML response');}};});
  await assert.rejects(api('/cases'),e=>e.status===status&&e.code==='INVALID_RESPONSE');
  assert.equal(calls,1);assert.equal(state.value,'parent-token');
 }
});

test('network failure does not retry a possibly committed write or expose transport details',async()=>{
 let calls=0;const api=createClient(session(),async()=>{calls++;throw new TypeError('internal transport detail');});
 await assert.rejects(api('/bindings',{method:'POST',body:'{}'}),e=>e.code==='NETWORK_UNAVAILABLE'&&e.status===0&&!e.message.includes('internal'));
 assert.equal(calls,1);
});

test('session change during failed response parsing discards the old response',async()=>{
 const state=session('old');let fail;
 const api=createClient(state,async()=>({status:403,ok:false,json:()=>new Promise((resolve,reject)=>{fail=reject;})}));
 const pending=api('/cases');await new Promise(resolve=>setImmediate(resolve));state.value='new';fail(new Error('invalid JSON'));
 await assert.rejects(pending,e=>e.code==='SESSION_CHANGED');assert.equal(state.value,'new');
});
test('counts separate pending, drafts and submissions, excluding teacher/cancelled/unknown tasks',()=>{
 const row=(role,status)=>({respondentRole:role,taskStatus:status});
 assert.deepEqual(summarize([row('PARENT','PENDING'),row('PARENT','IN_PROGRESS'),row('PARENT','SUBMITTED'),row('PARENT','CANCELLED'),row('TEACHER','PENDING'),row('PARENT','UNKNOWN')]),{pending:1,drafts:1,submitted:1,outstanding:2});
 assert.deepEqual(summarize([]),{pending:0,drafts:0,submitted:0,outstanding:0});
});
test('requests use current JWT and preserve 409 metadata without retry or revision overwrite',async()=>{
 const state=session();let calls=0;
 const api=createClient(state,async(url,options)=>{calls++;assert.equal(options.headers.Authorization,'Bearer parent-token');assert.equal(options.cache,'no-store');assert.equal(JSON.parse(options.body).revision,3);return {status:409,ok:false,json:async()=>({code:'REVISION_CONFLICT',message:'reload'})};});
 await assert.rejects(api('/profile',{method:'PUT',body:JSON.stringify({revision:3})}),e=>e.status===409&&e.code==='REVISION_CONFLICT');assert.equal(calls,1);assert.equal(state.value,'parent-token');
});
test('missing session does not send a request; expired session is cleared',async()=>{
 let calls=0;const empty=session(null);await assert.rejects(createClient(empty,async()=>{calls++;})('/cases'),e=>e.status===401);assert.equal(calls,0);
 const expired=session();await assert.rejects(createClient(expired,async()=>({status:401}))('/cases'),e=>e.status===401);assert.equal(expired.value,null);
});
test('late unauthorized response from previous account does not clear a newer login',async()=>{
 const state=session('old');let reply;const api=createClient(state,()=>new Promise(resolve=>{reply=resolve;}));const pending=api('/cases');state.value='new';reply({status:401});await assert.rejects(pending,e=>e.code==='SESSION_CHANGED');assert.equal(state.value,'new');
});
test('child/page request gate rejects stale results after switching children or reloading',()=>{
 const gate=latestRequest();const first=gate.begin();const second=gate.begin();assert.equal(gate.current(first),false);assert.equal(gate.current(second),true);gate.begin();assert.equal(gate.current(second),false);
});
test('shared login receiver rejects non-parent/missing responses and redirects only with supplied token',()=>{
 const values=new Map();let destination;
 const window={sessionStorage:{setItem:(k,v)=>values.set(k,v),getItem:k=>values.get(k),removeItem:k=>values.delete(k)},location:{assign:url=>{destination=url;}}};
 vm.runInNewContext(fs.readFileSync(require.resolve('../EarlyInterventionCare.Api/wwwroot/js/parent-session.js'),'utf8'),{window});
 assert.throws(()=>window.EarlyCareParentSession.signIn({role:'PARENT'}));assert.throws(()=>window.EarlyCareParentSession.signIn({role:'ADMIN',accessToken:'x'}));assert.equal(values.size,0);
 window.EarlyCareParentSession.signIn({role:'PARENT',accessToken:'server-issued-token'});assert.equal(destination,'/parent-workspace.html?mode=account');assert.equal(window.EarlyCareParentSession.token(),'server-issued-token');window.EarlyCareParentSession.signOut();assert.equal(values.size,0);
});

test('old account response finishing JSON parsing after a new login is discarded',async()=>{
 const state=session('old');let finish;const api=createClient(state,async()=>({status:200,ok:true,json:()=>new Promise(resolve=>{finish=resolve;})}));
 const pending=api('/cases');await new Promise(resolve=>setImmediate(resolve));state.value='new';finish([{caseId:'old-child'}]);await assert.rejects(pending,e=>e.code==='SESSION_CHANGED');assert.equal(state.value,'new');
});
