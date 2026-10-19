const {test}=require('node:test');
const assert=require('node:assert/strict');
const {createClient}=require('../EarlyInterventionCare.Api/wwwroot/js/parent-otp-client.js');
const now=()=>1_000_000;
const challenge={challengeId:'server-challenge',expiresAtUtc:new Date(now()+300000).toISOString(),resendAfterSeconds:60};
const response=(body,status=200)=>({ok:status<400,status,json:async()=>body});
test('disabled Email login fails without returning any locally generated code or login',async()=>{
 const api=createClient(async()=>response({code:'EMAIL_LOGIN_UNAVAILABLE',message:'暫時無法登入'},503),now);
 await assert.rejects(api.request('parent@example.test'),e=>e.status===503&&e.code==='EMAIL_LOGIN_UNAVAILABLE');
 await assert.rejects(api.verify('123456'),/過期/);
});
test('only server challenge/code are sent; userId or role is never invented by client',async()=>{
 let calls=0;const api=createClient(async(path,options)=>{
  assert.equal(options.cache,'no-store');assert.equal(options.credentials,'omit');const body=JSON.parse(options.body);calls++;
  if(calls===1){assert.equal(path,'/api/parent/auth/email/request');assert.deepEqual(body,{email:'parent@example.test'});return response(challenge);}
  assert.equal(path,'/api/parent/auth/email/verify');assert.deepEqual(body,{challengeId:'server-challenge',code:'123456'});return response({role:'PARENT',accessToken:'server-issued-token'});
 },now);
 await api.request('parent@example.test');assert.equal((await api.verify('123456')).accessToken,'server-issued-token');await assert.rejects(api.verify('123456'),/過期/);
});
test('invalid/expired challenge and invalid code cannot complete login',async()=>{
 const expired=createClient(async()=>response({...challenge,expiresAtUtc:new Date(now()-1).toISOString()}),now);
 await assert.rejects(expired.request('parent@example.test'),/有效/);
 const api=createClient(async()=>response(challenge),now);await api.request('parent@example.test');await assert.rejects(api.verify('12a456'),/6 位/);
});
test('non-parent or empty token response never counts as login',async()=>{
 for(const bad of [{role:'ADMIN',accessToken:'token'},{role:'PARENT',accessToken:''}]){
  let calls=0;const api=createClient(async()=>response(++calls===1?challenge:bad),now);await api.request('parent@example.test');await assert.rejects(api.verify('123456'),/登入結果/);
 }
});
test('changing role/email during verification discards late credential result',async()=>{
 let resolve,calls=0;const api=createClient(async()=>++calls===1?response(challenge):new Promise(r=>{resolve=r;}),now);
 await api.request('parent@example.test');const pending=api.verify('123456');await new Promise(r=>setImmediate(r));api.reset();resolve(response({role:'PARENT',accessToken:'old-token'}));await assert.rejects(pending,e=>e.code==='FLOW_CHANGED');
});
test('reset during request cannot revive a cancelled challenge',async()=>{
 let resolve;const api=createClient(()=>new Promise(r=>{resolve=r;}),now);const pending=api.request('parent@example.test');api.reset();resolve(response(challenge));await assert.rejects(pending,e=>e.code==='FLOW_CHANGED');await assert.rejects(api.verify('123456'),/過期/);
});

test('invalid email is rejected before network request',async()=>{
 let calls=0;const api=createClient(async()=>{calls++;return response(challenge);},now);
 for(const email of ['not-an-email','Name <parent@example.test>','parent@example.test\r\nBcc: other@example.test'])await assert.rejects(api.request(email),/電子郵件/);
 assert.equal(calls,0);
});

test('duplicate request and duplicate verify do not send twice',async()=>{
 let resolve,calls=0;const api=createClient(()=>{calls++;return new Promise(r=>{resolve=r;});},now);
 const requesting=api.request('parent@example.test');await assert.rejects(api.request('parent@example.test'),e=>e.code==='BUSY');assert.equal(calls,1);
 resolve(response(challenge));await requesting;
 const verifying=api.verify('123456');await assert.rejects(api.verify('123456'),e=>e.code==='BUSY');assert.equal(calls,2);
 resolve(response({role:'PARENT',accessToken:'server-token'}));await verifying;
});

test('failed resend discards old challenge and never retries automatically',async()=>{
 let calls=0;const api=createClient(async()=>{if(++calls===1)return response(challenge);throw new Error('network failure');},now);
 await api.request('parent@example.test');await assert.rejects(api.request('parent@example.test'),/無法連線/);
 await assert.rejects(api.verify('123456'),/過期/);assert.equal(calls,2);
});

test('non-JSON rate limit preserves status for meaningful feedback',async()=>{
 const api=createClient(async()=>({ok:false,status:429,json:async()=>{throw new Error('not json');}}),now);
 await assert.rejects(api.request('parent@example.test'),e=>e.status===429&&/頻繁/.test(e.message));
});
