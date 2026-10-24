'use strict';
// Only synthetic frontend fixtures. Never an authentication or authorization boundary.
(() => {
  const key='earlycare.frontend.v1';
  const names={snap:'SNAP-IV 評量表',clancy:'克氏行為量表'};
  const seed=()=>({tasks:[],grant:null});
  const read=()=>{try{return JSON.parse(localStorage.getItem(key))||seed();}catch{return seed();}};
  function write(data){localStorage.setItem(key,JSON.stringify(data));window.dispatchEvent(new Event('demo-data-change'));}
  const tasks=role=>read().tasks.filter(t=>!role||t.role===role);
  function parentTaskScope(taskId){
    const rows=tasks('parent'),requested=rows.find(t=>t.id===taskId);
    return requested?rows.filter(t=>t.caseId===requested.caseId&&t.round===requested.round):[];
  }
  function assign(items,deadline){
    if(items.some(i=>!names[i.formId]))throw new Error('目前共用填答已接上 SNAP-IV 與克氏量表；其他表單仍待提供題目。');
    const data=read();let added=0;
    items.forEach(i=>{if(data.tasks.some(t=>t.formId===i.formId&&t.role===i.respondentRole))return;
      data.tasks.push({id:crypto.randomUUID(),formId:i.formId,role:i.respondentRole,name:names[i.formId],caseId:'DEMO-001',round:2,version:'demo-1',deadline,status:'pending',revision:0,draft:null});added++;});
    write(data);return added;
  }
  function grant(){const data=read(),ids=data.tasks.filter(t=>t.role==='teacher'&&t.caseId==='DEMO-001'&&t.round===2&&t.status!=='done').map(t=>t.id);
    if(!ids.length)throw new Error('醫療端尚未指派待完成的教師問卷。');
    if(data.grant?.status==='active'&&ids.every(id=>data.grant.taskIds.includes(id))&&data.grant.taskIds.every(id=>data.tasks.some(t=>t.id===id&&t.role==='teacher'&&t.caseId==='DEMO-001'&&t.round===2)))return data.grant;
    data.grant={code:'DEMO-'+crypto.randomUUID().slice(0,8).toUpperCase(),status:'active',taskIds:ids};write(data);return data.grant;
  }
  function verify(code){const data=read();return data.grant?.code===code&&data.grant.status==='active'?data.grant:null;}
  function revoke(){const data=read();if(data.grant)data.grant.status='revoked';write(data);}
  function save(id,draft,revision,code){const data=read(),t=data.tasks.find(t=>t.id===id);
    if(!t||t.status==='done')throw new Error('任務已提交或不存在，不能修改。');
    if(t.role==='teacher'&&(!data.grant||data.grant.status!=='active'||data.grant.code!==code||!data.grant.taskIds.includes(id)))throw new Error('教師示範授權已撤銷或完成，請返回登入。');
    if(draft.submittedAt){const count=t.formId==='snap'?26:14,max=t.formId==='snap'?3:2;if(!draft.respondent?.trim()||!/^\d{4}-\d{2}-\d{2}$/.test(draft.date||'')||Array.from({length:count},(_,i)=>draft.answers?.[i]).some(v=>!Number.isInteger(v)||v<0||v>max))throw new Error('請完成姓名、日期及全部必填題目。');}
    if(t.revision!==revision)throw new Error('其他頁面已更新這份草稿，請重新整理後再填寫。');
    t.draft=draft;t.status=draft.submittedAt?'done':'draft';t.revision++;
    if(data.grant?.status==='active'&&data.grant.taskIds.every(id=>data.tasks.find(t=>t.id===id)?.status==='done'))data.grant.status='used';
    write(data);return t.revision;
  }
  // Browser presentation only. Never used by formal parent API/account mode.
  function restartParent(id,revision){
    const data=read(),t=data.tasks.find(t=>t.id===id&&t.role==='parent');
    if(!t||t.status!=='done'||t.revision!==revision)throw new Error('問卷狀態已改變，請重新整理後再試。');
    data.parentRetryBackups=data.parentRetryBackups||{};
    data.parentRetryBackups[id]=JSON.parse(JSON.stringify(t));
    t.status='pending';t.draft=null;t.revision++;write(data);return t;
  }
  window.SharedDemo={read,tasks,parentTaskScope,assign,grant,verify,revoke,save,restartParent,key,reset:()=>write(seed())};
})();
