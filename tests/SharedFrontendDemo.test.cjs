const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
function setup(){const storage=new Map(),window={dispatchEvent(){}};
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../EarlyInterventionCare.Api/wwwroot/js/shared-demo.js'),'utf8'),{window,localStorage:{getItem:k=>storage.get(k),setItem:(k,v)=>storage.set(k,v)},crypto:{randomUUID},Event:class{}});return window.SharedDemo;}
const answer=(count=26)=>({respondent:'示範填表人',date:'2026-10-11',answers:Object.fromEntries(Array.from({length:count},(_,i)=>[i,0])),submittedAt:1});
test('role assignments are separate and duplicates do not erase submitted answers',()=>{const s=setup();assert.equal(s.assign([{formId:'snap',respondentRole:'parent'},{formId:'snap',respondentRole:'teacher'}],'2026-10-20'),2);const t=s.tasks('parent')[0];s.save(t.id,answer(),0);assert.equal(s.assign([{formId:'snap',respondentRole:'parent'}],''),0);assert.equal(s.tasks('parent')[0].status,'done');assert.equal(s.tasks('teacher')[0].status,'pending');assert.throws(()=>s.save(t.id,{},1));});
test('draft revision conflicts and revoked authorization reject writes',()=>{const s=setup();s.assign([{formId:'snap',respondentRole:'teacher'}],'');const g=s.grant(),t=s.tasks()[0];s.save(t.id,{answers:{0:1}},0,g.code);assert.throws(()=>s.save(t.id,{},0,g.code),/其他頁面/);s.revoke();assert.equal(s.verify(g.code),null);assert.throws(()=>s.save(t.id,{},1,g.code),/撤銷/);assert.equal(s.tasks()[0].draft.answers[0],1);const replacement=s.grant();assert.notEqual(replacement.code,g.code);assert.throws(()=>s.save(t.id,{},1,g.code));});
test('one submitted task keeps grant active; all scoped tasks complete it',()=>{const s=setup();s.assign([{formId:'snap',respondentRole:'teacher'},{formId:'clancy',respondentRole:'teacher'}],'');const g=s.grant(),tasks=s.tasks();assert.throws(()=>s.save(tasks[0].id,{...answer(),answers:{}},0,g.code),/必填/);s.save(tasks[0].id,answer(),0,g.code);assert.ok(s.verify(g.code));s.save(tasks[1].id,answer(14),0,g.code);assert.equal(s.read().grant.status,'used');assert.equal(s.verify(g.code),null);assert.equal(s.tasks().filter(t=>t.status==='done').length,2);});
