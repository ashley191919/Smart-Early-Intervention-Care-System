const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');const fs=require('node:fs');const path=require('node:path');
const ids={task:'11111111-1111-4111-8111-111111111111',version:'22222222-2222-4222-8222-222222222222',response:'33333333-3333-4333-8333-333333333333',consent:'44444444-4444-4444-8444-444444444444'};
function load(file,name){const window={};vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../EarlyInterventionCare.Api/wwwroot/js',file),'utf8'),{window,localStorage:{getItem(){throw Error('localStorage must not be read');},setItem(){throw Error('localStorage must not be written');}}});return window[name];}
const consent=load('parent-consent-client.js','EarlyCareParentConsentClient');
const form=load('parent-questionnaire-client.js','EarlyCareParentQuestionnaireClient');
const document={documentVersionId:ids.version,content:'合成測試本文',contentHash:'A'.repeat(64),scopeCode:'ACCOUNT',version:'test'};
const target={caseId:null,consentId:null,dataUseDecision:'UNDECIDED',teacherParticipation:'UNDECIDED'};
const initial=(revision,respondentRelationship='FATHER')=>({taskId:ids.task,questionnaireVersionId:ids.version,revision,respondentRelationship,answers:[],taskStatus:'PENDING'});
const input={questionnaireVersionId:ids.version,respondentName:'虛構家長',filledOn:'2026-10-22',respondentRelationship:'FATHER',answers:[{questionId:'Q1',optionValue:'0'}],observation:''};
const receipt={taskId:ids.task,questionnaireVersionId:ids.version,responseId:ids.response,taskStatus:'SUBMITTED',submittedAtUtc:'2026-10-22T00:00:00Z'};

test('consent bootstrap uses server state, not browser acceptance',async()=>{const c=consent.create(async p=>{assert.equal(p,'/api/parent/consent/context');return {document,targets:[target]};},()=>ids.consent);assert.equal((await c.load()).targets[0].teacherParticipation,'UNDECIDED');});
test('consent accepts all three teacher states without inventing actor or organization',async()=>{for(const choice of ['UNDECIDED','AGREED','DECLINED']){let sent;const c=consent.create(async(p,o)=>{sent=JSON.parse(o.body);return {consentId:ids.consent,...sent};},()=>ids.response);await c.save(document,target,'AGREED',choice);assert.equal(sent.teacherParticipation,choice);assert.equal(sent.actorUserId,undefined);assert.equal(sent.organizationId,undefined);}});
test('consent dropped request retries same id, never auto retries',async()=>{let calls=0,key;const c=consent.create(async(p,o)=>{const body=JSON.parse(o.body);calls++;if(!key)key=body.requestId;assert.equal(body.requestId,key);if(calls===1)throw Error('disconnect');return {consentId:ids.consent,...body};},()=>ids.response);await assert.rejects(c.save(document,target,'AGREED','DECLINED'));assert.equal(calls,1);await c.save(document,target,'AGREED','DECLINED');assert.equal(calls,2);});
test('consent uncertain outcome cannot silently change a pending decision',async()=>{let calls=0;const c=consent.create(async()=>{calls++;throw Error('disconnect');},()=>ids.response);await assert.rejects(c.save(document,target,'AGREED','UNDECIDED'));await assert.rejects(c.save(document,target,'AGREED','AGREED'),/上次/);assert.equal(calls,1);});
test('consent malformed server receipt does not count as acceptance',async()=>{const c=consent.create(async()=>({consentId:ids.consent,documentVersionId:'wrong'}),()=>ids.response);await assert.rejects(c.save(document,target,'AGREED','DECLINED'),/保存結果/);});
test('consent unconfigured or empty scope never falls back to UI text',async()=>{const c=consent.create(async()=>({document,targets:[]}),()=>ids.response);await assert.rejects(c.load(),/適用/);});
test('CASE consent rejects missing scope identifiers',async()=>{const c=consent.create(async()=>({document:{...document,scopeCode:'CASE'},targets:[target]}),()=>ids.response);await assert.rejects(c.load());});

test('draft revision must be loaded before write',async()=>{let calls=0;const c=form.create(async()=>{calls++;throw Error('unavailable');},()=>ids.response);await assert.rejects(c.draft(ids.task));await assert.rejects(c.save(ids.task,input),/先重新載入/);assert.equal(calls,1);});
test('full unsigned draft revision precision is preserved',async()=>{let sent;const c=form.create(async(p,o)=>{if(!o)return initial('9007199254740993');sent=JSON.parse(o.body);return {...initial('9007199254740994'),taskStatus:'IN_PROGRESS'};},()=>ids.response);await c.draft(ids.task);await c.save(ids.task,input);assert.equal(sent.expectedRevision,'9007199254740993');});
test('numeric, leading zero or oversized revisions are rejected',async()=>{for(const rev of [0,'00','01','18446744073709551616','-1']){const c=form.create(async()=>initial(rev),()=>ids.response);await assert.rejects(c.draft(ids.task));}});
test('409 blocks another save until explicit successful reload',async()=>{let writes=0;const c=form.create(async(p,o)=>{if(!o)return initial('1');writes++;throw Object.assign(Error('conflict'),{status:409});},()=>ids.response);await c.draft(ids.task);await assert.rejects(c.save(ids.task,input));await assert.rejects(c.save(ids.task,input));assert.equal(writes,1);await c.draft(ids.task);await assert.rejects(c.save(ids.task,input));assert.equal(writes,2);});
test('503 does not advance revision and never causes local success',async()=>{let writes=0;const c=form.create(async(p,o)=>{if(!o)return initial('2');writes++;assert.equal(JSON.parse(o.body).expectedRevision,'2');throw Object.assign(Error('unavailable'),{status:503});},()=>ids.response);await c.draft(ids.task);await assert.rejects(c.save(ids.task,input));assert.equal(writes,1);await assert.rejects(c.save(ids.task,input));assert.equal(writes,2);});
test('submit retry preserves original idempotency key and payload',async()=>{let calls=0,first;const c=form.create(async(p,o)=>{if(!o)return initial('0');calls++;if(!first)first={body:o.body,key:o.headers['Idempotency-Key']};assert.equal(o.body,first.body);assert.equal(o.headers['Idempotency-Key'],first.key);if(calls===1)throw Object.assign(Error('disconnect'),{status:0});return receipt;},()=>ids.response);await c.draft(ids.task);await assert.rejects(c.submit(ids.task,input));assert.equal(calls,1);assert.equal((await c.submit(ids.task,input)).taskStatus,'SUBMITTED');});
test('uncertain submit blocks changed answers and subsequent draft save',async()=>{let calls=0;const c=form.create(async(p,o)=>{if(!o)return initial('0');calls++;throw Object.assign(Error('unavailable'),{status:503});},()=>ids.response);await c.draft(ids.task);await assert.rejects(c.submit(ids.task,input));await assert.rejects(c.submit(ids.task,{...input,respondentName:'changed'}),/上次提交/);await assert.rejects(c.save(ids.task,input),/提交結果/);assert.equal(calls,1);});
test('duplicate in-flight submission sends one request',async()=>{let finish,calls=0;const c=form.create(async(p,o)=>{if(!o)return initial('0');calls++;return new Promise(r=>finish=r);},()=>ids.response);await c.draft(ids.task);const first=c.submit(ids.task,input);await assert.rejects(c.submit(ids.task,input),/正在儲存/);assert.equal(calls,1);finish(receipt);await first;});
test('400 rejected submission can be corrected with a new intent',async()=>{let calls=0;const c=form.create(async(p,o)=>{if(!o)return initial('0');calls++;if(calls===1)throw Object.assign(Error('invalid'),{status:400});return receipt;},()=>ids.response);await c.draft(ids.task);await assert.rejects(c.submit(ids.task,input));await c.submit(ids.task,{...input,respondentName:'修正姓名'});assert.equal(calls,2);});
test('receipt must be server SUBMITTED and task/version must match',async()=>{for(const value of [{...receipt,taskStatus:'PENDING'},{...receipt,taskId:ids.version},{...receipt,submittedAtUtc:null}]){const c=form.create(async()=>value,()=>ids.response);await assert.rejects(c.receipt(ids.task));}});
test('foreign task response never advances a draft',async()=>{const c=form.create(async()=>({...initial('0'),taskId:ids.version}),()=>ids.response);await assert.rejects(c.draft(ids.task));});
test('invalid task route is rejected before calling API',async()=>{let calls=0;const c=form.create(async()=>{calls++;},()=>ids.response);await assert.rejects(c.draft('../other'));assert.equal(calls,0);});

test('malformed submit timestamp is not treated as a successful receipt',async()=>{const c=form.create(async(p,o)=>o?{...receipt,submittedAtUtc:'not-a-date'}:initial('0'),()=>ids.response);await c.draft(ids.task);await assert.rejects(c.submit(ids.task,input),/提交回執/);await assert.rejects(c.save(ids.task,input),/提交結果/);});

test('legacy null relationship remains null and unfilled drafts save without inventing a relation',async()=>{let writes=0,sent;const c=form.create(async(p,o)=>{if(!o)return initial('7',null);writes++;sent=JSON.parse(o.body);return {...initial('8',null),taskStatus:'IN_PROGRESS'};},()=>ids.response);assert.equal((await c.draft(ids.task)).respondentRelationship,null);await c.save(ids.task,{...input,respondentRelationship:null});assert.equal(sent.respondentRelationship,null);await assert.rejects(c.submit(ids.task,{...input,respondentRelationship:null}),/關係/);assert.equal(writes,1);});

test('missing or invalid relationship in server response is not silently inferred',async()=>{for(const relation of [undefined,'ADMIN','']){const c=form.create(async()=>initial('0',relation),()=>ids.response);if(relation===undefined){const bad=initial('0');delete bad.respondentRelationship;const missing=form.create(async()=>bad,()=>ids.response);await assert.rejects(missing.draft(ids.task));}else await assert.rejects(c.draft(ids.task));}});

test('changed precheck is denied once and another save requires fresh explicit draft read',async()=>{let writes=0,reads=0;const c=form.create(async(p,o)=>{if(!o){reads++;return initial('1');}writes++;throw Object.assign(Error('資格已變更'),{status:409,code:'ACCESS_PRECHECK_CHANGED'});},()=>ids.response);await c.draft(ids.task);await assert.rejects(c.submit(ids.task,input));assert.equal(writes,1);assert.equal(reads,1);await assert.rejects(c.save(ids.task,input),/重新載入/);assert.equal(writes,1);await c.draft(ids.task);assert.equal(reads,2);assert.equal(form.isAccessInvalidation({status:409,code:'ACCESS_PRECHECK_CHANGED'}),true);assert.equal(form.isAccessInvalidation({status:409,code:'DRAFT_CONFLICT'}),false);});

test('save response that drops relationship is not counted as confirmed saved',async()=>{const c=form.create(async(p,o)=>o?{...initial('1',null),taskStatus:'IN_PROGRESS'}:initial('0'),()=>ids.response);await c.draft(ids.task);await assert.rejects(c.save(ids.task,input),/保存結果/);});

test('unknown hash version blocks writes and new keys even after explicit draft reload, but allows authorized receipt',async()=>{
 let writes=0,keys=0,reads=0,lookups=0;
 const c=form.create(async(p,o)=>{if(p.endsWith('/receipt')){lookups++;return receipt;}if(!o){reads++;return initial('1');}writes++;throw Object.assign(Error('internal detail'),{status:409,code:'IDEMPOTENCY_VERSION_UNSUPPORTED'});},()=>{keys++;return ids.response;});
 await c.draft(ids.task);
 await assert.rejects(c.submit(ids.task,input),/目前無法確認先前提交結果，請聯絡服務人員/);
 assert.equal(writes,1);assert.equal(keys,1);assert.equal(lookups,0);
 await assert.rejects(c.submit(ids.task,input));
 await assert.rejects(c.submit(ids.task,{...input,respondentName:'changed'}));
 await c.draft(ids.task);
 await assert.rejects(c.submit(ids.task,input));await assert.rejects(c.save(ids.task,input));
 assert.equal(writes,1);assert.equal(keys,1);assert.equal(reads,2);
 assert.equal((await c.receipt(ids.task)).responseId,ids.response);assert.equal(lookups,1);
});

test('unknown hash does not fabricate a receipt if authorized lookup is refused',async()=>{
 let writes=0,lookups=0;
 const c=form.create(async(p,o)=>{if(p.endsWith('/receipt')){lookups++;throw Object.assign(Error('not found'),{status:404,code:'TASK_NOT_FOUND'});}if(!o)return initial('0');writes++;throw Object.assign(Error('unknown hash'),{status:409,code:'IDEMPOTENCY_VERSION_UNSUPPORTED'});},()=>ids.response);
 await c.draft(ids.task);await assert.rejects(c.submit(ids.task,input));
 await assert.rejects(c.receipt(ids.task));await assert.rejects(c.submit(ids.task,input));
 assert.equal(writes,1);assert.equal(lookups,1);
});
