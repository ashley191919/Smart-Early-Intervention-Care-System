'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const {TeacherDraftClient}=require('../EarlyInterventionCare.Api/wwwroot/js/teacher-draft.js');
const form={taskId:'40000000-0000-4000-8000-000000000001',versionId:'30000000-0000-4000-8000-000000000001',questionIds:['Q1','Q2'],optionValues:['0','1','2','3']};
const dto=(revision=0,extra={})=>({taskId:form.taskId,questionnaireVersionId:form.versionId,revision,
  respondentName:null,filledOn:null,answers:[],observation:null,updatedAtUtc:revision?'2026-10-09T00:00:00Z':null,
  taskStatus:revision?'IN_PROGRESS':'PENDING',...extra});
const response=(body,status=200)=>new Response(JSON.stringify(body),{status});
const input={answers:{0:2},respondent:'',date:'',note:''};

test('GET initializes revision 0; partial PUT sends cookie/header/version/revision and accepts server timestamp',async()=>{
  const requests=[];
  const client=new TeacherDraftClient(async(url,options)=>{
    requests.push({url,options});return response(dto(options.method==='PUT'?1:0,options.method==='PUT'?{answers:[{questionId:'Q1',optionValue:'2'}]}:{}));
  });
  const empty=await client.load(form);assert.equal(empty.revision,'0');assert.equal(empty.savedAt,null);
  const saved=await client.save(form,input);
  assert.equal(saved.revision,'1');assert.equal(saved.taskStatus,'IN_PROGRESS');assert.equal(saved.answers[0],2);
  const put=requests[1];assert.equal(put.options.credentials,'same-origin');assert.equal(put.options.cache,'no-store');
  assert.equal(put.options.headers['X-Teacher-Draft'],'1');assert.deepEqual(JSON.parse(put.options.body),{
    questionnaireVersionId:form.versionId,expectedRevision:0,respondentName:null,filledOn:null,
    answers:[{questionId:'Q1',optionValue:'2'}],observation:null});
  assert.equal(saved.savedAt,Date.parse('2026-10-09T00:00:00Z'));
});
test('new client restores saved partial answers and optional fields from server',async()=>{
  const client=new TeacherDraftClient(async()=>response(dto(3,{respondentName:'測試',filledOn:'2026-10-09',observation:'虛構',answers:[{questionId:'Q2',optionValue:'3'}]})));
  const saved=await client.load(form);assert.deepEqual(saved.answers,{1:3});assert.equal(saved.revision,'3');assert.equal(saved.respondent,'測試');
});
test('409 never overwrites cached server state or retries PUT until explicit successful reload',async()=>{
  let calls=0;
  const client=new TeacherDraftClient(async()=>++calls===1?response(dto(1)):calls===2?response({code:'DRAFT_CONFLICT',message:'conflict'},409):response(dto(7)));
  await client.load(form);
  await assert.rejects(client.save(form,input),{code:'DRAFT_CONFLICT'});
  assert.equal(client.record(form).revision,'1');assert.equal(client.record(form).phase,'conflict');
  await assert.rejects(client.save(form,input),{code:'DRAFT_CONFLICT'});assert.equal(calls,2);
  await client.load(form);assert.equal(client.record(form).revision,'7');assert.equal(client.record(form).conflict,false);
});
test('503 and dropped connections never advance revision or saved time',async()=>{
  for(const failure of ['503','network','invalid-json']){
    let calls=0;
    const client=new TeacherDraftClient(async()=>{
      if(++calls===1)return response(dto(1));
      if(failure==='network')throw new TypeError('Synthetic network failure');
      return failure==='503'?response({message:'unavailable'},503):new Response('not JSON');
    });
    await client.load(form);const previous=client.record(form).snapshot;
    await assert.rejects(client.save(form,input));assert.equal(client.record(form).revision,'1');
    assert.equal(client.record(form).snapshot,previous);assert.equal(client.record(form).phase,'error');
  }
});
test('failed GET cannot silently save with an assumed zero revision',async()=>{
  let calls=0;const client=new TeacherDraftClient(async()=>{calls++;return response({message:'expired'},401);});
  await assert.rejects(client.load(form));await assert.rejects(client.save(form,input),{code:'DRAFT_NOT_LOADED'});assert.equal(calls,1);
});
test('preserves full unsigned revision precision when reading and writing JSON',async()=>{
  let payload;
  const client=new TeacherDraftClient(async(_,options)=>{
    if(options.method==='PUT'){payload=options.body;return new Response(JSON.stringify(dto(1)).replace('"revision":1','"revision":9007199254740994'));}
    return new Response(JSON.stringify(dto(1)).replace('"revision":1','"revision":9007199254740993'));
  });
  await client.load(form);await client.save(form,input);
  assert.match(payload,/"expectedRevision":9007199254740993[,}]/);assert.equal(client.record(form).revision,'9007199254740994');
});
test('rejects foreign task/version, invalid server answer or revision before any save',async()=>{
  for(const patch of [{taskId:'other'},{questionnaireVersionId:'other'},{revision:-1},
    {answers:[{questionId:'UNKNOWN',optionValue:'1'}]},{answers:[{questionId:'Q1',optionValue:'99'}]},
    {answers:[{questionId:'Q1',optionValue:'1'},{questionId:'Q1',optionValue:'2'}]}]){
    const client=new TeacherDraftClient(async()=>response(dto(0,patch)));
    await assert.rejects(client.load(form));assert.equal(client.record(form).loaded,false);
  }
});
test('in-flight save blocks competing calls; no out-of-order revision updates',async()=>{
  let finish;let calls=0;
  const client=new TeacherDraftClient(async(_,options)=>{calls++;return options.method==='PUT'?new Promise(resolve=>finish=resolve):response(dto(0));});
  await client.load(form);const saving=client.save(form,input);
  await assert.rejects(client.save(form,input),{code:'DRAFT_BUSY'});await assert.rejects(client.load(form),{code:'DRAFT_BUSY'});
  assert.equal(calls,2);finish(response(dto(1)));await saving;assert.equal(client.record(form).revision,'1');
});
