'use strict';
// Shared browser fixtures for DEMO-001 tasks; other binding/contact scenarios remain page-local. No real authentication or API.
(() => {
  if (new URLSearchParams(location.search).get("mode") === "account" || sessionStorage.getItem("earlycare.parent.accessToken")) return;
  const children = [{id:'DEMO-001',name:'王○安',age:'4 歲',sex:'男',relation:'主要照顧者',contact:'家長',phone:'0900-***-000'}];
  const makeTasks = () => [
    {id:'basic-r2',name:'早療評估個案基本資料',kind:'基本資料',status:'pending',round:2,meta:'第 2 輪複評 · 版本 1.0.0'},
    {id:'snap-r2',name:'SNAP-IV 評量表',kind:'家長版',status:'draft',round:2,meta:'第 2 輪複評 · 版本 1.0.0'},
    {id:'clancy-r2',name:'克氏行為量表',kind:'家長版',status:'pending',round:2,meta:'第 2 輪複評 · 版本 1.0.0'},
    {id:'snap-r1',name:'SNAP-IV 評量表',kind:'家長版',status:'done',round:1,meta:'第 1 輪初評 · 版本 1.0.0 · 提交後不可修改',submittedAt:'2026／09／15 10:30'},
    {id:'clancy-r1',name:'克氏行為量表',kind:'家長版',status:'done',round:1,meta:'第 1 輪初評 · 版本 1.0.0 · 提交後不可修改',submittedAt:'2026／09／15 10:45'},
    {id:'satisfaction',name:'早期療育服務滿意度調查',kind:'家長版',status:'done',round:null,meta:'過往服務後調查 · 版本 1.0.0 · 提交後不可修改',submittedAt:'2026／09／30 14:20'}
  ];
  const states = new Map([['DEMO-001',{currentRound:2,roundLabel:'第 2 輪複評',tasks:makeTasks()}]]);
  function initialPage(){const page=new URLSearchParams(location.search).get('page');return ['home','tasks','children','help'].includes(page)?page:'home';}
  let selected=children[0].id, page=initialPage(), filter='all', taskScope='current', toastTimer;
  let previewState='normal';
  const titles={home:['家長照顧工作台','陪伴孩子完成評估前的問卷準備。'],tasks:['我的問卷','只列出醫護為目前孩子指定的表單；提交後不能修改。'],children:['我的孩子','查看已綁定孩子的資料，新增孩子時使用醫護提供的邀請碼。'],help:['使用協助','從登入、綁定孩子到填寫問卷，一步一步完成。']};
  const content=document.querySelector('#page-content'), modal=document.querySelector('#modal'), select=document.querySelector('#child-select');
  const esc=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const current=()=>children.find(c=>c.id===selected);
  const state=()=>states.get(selected);
  const currentTasks=()=>state().tasks.filter(t=>t.round===state().currentRound);
  const labels={pending:'待填寫',draft:'有草稿',done:'已提交'};
  function toast(text){const box=document.querySelector('#toast');box.textContent=text;box.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>box.hidden=true,4000);}
  function dialog(title,html){document.querySelector('#modal-title').textContent=title;document.querySelector('#modal-body').innerHTML=html;if(!modal.open)modal.showModal();}
  let pendingInvite = null;
  const inviteErrors = {
    'DEMO-EXPIRED':'邀請碼已過期，請向原提供邀請的醫護索取新碼。',
    'DEMO-USED':'邀請碼已被使用。若孩子尚未出現在您的清單，請聯絡醫護核對。',
    'DEMO-REVOKED':'邀請碼已被醫護撤銷，請向醫護確認是否需要重新邀請。',
    'DEMO-EMAIL':'邀請指定的信箱與目前登入信箱不符，請向醫護核對登記資料。'
  };
  function bindingSteps(step){
    return `<ol class="binding-steps" aria-label="綁定流程">${['輸入邀請碼','核對孩子','完成'].map((label,i)=>`<li class="${i+1===step?'current':i+1<step?'finished':''}" ${i+1===step?'aria-current="step"':''}><span>${i+1<step?'✓':i+1}</span>${label}</li>`).join('')}</ol>`;
  }
  function bindingView(title,html){
    document.querySelector('#modal-title').textContent=title;
    document.querySelector('#modal-body').innerHTML=html;
    if(!modal.open)modal.showModal();
  }
  function beginBinding(){
    pendingInvite=null;
    bindingView('綁定孩子',`${bindingSteps(1)}<div class="binding-phone">登入信箱 <strong>p***@example.invalid</strong></div><form id="bind-form" novalidate><label class="modal-field">醫護提供的邀請碼<input name="invite" autocomplete="off" spellcheck="false" placeholder="請輸入邀請碼" maxlength="40" aria-describedby="invite-help bind-error" required></label><p class="privacy-note" id="invite-help">可用授權碼：DEMO-CHILD-002</p><p class="error" id="bind-error" role="alert" hidden></p><div class="actions"><button class="btn" type="submit">核對邀請碼</button><button class="btn ghost" type="button" data-action="close">稍後綁定</button></div></form><details class="binding-examples"><summary>查看錯誤情境授權碼</summary><p>DEMO-EXPIRED：已過期<br>DEMO-USED：已使用<br>DEMO-REVOKED：已撤銷<br>DEMO-EMAIL：信箱不符</p></details>`);
  }
  function reviewBinding(){
    bindingView('請核對孩子資料',`${bindingSteps(2)}<p class="modal-copy">請確認這是您要綁定的孩子。資料不符時，請停止操作並聯絡醫護。</p><section class="binding-child" aria-label="邀請對象"><span class="pill">孩子資料</span><dl class="detail-list"><dt>孩子姓名</dt><dd>陳○晴</dd><dt>個案代碼</dt><dd>DEMO-002</dd><dt>出生日期</dt><dd>2023／04／12</dd><dt>性別</dt><dd>女</dd><dt>照顧者關係</dt><dd>主要照顧者</dd></dl></section><label class="check-label"><input id="binding-check" type="checkbox">我已核對上述，這是要加入本頁清單的孩子。</label><p class="privacy-note">此確認只用於畫面體驗，不代表監護關係核實或正式知情同意。</p><div class="actions"><button class="btn" id="confirm-binding" data-action="confirm-binding" disabled>確認綁定</button><button class="btn ghost" data-action="bind">資料不符／重新輸入</button></div>`);
  }
  function finishBinding(){
    if(pendingInvite!=='DEMO-CHILD-002'||!document.querySelector('#binding-check')?.checked)return;
    pendingInvite=null;
    if(children.some(c=>c.id==='DEMO-002')){beginBinding();showInviteError('邀請碼已被使用，第二位孩子已在本頁清單中。');return;}
    children.push({id:'DEMO-002',name:'陳○晴',sex:'女',age:'3 歲',relation:'主要照顧者',contact:'家長',phone:'0900-***-000'});
    states.set('DEMO-002',{currentRound:1,roundLabel:'第 1 輪初評',tasks:[{...makeTasks()[0],id:'basic-r1-child2',round:1,meta:'第 1 輪初評 · 版本 1.0.0'}]});
    selected='DEMO-002';filter='all';taskScope='current';childOptions();navigate('home');
    bindingView('孩子已加入本頁清單',`${bindingSteps(3)}<div class="binding-complete"><span class="binding-success" aria-hidden="true">✓</span><h3>接下來，一起完成評估準備</h3></div><p class="modal-copy">綁定完成後，之後只需Email 驗證碼登入即可看到孩子，不必再次輸入邀請碼。</p><p class="privacy-note">本次未建立正式綁定或保存至資料庫，重新整理會重設。</p><div class="actions"><button class="btn" data-action="binding-home">查看孩子的首頁</button><button class="btn ghost" data-action="binding-child">查看孩子資料</button></div>`);
  }
  function showInviteError(message){
    const form=document.querySelector('#bind-form'),error=document.querySelector('#bind-error');
    error.textContent=message;error.hidden=false;
    form.elements.invite.setAttribute('aria-invalid','true');form.elements.invite.focus();
  }
  modal.addEventListener('close',()=>{pendingInvite=null;});
  modal.addEventListener('cancel',()=>{pendingInvite=null;});
  function childOptions(){select.innerHTML=children.map(c=>`<option value="${c.id}">${esc(c.name)} · ${c.id}</option>`).join('');select.value=selected;}
  function taskRows(tasks){return tasks.map(t=>`<div class="task-row"><div><div class="task-name">${t.name}</div><p class="task-meta">${t.kind} · ${t.meta}${t.submittedAt?`<br>提交：${esc(t.submittedAt)}（臺灣時間）`:""}</p></div><div class="task-end"><span class="pill ${t.status}">${labels[t.status]}</span><button class="btn ${t.status==='done'?'ghost':''}" data-task="${t.id}">${t.status==='draft'?'繼續填寫':t.status==='done'?'提交紀錄':'開始填寫'}</button></div></div>`).join('');}
  function syncShared(){
    const s=states.get('DEMO-001');
    s.tasks=s.tasks.filter(t=>t.round!==2).concat(SharedDemo.tasks('parent').map(t=>({id:t.id,name:t.name,kind:'家長版',status:t.status,round:2,meta:'第 2 輪複評 · 版本 1.0.0',submittedAt:t.draft?.submittedAt?new Date(t.draft.submittedAt).toLocaleString('zh-TW',{timeZone:'Asia/Taipei'}):null})));
  }
  window.addEventListener('storage',()=>render());window.addEventListener('focus',()=>render());
  window.addEventListener('pageshow',event=>{if(event.persisted)render();});
  function render(){if(!window.EarlyCareParentEntryConsent.hasUiChoice())return;syncShared();
    document.querySelector('.child-switch').hidden=previewState!=='normal';
    document.querySelector('#crumb').textContent=page==='home'?'我的首頁':titles[page][0];document.querySelector('#page-title').textContent=titles[page][0];document.querySelector('#page-subtitle').textContent=titles[page][1];
    document.querySelectorAll('[data-page]').forEach(b=>{const active=b.dataset.page===page;b.classList.toggle('active',active);if(active)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');});
    document.querySelector('.nav-count').textContent=selected==='empty'?'0':currentTasks().filter(t=>t.status!=='done').length;
    if(previewState!=='normal'){
      document.querySelector('.nav-count').textContent='0';
      const scenarios={loading:['資料載入中','正在取得孩子與指定問卷資料，請稍候。','載入完成'],error:['暫時無法載入資料','請確認網路連線，再重新載入。您的正式資料不會因載入失敗被刪除。','重新載入'],empty:['目前沒有指定問卷','已綁定孩子，但尚未收到醫護指派的問卷。請與醫護確認，不需自行選擇量表。','返回有問卷情境'],expired:['登入已逾期','請重新以Email 驗證碼登入，再查看孩子資料。','返回家長登入']};
      const [title,description,button]=scenarios[previewState];
      content.innerHTML=`<section class="panel empty"><span class="state-symbol ${previewState==='loading'?'is-loading':''}" aria-hidden="true">${previewState==='loading'?'◌':previewState==='expired'?'⌁':previewState==='error'?'!':'✓'}</span><h2 ${previewState==='loading'?'role="status"':''}>${title}</h2><p>${description}</p>${previewState==='expired'?`<a class="btn" href="/?role=parent">${button}</a>`:`<button class="btn" data-action="reset-state">${button}</button>`}</section>`;
      return;
    }
    if(selected==='empty'&&page!=='help'){content.innerHTML=`<section class="panel empty"><span class="welcome-symbol" style="margin:auto">＋</span><h2>先綁定孩子，開始準備評估資料</h2><p>請使用醫護提供的一次性邀請碼。<br>Email 登入與個案綁定是兩個不同的步驟。</p><button class="btn" data-action="bind">輸入邀請碼</button><p class="privacy-note">本頁不會以姓名、生日或個案編號直接綁定孩子。</p></section>`;return;}
    if(page==='home'){
      const pending=currentTasks().filter(t=>t.status==='pending').length,draft=currentTasks().filter(t=>t.status==='draft').length,done=currentTasks().filter(t=>t.status==='done').length;
      content.innerHTML=`<div class="welcome"><div><span class="welcome-tag">每一份觀察，都是了解孩子的起點</span><h2>家長您好，今天一起完成 ${esc(current().name)} 的評估準備。</h2><p>${current().id} · ${current().sex} · ${current().age} · ${esc(state().roundLabel)}<br>請依照孩子在家中的實際情況填寫指定問卷。</p></div><span class="welcome-symbol" aria-hidden="true">♡</span></div><div class="stats"><div class="stat"><span class="stat-icon"><svg><use href="#form"/></svg></span><div><strong>${pending}</strong><small>份待填寫</small><p>依醫護指定問卷填寫</p></div></div><div class="stat"><span class="stat-icon"><svg><use href="#form"/></svg></span><div><strong>${draft}</strong><small>份草稿</small><p>可接續先前填答</p></div></div><div class="stat"><span class="stat-icon"><svg><use href="#shield"/></svg></span><div><strong>${done}</strong><small>份已提交</small><p>提交後保留原始答案</p></div></div></div><div class="home-grid"><section class="panel"><div class="section-title"><h2>本次待辦問卷</h2><button class="text-button" data-page="tasks">查看全部 →</button></div>${taskRows(currentTasks().filter(t=>t.status!=='done')) || '<p class="modal-copy">本輪目前沒有待辦問卷。</p>'}<p class="privacy-note">問卷種類、版本與複評輪次由醫護指定，不需自行挑選。</p></section><section class="panel teacher-share-card"><h2>邀請老師填寫</h2><p>將授權碼或 QR Code 交給老師，協助提供孩子在校園的觀察。</p><button class="btn" data-action="invite-teacher">邀請老師填寫</button></section></div>`;
    } else if(page==='tasks'){
      const scoped=state().tasks.filter(t=>taskScope==='current'?t.round===state().currentRound:t.round!==state().currentRound);
      const tasks=scoped.filter(t=>filter==='all'||t.status===filter);
      content.innerHTML=`<div class="filterbar" role="group" aria-label="問卷輪次範圍">${[['current','本輪指定問卷'],['history','過往提交紀錄']].map(([key,label])=>`<button data-scope="${key}" class="${taskScope===key?'active':''}" aria-pressed="${taskScope===key}">${label}</button>`).join('')}</div>${taskScope==='current'?`<div class="filterbar" role="group" aria-label="問卷狀態篩選">${[['all','全部'],['pending','待填寫'],['draft','草稿'],['done','已提交']].map(([key,label])=>`<button data-filter="${key}" class="${filter===key?'active':''}" aria-pressed="${filter===key}">${label}</button>`).join('')}</div>`:''}<section class="panel"><div class="section-title"><h2>${esc(current().name)} · ${taskScope==='current'?esc(state().roundLabel):'過往提交紀錄'}</h2><small>全部 ${scoped.length} 份</small></div>${tasks.length?taskRows(tasks):'<p class="modal-copy">目前沒有此範圍或狀態的問卷。</p>'}</section><p class="privacy-note">${taskScope==='history'?'過往提交紀錄保留原輪次與版本，不能重開修改；服務後滿意度調查不計入本輪評估待辦。':'本輪由醫護指定問卷與版本，複評建立新任務，不覆蓋初評紀錄。'} 此為虛構清單，SNAP-IV 與克氏已接上共用前端填答；正式 API 仍待串接。</p>`;
    } else if(page==='children'){
      content.innerHTML=`<div class="split"><section class="panel"><div class="section-title"><h2>孩子基本資料</h2><span class="pill done">已綁定情境</span></div><dl class="detail-list"><dt>孩子姓名</dt><dd>${esc(current().name)}</dd><dt>個案代碼</dt><dd>${current().id}</dd><dt>性別／年齡</dt><dd>${current().sex} ／ ${current().age}</dd><dt>照顧者關係</dt><dd>${current().relation}</dd><dt>主要聯絡人</dt><dd>${esc(current().contact)}</dd><dt>手機號碼</dt><dd>${current().phone}</dd></dl><button class="btn ghost" data-action="contact">更新聯絡人</button><p class="privacy-note">孩子身分資料如需更正，請聯絡醫護核對。</p></section><section class="panel article"><h2>新增另一位孩子</h2><p>醫護建立個案並確認家長關係後，提供指定個案及家長信箱的一次性邀請碼。</p><ol><li>先完成 Email 驗證碼登入。</li><li>輸入醫護提供的邀請碼。</li><li>核對個案後建立綁定關係。</li></ol><button class="btn" data-action="bind">輸入邀請碼</button><p class="privacy-note">後续登入即可看到已綁定孩子，不用重複輸入邀請碼。</p></section></div>`;
    } else {
      content.innerHTML=window.EarlyCareParentHelp.render();
    }
    if(page==='tasks'&&selected==='DEMO-001'){const button=document.createElement('button');button.type='button';button.className='btn secondary';button.dataset.action='try-parent';button.textContent='試填問卷';content.append(button);}
  }
  function navigate(next){page=['home','tasks','children','help'].includes(next)?next:'home';render();document.querySelector('#content').scrollTop=0;document.querySelector('#page-title').focus({preventScroll:true});}
  // Browser presentation only. Formal account mode exits this script above.
  async function inviteTeacher(){
    if(!window.EarlyCareParentEntryConsent.teacherAllowed()){
      dialog('是否邀請老師協助？','<p class="modal-copy">請先確認教師參與的選擇，再取得邀請資訊。不邀請老師也可以繼續填寫家長問卷。</p><div class="actions"><button class="btn" data-action="review-teacher-choice">查看同意選項</button><button class="btn ghost" data-action="close">稍後再說</button></div>');return;
    }
    const rows=SharedDemo.tasks('teacher').filter(t=>t.caseId===selected&&t.round===state().currentRound&&t.status!=='done');
    if(!rows.length){dialog('邀請老師填寫','<p class="modal-copy">目前沒有需要老師填寫的問卷。請與醫護確認是否已指派。</p><button class="btn" data-action="close">我知道了</button>');return;}
    if(selected!=='DEMO-001')return;
    const grant=SharedDemo.grant();
    const url=new URL('/?role=teacher',location.origin);url.hash=new URLSearchParams({code:grant.code}).toString();
    dialog('邀請老師填寫',`<div class="teacher-share-body"><p>請將以下任一方式交給老師，由老師驗證後填寫指定問卷。</p><strong class="grant-code">${esc(grant.code)}</strong><button class="btn ghost" data-copy-teacher="code">複製授權碼</button><img id="parent-teacher-qr" alt="教師填答邀請 QR Code" hidden><p id="teacher-qr-status" role="status">正在準備 QR Code…</p><label>教師填答連結<input id="parent-teacher-url" readonly value="${esc(url.href)}"></label><div class="actions"><button class="btn" data-copy-teacher="url">複製連結</button><a class="btn ghost" href="${esc(url.href)}" target="_blank" rel="noopener noreferrer">開啟教師入口</a></div><p class="privacy-note">請只交給協助填答的老師。指定問卷全部提交或授權撤銷後，授權即失效。</p><p class="teacher-share-feedback" role="status" id="teacher-share-feedback"></p></div>`);
    // This endpoint renders a QR only. It never creates a real Grant or Session.
    try{
      const response=await fetch('/api/dev/presentation-invitation',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code:grant.code}),cache:'no-store'});
      if(!response.ok)throw new Error('QR unavailable');const result=await response.json();
      const image=document.querySelector('#parent-teacher-qr'),status=document.querySelector('#teacher-qr-status');
      if(!modal.open||!image||!status||document.querySelector('#parent-teacher-url')?.value!==url.href)return;
      if(result.invitationUrl!==url.href||!/^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(result.qrCodeDataUrl))throw new Error('QR mismatch');
      image.src=result.qrCodeDataUrl;image.hidden=false;status.textContent='也可請老師掃描 QR Code。';
    }catch{const status=document.querySelector('#teacher-qr-status');if(status)status.textContent='QR Code 暫時無法載入，請使用授權碼或連結。';}
  }
  document.querySelector('#close-modal').addEventListener('click',()=>modal.close());
  select.addEventListener('change',()=>{selected=select.value;filter='all';taskScope='current';navigate('home');});
  document.addEventListener('click',event=>{
    const copyTeacher=event.target.closest('[data-copy-teacher]');
    if(copyTeacher){const field=document.querySelector('#parent-teacher-url'),feedback=document.querySelector('#teacher-share-feedback');if(!field||!feedback)return;const text=copyTeacher.dataset.copyTeacher==='code'?new URLSearchParams(new URL(field.value).hash.slice(1)).get('code'):field.value;navigator.clipboard.writeText(decodeURIComponent(text)).then(()=>{feedback.textContent='已複製，可以貼給老師。';}).catch(()=>{if(copyTeacher.dataset.copyTeacher==='url'){field.focus();field.select();}feedback.textContent='無法自動複製，請長按上方授權碼或選取連結複製。';});return;}
    const retry=event.target.closest('[data-restart-parent]');
    if(retry){const t=SharedDemo.tasks('parent').find(t=>t.id===retry.dataset.restartParent);if(!t)return;dialog('重新填寫這份問卷？',`<p>只重設「${esc(t.name)}」的填答狀態，其他問卷不變。原內容會保留一份備份。</p><div class="actions"><button class="btn" data-confirm-parent-retry="${esc(t.id)}" data-revision="${t.revision}">確認重新填寫</button><button class="btn secondary" data-action="close">取消</button></div>`);return;}
    const confirmRetry=event.target.closest('[data-confirm-parent-retry]');
    if(confirmRetry){try{const t=SharedDemo.restartParent(confirmRetry.dataset.confirmParentRetry,Number(confirmRetry.dataset.revision));location.assign('/parent-questionnaire.html?mode=demo&task='+encodeURIComponent(t.id));}catch(error){toast(error.message);}return;}
    const nav=event.target.closest('[data-page]');if(nav){navigate(nav.dataset.page);return;}
    const scope=event.target.closest('[data-scope]');if(scope){taskScope=scope.dataset.scope;filter='all';render();return;}
    const f=event.target.closest('[data-filter]');if(f){filter=f.dataset.filter;render();return;}
    const task=event.target.closest('[data-task]');
    const sharedTask=task&&SharedDemo.tasks('parent').find(t=>t.id===task.dataset.task);
    if(sharedTask){
      if(sharedTask.status==='done'){
        const saved=sharedTask.draft;
        dialog('提交紀錄',`<h3>${esc(sharedTask.name)}</h3><p>第 ${esc(sharedTask.round)} 輪 · 已提交</p><p>填表人：${esc(saved?.respondent)}</p><p>填表日期：${esc(saved?.date)}</p><p>提交時間：${saved?.submittedAt?esc(new Date(saved.submittedAt).toLocaleString('zh-TW',{timeZone:'Asia/Taipei'})):'未提供'}</p><p>提交後不能修改原始答案。</p><div class="actions"><button class="btn secondary" data-restart-parent="${esc(sharedTask.id)}">重新填寫</button><button class="btn" data-action="close">返回問卷清單</button></div>`);return;
      }
      location.assign('/parent-questionnaire.html?mode=demo&task='+encodeURIComponent(sharedTask.id));return;
    }
    if(task){const t=state().tasks.find(x=>x.id===task.dataset.task);dialog(t.status==='done'?'提交紀錄':t.name,`<button class="btn" data-action="close">我知道了</button>`);return;}
    const action=event.target.closest('[data-action]')?.dataset.action;if(!action)return;
    if(action==='invite-teacher'){inviteTeacher().catch(()=>toast('邀請資訊無法載入，請稍後再試。'));return;}
    if(action==='review-teacher-choice'){modal.close();window.EarlyCareParentEntryConsent.request(true);return;}
    if(action==='try-parent'){
      let t=SharedDemo.tasks('parent').find(t=>t.formId==='snap');
      if(!t){SharedDemo.assign([{formId:'snap',respondentRole:'parent'}],'');t=SharedDemo.tasks('parent').find(t=>t.formId==='snap');}
      if(t.status==='done'){dialog('重新填寫這份問卷？',`<p>只重設 SNAP-IV 的填答狀態，其他問卷不變。原內容會保留一份備份。</p><div class="actions"><button class="btn" data-confirm-parent-retry="${esc(t.id)}" data-revision="${t.revision}">確認重新填寫</button><button class="btn secondary" data-action="close">取消</button></div>`);return;}
      location.assign('/parent-questionnaire.html?mode=demo&task='+encodeURIComponent(t.id));return;
    }
    if(action==='close')modal.close();
    if(action==='reset-state'){previewState='normal';document.querySelector('#preview-state').value='normal';render();}
    if(action==='bind')beginBinding();
    if(action==='confirm-binding')finishBinding();
    if(action==='binding-home'){modal.close();navigate('home');}
    if(action==='binding-child'){modal.close();navigate('children');}
    if(action==='contact')dialog('聯絡人資料',`<p class="modal-copy">請確認主要聯絡人姓名。</p><form id="contact-form"><label class="modal-field">主要聯絡人<input name="contact" value="${esc(current().contact)}" maxlength="30" required></label><button class="btn" type="submit">套用至本頁</button></form>`);
  });
  document.addEventListener('change',event=>{if(event.target.id==='binding-check')document.querySelector('#confirm-binding').disabled=!event.target.checked;});
  document.querySelector('#preview-state').addEventListener('change',event=>{previewState=event.target.value;if(modal.open)modal.close();navigate('home');});
  document.addEventListener('input',event=>{
    if(event.target.name==='contact')event.target.setCustomValidity('');
    if(event.target.name==='invite'){event.target.removeAttribute('aria-invalid');document.querySelector('#bind-error').hidden=true;}
  });
  document.addEventListener('submit',event=>{
    if(event.target.id==='bind-form'){
      event.preventDefault();pendingInvite=null;
      const code=event.target.elements.invite.value.trim().toUpperCase();
      if(!code){showInviteError('請輸入醫護提供的邀請碼。');return;}
      if(inviteErrors[code]){showInviteError(inviteErrors[code]);return;}
      if(code!=='DEMO-CHILD-002'){showInviteError('找不到此邀請碼，請檢查是否輸入完整。');return;}
      if(children.some(c=>c.id==='DEMO-002')){showInviteError('邀請碼已被使用，第二位孩子已在本頁清單中。');return;}
      pendingInvite=code;reviewBinding();
    }
    if(event.target.id==='contact-form'){event.preventDefault();const name=event.target.elements.contact.value.trim();if(!name){event.target.elements.contact.setCustomValidity('請輸入聯絡人名稱。');event.target.elements.contact.reportValidity();return;}current().contact=name;modal.close();render();toast('聯絡人只更新於本頁。');}
  });
  if(window.EarlyCareParentEntryConsent.hasUiChoice()){childOptions();render();}
  else{content.replaceChildren();select.replaceChildren();window.addEventListener('parent-entry-confirmed',()=>{childOptions();render();},{once:true});window.EarlyCareParentEntryConsent.request();}
})();
