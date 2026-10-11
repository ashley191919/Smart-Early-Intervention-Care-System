'use strict';
(() => {
  const esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const bar=document.createElement('nav');bar.className='system-demo-bar';bar.setAttribute('aria-label','共用系統示範導覽');
  bar.innerHTML='<strong>早療智慧動態照顧系統 · 共用前端示範</strong><a href="/?role=medical">醫療登入</a><a href="/?role=parent">家長登入</a><a href="/?role=teacher">教師登入</a>';
  document.body.prepend(bar);const reset=document.createElement('button');reset.textContent='重設示範';reset.addEventListener('click',()=>{if(confirm('重設此瀏覽器的共用示範任務、草稿與授權？不會改動 MySQL。')){SharedDemo.reset();location.assign('/?role=medical');}});bar.append(reset);
  function medicalLogin(event){
    event.preventDefault();event.stopImmediatePropagation();
    const host=document.querySelector('#medical-form')||document.querySelector('.login-form');
    const unit=host.querySelector('#unit-code,#organization-code'),account=host.querySelector('#staff-account'),password=host.querySelector('#staff-password');
    let status=host.querySelector('.form-status');if(!status){status=document.createElement('p');status.className='form-status';host.append(status);}
    if(!unit.value.trim()||!account.value.trim()||!password.value){status.textContent='請填寫單位代碼、帳號與密碼。示範登入請使用 DEMO／demo／demo。';status.hidden=false;return;}
    if(unit.value.trim().toUpperCase()!=='DEMO'||account.value.trim()!=='demo'||password.value!=='demo'){password.value='';status.textContent='共用前端示範請使用單位 DEMO、帳號 demo、密碼 demo；正式帳號登入待 API 串接。';status.hidden=false;return;}
    password.value='';location.assign('/staff-dashboard.html');
  }
  document.addEventListener('submit',e=>{if(e.target.id==='medical-form')medicalLogin(e);},true);
  document.addEventListener('click',e=>{if(e.target.closest('.login-button'))medicalLogin(e);},true);
  const medical=document.querySelector('#medical-form')||document.querySelector('.login-form');
  if(medical){
    const area=document.createElement('section');area.className='demo-preview-area';area.setAttribute('aria-label','醫療前端示範');
    const preview=document.createElement('a');preview.href='/staff-dashboard.html';preview.className='demo-preview-link';preview.textContent='進入醫療端示範（不需帳密）';
    const hint=document.createElement('p');hint.textContent='僅體驗前端流程，不驗證正式帳號。';
    area.append(preview,hint);medical.after(area);
  }
  const main=document.querySelector('#main-content');
  if(main&&['/case-detail.html','/staff-dashboard.html'].includes(location.pathname)){
    const panel=document.createElement('section');panel.id='shared-tasks';panel.className='system-demo-panel';
    if(location.pathname==='/case-detail.html'){
      const target=document.querySelector('#detail-panel-forms');if(target){target.replaceChildren(panel);}
    }else{const hero=main.querySelector('.page-heading,.workspace-heading,section');if(hero)hero.after(panel);else main.append(panel);}
    function render(){const tasks=SharedDemo.tasks();const done=tasks.filter(t=>t.status==='done').length;const summary=document.querySelector('.detail-summary-card:nth-child(2)');if(summary){summary.querySelector('strong').textContent=`${done} / ${tasks.length}`;summary.querySelector('small').textContent=`已完成 ${done} 份，共指派 ${tasks.length} 份`;}const detailNote=document.querySelector('.detail-demo-note');if(detailNote)detailNote.textContent='共用前端示範：本輪表單進度同步家長與教師提交；虛構個案，不寫入 MySQL。';panel.innerHTML='<h2>王○安 · DEMO-001 · 本輪問卷進度</h2><p>第 2 輪複評 · 以下為同一瀏覽器共用的示範任務。家長與教師答案分開保存。</p><a href="/form-assign.html">指派問卷 →</a>'+(tasks.length?'<table><thead><tr><th>問卷</th><th>填答者</th><th>狀態</th><th>結果</th></tr></thead><tbody>'+tasks.map(t=>`<tr><td>${esc(t.name)}</td><td>${t.role==='parent'?'家長':'教師'}</td><td>${{pending:'待填寫',draft:'有草稿',done:'已提交'}[t.status]}</td><td>${t.status==='done'?`<button data-demo-result="${t.id}">查看原始答案</button>`:'尚未提交'}</td></tr>`).join('')+'</tbody></table>':'<p>尚未指派問卷，請先按「指派問卷」。</p>');}
    render();window.addEventListener('storage',render);window.addEventListener('focus',render);
    panel.addEventListener('click',e=>{const id=e.target.closest('[data-demo-result]')?.dataset.demoResult;if(!id)return;const t=SharedDemo.tasks().find(t=>t.id===id);if(!t||t.status!=='done')return;
      const dialog=document.createElement('dialog');dialog.innerHTML=`<h2>${esc(t.name)} · ${t.role==='parent'?'家長':'教師'}原始答案</h2><p>示範版本 ${esc(t.version)} · 提交後不能修改</p><p>提交時間：${esc(new Date(t.draft.submittedAt).toLocaleString('zh-TW',{timeZone:'Asia/Taipei'}))}</p><ol>${Object.keys(t.draft.answers).sort((a,b)=>Number(a)-Number(b)).map(i=>`<li>${esc(t.draft.questions?.[i]||`第 ${Number(i)+1} 題`)}：<strong>${esc(t.draft.options?.[t.draft.answers[i]]??`選項代碼 ${t.draft.answers[i]}`)}</strong></li>`).join('')}</ol><p>補充觀察：${esc(t.draft.note)}</p><button>關閉</button>`;dialog.querySelector('button').onclick=()=>{dialog.close();dialog.remove();};dialog.addEventListener('cancel',()=>dialog.remove());document.body.append(dialog);dialog.showModal();});
    if(location.hash==='#shared-tasks')document.addEventListener('DOMContentLoaded',()=>{document.querySelector('#detail-tab-forms')?.click();panel.scrollIntoView();});
  }
  if(location.pathname==='/frontend-preview.html')location.replace('/?role=medical');
})();
