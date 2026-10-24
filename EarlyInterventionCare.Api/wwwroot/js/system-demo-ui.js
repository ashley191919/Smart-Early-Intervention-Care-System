'use strict';
(() => {
  if(window.MedicalCaseMode?.mode==='api')return;
  const esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  // Legacy medical entry events belong to login.js, never to the fixture UI.
  const main=document.querySelector('#main-content');
  const detailCase={'/case-detail.html':['DEMO-001','王○安'],'/case-detail-empty.html':['DEMO-024','林○樂']}[location.pathname];
  const fixtures=window.MedicalDemo;
  if(main&&(detailCase||location.pathname==='/staff-dashboard.html')){
    const panel=document.createElement('section');panel.id='shared-tasks';panel.className='system-demo-panel';
    if(detailCase){
      const target=document.querySelector('#detail-panel-forms');if(target){target.replaceChildren(panel);}
    }else{const hero=main.querySelector('.page-heading,.workspace-heading,section');if(hero)hero.after(panel);else main.append(panel);}
    let revision=0;
    async function render(){
      const current=++revision;
      try {
        if(detailCase&&fixtures&&!window.StaffAuth?.getCurrentUser()){panel.textContent='正在驗證醫療端登入狀態…';return;}
        const [caseId,caseName]=detailCase||['DEMO-001','王○安'];
        const localTasks=window.SharedDemo.tasks();
        const entries=fixtures?await fixtures.records(localTasks,caseId,location.search):
          localTasks.filter(t=>t.caseId===caseId).map(task=>({task,source:'local-demo'}));
        if(current!==revision)return;
        const hasDemo=entries.some(entry=>entry.source==='medical-demo');
        const done=entries.filter(e=>e.task.status==='done').length;
        const summary=document.querySelector('.detail-summary-card:nth-child(2)');
        if(summary){summary.querySelector('strong').textContent=`${done} / ${entries.length}`;summary.querySelector('small').textContent=`已完成 ${done} 份，共指派 ${entries.length} 份`;}
        const detailNote=document.querySelector('.detail-demo-note');
        if(detailNote)detailNote.textContent=hasDemo?'Demo 模擬資料 · 展示結果不寫入問卷任務。':'本輪表單進度同步家長與教師提交。';
        panel.innerHTML=`<h2>${esc(caseName)} · ${esc(caseId)} · 本輪問卷進度</h2>`+
          (hasDemo?'<p class="medical-demo-label">Demo 模擬資料</p>':'')+
          '<p>家長與教師答案分開保存。</p><a href="/form-assign.html">指派問卷 →</a>'+
          (entries.length?'<div class="medical-task-table"><table><thead><tr><th scope="col">問卷</th><th scope="col">填答者</th><th scope="col">狀態</th><th scope="col">結果</th></tr></thead><tbody>'+entries.map(entry=>{
            const t=entry.task;
            const href=fixtures?fixtures.responseHref(entry,location.search):'/response-detail.html?'+new URLSearchParams({taskId:t.id,caseId:t.caseId,source:'local-demo'});
            return `<tr><td>${esc(t.name)}${entry.source==='medical-demo'?'<small class="medical-demo-label">Demo 模擬資料</small>':''}</td><td>${esc(({parent:'家長',teacher:'教師'})[t.role]||'未提供')}</td><td>${esc(({pending:'待填寫',draft:'有草稿',done:'已提交'})[t.status]||'未提供')}</td><td>${t.status==='done'?`<a data-demo-result="${esc(t.id)}" href="${esc(href)}">查看原始答案 →</a>`:'尚未提交'}</td></tr>`;
          }).join('')+'</tbody></table></div>':'<p>目前尚未指派任何表單</p>');
      } catch {
        if(current===revision)panel.textContent='暫時無法讀取表單紀錄，請稍後再試。';
      }
    }
    render();window.addEventListener('storage',render);window.addEventListener('focus',render);
    document.addEventListener('staff-authenticated',render);
    document.addEventListener('medical-demo-invalidated',()=>{if(detailCase){revision++;panel.textContent='正在驗證醫療端登入狀態…';}});
  }
  if(detailCase&&location.hash==='#shared-tasks')document.addEventListener('DOMContentLoaded',()=>{document.querySelector('#detail-tab-forms')?.click();document.querySelector('#detail-panel-forms')?.scrollIntoView();});
  if(location.pathname==='/frontend-preview.html')location.replace('/staff-login.html');
})();
