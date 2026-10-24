const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
function setup(seed){const storage=new Map(seed?[['earlycare.frontend.v1',JSON.stringify(seed)]]:[]),window={dispatchEvent(){}};
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../EarlyInterventionCare.Api/wwwroot/js/shared-demo.js'),'utf8'),{window,localStorage:{getItem:k=>storage.get(k),setItem:(k,v)=>storage.set(k,v)},crypto:{randomUUID},Event:class{}});return window.SharedDemo;}
const answer=(count=26)=>({respondent:'示範填表人',date:'2026-10-11',answers:Object.fromEntries(Array.from({length:count},(_,i)=>[i,0])),submittedAt:1});

test('reopening one completed parent presentation form preserves a backup and other roles',()=>{const s=setup();s.assign([{formId:'snap',respondentRole:'parent'},{formId:'clancy',respondentRole:'parent'},{formId:'snap',respondentRole:'teacher'}],'');const task=s.tasks('parent')[0];s.save(task.id,{...answer(),relationship:'GRANDPARENT'},0);const before=s.read();s.restartParent(task.id,1);const data=s.read(),reopened=data.tasks.find(t=>t.id===task.id);assert.equal(reopened.status,'pending');assert.equal(reopened.draft,null);assert.equal(reopened.revision,2);assert.equal(data.parentRetryBackups[task.id].draft.relationship,'GRANDPARENT');assert.equal(data.parentRetryBackups[task.id].status,'done');assert.equal(JSON.stringify(data.tasks.filter(t=>t.id!==task.id)),JSON.stringify(before.tasks.filter(t=>t.id!==task.id)));assert.throws(()=>s.restartParent(task.id,1));});
test('parent retry cannot reopen a teacher authorization or an unfinished task',()=>{const s=setup();s.assign([{formId:'snap',respondentRole:'teacher'},{formId:'clancy',respondentRole:'parent'}],'');const g=s.grant(),task=s.tasks('teacher')[0];s.save(task.id,answer(),0,g.code);assert.throws(()=>s.restartParent(task.id,1));assert.throws(()=>s.restartParent(s.tasks('parent')[0].id,0));assert.equal(s.read().grant.status,'used');});
test('role assignments are separate and duplicates do not erase submitted answers',()=>{const s=setup();assert.equal(s.assign([{formId:'snap',respondentRole:'parent'},{formId:'snap',respondentRole:'teacher'}],'2026-10-20'),2);const t=s.tasks('parent')[0];s.save(t.id,answer(),0);assert.equal(s.assign([{formId:'snap',respondentRole:'parent'}],''),0);assert.equal(s.tasks('parent')[0].status,'done');assert.equal(s.tasks('teacher')[0].status,'pending');assert.throws(()=>s.save(t.id,{},1));});
test('draft revision conflicts and revoked authorization reject writes',()=>{const s=setup();s.assign([{formId:'snap',respondentRole:'teacher'}],'');const g=s.grant(),t=s.tasks()[0];s.save(t.id,{answers:{0:1}},0,g.code);assert.throws(()=>s.save(t.id,{},0,g.code),/其他頁面/);s.revoke();assert.equal(s.verify(g.code),null);assert.throws(()=>s.save(t.id,{},1,g.code),/撤銷/);assert.equal(s.tasks()[0].draft.answers[0],1);const replacement=s.grant();assert.notEqual(replacement.code,g.code);assert.throws(()=>s.save(t.id,{},1,g.code));});
test('one submitted task keeps grant active; all scoped tasks complete it',()=>{const s=setup();s.assign([{formId:'snap',respondentRole:'teacher'},{formId:'clancy',respondentRole:'teacher'}],'');const g=s.grant(),tasks=s.tasks();assert.throws(()=>s.save(tasks[0].id,{...answer(),answers:{}},0,g.code),/必填/);s.save(tasks[0].id,answer(),0,g.code);assert.ok(s.verify(g.code));s.save(tasks[1].id,answer(14),0,g.code);assert.equal(s.read().grant.status,'used');assert.equal(s.verify(g.code),null);assert.equal(s.tasks().filter(t=>t.status==='done').length,2);});

test('parent task scope keeps all forms in the same child and round, not only the clicked form',()=>{
 const s=setup();s.assign([{formId:'snap',respondentRole:'parent'},{formId:'clancy',respondentRole:'parent'},{formId:'snap',respondentRole:'teacher'}],'');
 const rows=s.tasks('parent');assert.equal(s.parentTaskScope(rows[1].id).length,2);
 s.save(rows[1].id,answer(14),0);
 const scope=s.parentTaskScope(rows[1].id);assert.equal(scope.filter(t=>t.status==='done').length,1);assert.equal(scope.filter(t=>t.status==='pending').length,1);
 assert.equal(s.tasks('teacher')[0].status,'pending');
});
test('parent task scope excludes another child, another round and teacher tasks; invalid task never falls back',()=>{
 const base={id:'one',caseId:'DEMO-001',round:2,role:'parent'};
 const s=setup({tasks:[base,{...base,id:'two'},{...base,id:'child-two',caseId:'DEMO-002'},{...base,id:'older',round:1},{...base,id:'teacher',role:'teacher'}],grant:null});
 assert.deepEqual(Array.from(s.parentTaskScope('one'),t=>t.id),['one','two']);assert.equal(s.parentTaskScope('missing').length,0);assert.equal(s.parentTaskScope('teacher').length,0);
});
