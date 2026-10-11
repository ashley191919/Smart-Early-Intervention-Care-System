'use strict';
// Shared browser fixtures for DEMO-001 tasks; other binding/contact scenarios remain page-local. No real authentication or API.
(() => {
  const children = [{id:'DEMO-001',name:'王○安',age:'4 歲',sex:'男',relation:'主要照顧者',contact:'示範家長',phone:'0900-***-000'}];
  const makeTasks = () => [
    {id:'basic-r2',name:'早療評估個案基本資料',kind:'基本資料',status:'pending',round:2,meta:'第 2 輪複評 · 示範版本 1.0.0'},
    {id:'snap-r2',name:'SNAP-IV 評量表',kind:'家長版',status:'draft',round:2,meta:'第 2 輪複評 · 示範版本 1.0.0'},
    {id:'clancy-r2',name:'克氏行為量表',kind:'家長版',status:'pending',round:2,meta:'第 2 輪複評 · 示範版本 1.0.0'},
    {id:'snap-r1',name:'SNAP-IV 評量表',kind:'家長版',status:'done',round:1,meta:'第 1 輪初評 · 示範版本 1.0.0 · 提交後不可修改',submittedAt:'2026／09／15 10:30'},
    {id:'clancy-r1',name:'克氏行為量表',kind:'家長版',status:'done',round:1,meta:'第 1 輪初評 · 示範版本 1.0.0 · 提交後不可修改',submittedAt:'2026／09／15 10:45'},
    {id:'satisfaction',name:'早期療育服務滿意度調查',kind:'家長版',status:'done',round:null,meta:'過往服務後調查 · 示範版本 1.0.0 · 提交後不可修改',submittedAt:'2026／09／30 14:20'}
  ];
  const states = new Map([['DEMO-001',{currentRound:2,roundLabel:'第 2 輪複評',tasks:makeTasks(),grant:null,teacherTasks:[
    {taskId:'teacher-snap-r2',name:'SNAP-IV 教師版',round:2,isCompleted:false},
    {taskId:'teacher-clancy-r2',name:'克氏行為量表教師版',round:2,isCompleted:false}
  ]}]]);
  let selected=children[0].id, page='home', filter='all', taskScope='current', toastTimer;
  let previewState='normal';
  const teacherPreviewUrl=new URL('/?role=teacher',location.origin).href;
  function showShare(){
    const grant=state().grant;
    if(!grant||grant.status!=='active'){toast('這組示範授權已無法分享，請查看目前狀態。');return;}
    dialog('分享教師填答入口（示範）',`<p class="modal-copy">${esc(current().name)} · ${esc(state().roundLabel)}<br>授權問卷：${grantTasks(grant).map(t=>esc(t.name)).join('、')}。<br>正式版只分享指定任務的授權入口。</p><div class="share-layout"><p class="privacy-note">請使用同一瀏覽器的示範登入入口。跨裝置 QR Code 待正式後端串接。</p><div><span class="pill">QR Code 僅連到畫面預覽</span><p class="privacy-note">此碼不含正式授權，無法用於實際填答。localhost 只適用目前這台電腦，其他手機不能直接連線。</p><a class="btn ghost" href="${teacherPreviewUrl}" target="_blank" rel="noopener">開啟教師畫面預覽</a></div></div><label class="modal-field">示範授權碼<input readonly value="${grant.code}" aria-label="示範授權碼"></label><div class="actions"><button class="btn" data-action="copy-code">複製示範碼</button><button class="btn ghost" data-action="copy-link">複製預覽連結</button></div><p class="error" id="copy-error" role="alert" hidden></p><p class="privacy-note">請複製示範碼，到同一瀏覽器的教師登入頁輸入；這不是正式授權。</p>`);
  }
  async function copyShare(kind){
    const grant=state().grant;if(!grant||grant.status!=='active')return;
    try{await navigator.clipboard.writeText(kind==='code'?grant.code:teacherPreviewUrl);toast(kind==='code'?'示範碼已複製，可在教師入口體驗共用流程。':'本機教師預覽連結已複製。');}
    catch{const error=document.querySelector('#copy-error');error.textContent='瀏覽器未允許複製，請手動選取上方示範碼；預覽連結可由「開啟教師畫面預覽」取得。';error.hidden=false;}
  }
  function grantActions(grant){
    if(!teacherTasks().length)return '<p class="privacy-note">醫護尚未指派教師問卷，暫時無法建立授權。請向醫護確認。</p>';
    if(!pendingTeacherTasks().length)return '<p class="privacy-note">全部指定問卷已完成，授權已結束，無需再分享或重新建立。</p>';
    if(grant?.status==='active')return '<button class="btn" data-action="share">分享示範入口</button><button class="btn danger" data-action="revoke">撤銷示範授權</button>';
    return '<button class="btn" data-action="grant">建立示範授權</button>';
  }
  const titles={home:['家長照顧儀表板','把填表與授權集中在這裡，陪伴孩子完成評估前的準備。'],tasks:['我的問卷','只列出醫護為目前孩子指定的表單；提交後不能修改。'],children:['我的孩子','查看已綁定孩子的資料，新增孩子時使用醫護提供的邀請碼。'],teacher:['教師授權與進度','由家長提供指定問卷的授權，邀請教師補充校園觀察。'],consent:['同意與資料使用','了解資料用途與授權範圍；本頁尚未取得正式知情同意。'],help:['使用協助','從綁定孩子、填寫問卷，到邀請老師，一步一步完成。']};
  const content=document.querySelector('#page-content'), modal=document.querySelector('#modal'), select=document.querySelector('#child-select');
  const esc=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const current=()=>children.find(c=>c.id===selected);
  const state=()=>states.get(selected);
  const currentTasks=()=>state().tasks.filter(t=>t.round===state().currentRound);
  const teacherTasks=()=>state().teacherTasks.filter(t=>t.round===state().currentRound);
  const pendingTeacherTasks=()=>teacherTasks().filter(t=>!t.isCompleted);
  const grantTasks=grant=>teacherTasks().filter(t=>grant.taskIds.includes(t.taskId));
  const labels={pending:'待填寫',draft:'有草稿',done:'已提交'};
  function toast(text){const box=document.querySelector('#toast');box.textContent=text;box.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>box.hidden=true,4000);}
  function dialog(title,html){document.querySelector('#modal-title').textContent=title;document.querySelector('#modal-body').innerHTML=html;modal.showModal();}
  let pendingInvite = null;
  const inviteErrors = {
    'DEMO-EXPIRED':'邀請碼已過期，請向原提供邀請的醫護索取新碼。',
    'DEMO-USED':'邀請碼已被使用。若孩子尚未出現在您的清單，請聯絡醫護核對。',
    'DEMO-REVOKED':'邀請碼已被醫護撤銷，請向醫護確認是否需要重新邀請。',
    'DEMO-PHONE':'邀請指定的手機與目前登入手機不符，請向醫護核對登記資料。'
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
    bindingView('綁定孩子（前端示範）',`${bindingSteps(1)}<p class="modal-copy">請輸入醫護提供的一次性邀請碼。正式流程會核對已登入手機與指定個案；本頁只使用虛構資料。</p><div class="binding-phone">示範登入手機 <strong>0900-***-000</strong></div><form id="bind-form" novalidate><label class="modal-field">醫護提供的邀請碼<input name="invite" autocomplete="off" spellcheck="false" placeholder="請輸入邀請碼" maxlength="40" aria-describedby="invite-help bind-error" required></label><p class="privacy-note" id="invite-help">可用示範碼：DEMO-CHILD-002</p><p class="error" id="bind-error" role="alert" hidden></p><div class="actions"><button class="btn" type="submit">核對邀請碼</button><button class="btn ghost" type="button" data-action="close">稍後綁定</button></div></form><details class="binding-examples"><summary>查看錯誤情境示範碼</summary><p>DEMO-EXPIRED：已過期<br>DEMO-USED：已使用<br>DEMO-REVOKED：已撤銷<br>DEMO-PHONE：手機不符</p></details>`);
  }
  function reviewBinding(){
    bindingView('請核對孩子資料',`${bindingSteps(2)}<p class="modal-copy">請確認這是您要綁定的孩子。資料不符時，請停止操作並聯絡醫護。</p><section class="binding-child" aria-label="邀請對象"><span class="pill">虛構資料</span><dl class="detail-list"><dt>孩子姓名</dt><dd>陳○晴</dd><dt>個案代碼</dt><dd>DEMO-002</dd><dt>出生日期</dt><dd>2023／04／12（示範）</dd><dt>性別</dt><dd>女</dd><dt>照顧者關係</dt><dd>主要照顧者（示範）</dd></dl></section><label class="check-label"><input id="binding-check" type="checkbox">我已核對上述示範資料，這是要加入本頁清單的孩子。</label><p class="privacy-note">此確認只用於畫面體驗，不代表監護關係核實或正式知情同意。</p><div class="actions"><button class="btn" id="confirm-binding" data-action="confirm-binding" disabled>確認綁定（示範）</button><button class="btn ghost" data-action="bind">資料不符／重新輸入</button></div>`);
  }
  function finishBinding(){
    if(pendingInvite!=='DEMO-CHILD-002'||!document.querySelector('#binding-check')?.checked)return;
    pendingInvite=null;
    if(children.some(c=>c.id==='DEMO-002')){beginBinding();showInviteError('邀請碼已被使用，第二位示範孩子已在本頁清單中。');return;}
    children.push({id:'DEMO-002',name:'陳○晴',sex:'女',age:'3 歲',relation:'主要照顧者',contact:'示範家長',phone:'0900-***-000'});
    states.set('DEMO-002',{currentRound:1,roundLabel:'第 1 輪初評',tasks:[{...makeTasks()[0],id:'basic-r1-child2',round:1,meta:'第 1 輪初評 · 示範版本 1.0.0'}],grant:null,teacherTasks:[]});
    selected='DEMO-002';filter='all';taskScope='current';childOptions();navigate('home');
    bindingView('孩子已加入本頁示範清單',`${bindingSteps(3)}<div class="binding-complete"><span class="binding-success" aria-hidden="true">✓</span><h3>接下來，一起完成評估準備</h3><p>陳○晴 · DEMO-002<br>已加入本頁虛構孩子清單。</p></div><p class="modal-copy">正式綁定完成後，之後只需手機 OTP 登入即可看到孩子，不必再次輸入邀請碼。</p><p class="privacy-note">本次未建立正式綁定或保存至資料庫，重新整理會重設示範資料。</p><div class="actions"><button class="btn" data-action="binding-home">查看孩子的首頁</button><button class="btn ghost" data-action="binding-child">查看孩子資料</button></div>`);
  }
  function showInviteError(message){
    const form=document.querySelector('#bind-form'),error=document.querySelector('#bind-error');
    error.textContent=message;error.hidden=false;
    form.elements.invite.setAttribute('aria-invalid','true');form.elements.invite.focus();
  }
  modal.addEventListener('close',()=>{pendingInvite=null;});
  modal.addEventListener('cancel',()=>{pendingInvite=null;});
  function childOptions(){select.innerHTML=children.map(c=>`<option value="${c.id}">${esc(c.name)} · ${c.id}</option>`).join('')+'<option value="empty">尚未綁定情境（預覽）</option>';select.value=selected;}
  function taskRows(tasks){return tasks.map(t=>`<div class="task-row"><div><div class="task-name">${t.name}</div><p class="task-meta">${t.kind} · ${t.meta}${t.submittedAt?`<br>示範提交：${esc(t.submittedAt)}（臺灣時間）`:""}</p></div><div class="task-end"><span class="pill ${t.status}">${labels[t.status]}</span><button class="btn ${t.status==='done'?'ghost':''}" data-task="${t.id}">${t.status==='draft'?'繼續填寫':t.status==='done'?'提交紀錄':'開始填寫'}</button></div></div>`).join('');}
  function teacherRows(simulate=false){
    const tasks=teacherTasks(),grant=state().grant;
    if(!tasks.length)return '<p class="privacy-note">本輪尚未指派教師問卷。</p>';
    return tasks.map(t=>`<div class="teacher-task"><div class="teacher-status"><span>${esc(t.name)}</span><span class="pill ${t.isCompleted?'done':'pending'}">${t.isCompleted?'已完成':'尚未完成'}</span></div>${simulate&&!t.isCompleted?`<button class="btn ghost" data-teacher-task="${t.taskId}" ${grant?.status==='active'&&grant.taskIds.includes(t.taskId)?'':'disabled'}>示範這份已提交</button>`:''}</div>`).join('');
  }
  function syncShared(){
    const s=states.get('DEMO-001');
    s.tasks=s.tasks.filter(t=>t.round!==2).concat(SharedDemo.tasks('parent').map(t=>({id:t.id,name:t.name,kind:'家長版',status:t.status,round:2,meta:'第 2 輪複評 · 示範版本 demo-1',submittedAt:t.draft?.submittedAt?new Date(t.draft.submittedAt).toLocaleString('zh-TW',{timeZone:'Asia/Taipei'}):null})));
    s.teacherTasks=SharedDemo.tasks('teacher').map(t=>({taskId:t.id,name:t.name+'教師版',round:2,isCompleted:t.status==='done'}));
    s.grant=SharedDemo.read().grant;
  }
  window.addEventListener('storage',()=>render());window.addEventListener('focus',()=>render());
  function render(){syncShared();
    document.querySelector('.child-switch').hidden=previewState!=='normal';
    document.querySelector('#crumb').textContent=page==='home'?'我的首頁':titles[page][0];document.querySelector('#page-title').textContent=titles[page][0];document.querySelector('#page-subtitle').textContent=titles[page][1];
    document.querySelectorAll('[data-page]').forEach(b=>{const active=b.dataset.page===page;b.classList.toggle('active',active);if(active)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');});
    document.querySelector('.nav-count').textContent=selected==='empty'?'0':currentTasks().filter(t=>t.status!=='done').length;
    if(previewState!=='normal'){
      document.querySelector('.nav-count').textContent='0';
      const scenarios={loading:['資料載入中','正在取得孩子與指定問卷資料，請稍候。','載入完成（示範）'],error:['暫時無法載入資料','請確認網路連線，再重新載入。您的正式資料不會因載入失敗被刪除。','重新載入（示範）'],empty:['目前沒有指定問卷','已綁定孩子，但尚未收到醫護指派的問卷。請與醫護確認，不需自行選擇量表。','返回有問卷情境'],expired:['登入已逾期','請重新以手機 OTP 登入，再查看孩子資料。','返回家長登入']};
      const [title,description,button]=scenarios[previewState];
      content.innerHTML=`<section class="panel empty"><span class="state-symbol ${previewState==='loading'?'is-loading':''}" aria-hidden="true">${previewState==='loading'?'◌':previewState==='expired'?'⌁':previewState==='error'?'!':'✓'}</span><h2 ${previewState==='loading'?'role="status"':''}>${title}</h2><p>${description}</p>${previewState==='expired'?`<a class="btn" href="/?role=parent">${button}</a>`:`<button class="btn" data-action="reset-state">${button}</button>`}<p class="privacy-note">目前為手動切換的前端情境，尚未連接正式登入或資料服務。</p></section>`;
      return;
    }
    if(selected==='empty'&&page!=='help'&&page!=='consent'){content.innerHTML=`<section class="panel empty"><span class="welcome-symbol" style="margin:auto">＋</span><h2>先綁定孩子，開始準備評估資料</h2><p>請使用醫護提供的一次性邀請碼。<br>手機登入與個案綁定是兩個不同的步驟。</p><button class="btn" data-action="bind">輸入邀請碼（示範）</button><p class="privacy-note">本頁不會以姓名、生日或個案編號直接綁定孩子。</p></section>`;return;}
    if(page==='home'){
      const pending=currentTasks().filter(t=>t.status==='pending').length,draft=currentTasks().filter(t=>t.status==='draft').length,done=currentTasks().filter(t=>t.status==='done').length;
      content.innerHTML=`<div class="welcome"><div><span class="welcome-tag">每一份觀察，都是了解孩子的起點</span><h2>家長您好，今天一起完成 ${esc(current().name)} 的評估準備。</h2><p>${current().id} · ${current().sex} · ${current().age} · ${esc(state().roundLabel)}（示範）<br>先填寫家中的觀察，再邀請老師提供校園表現。</p></div><span class="welcome-symbol" aria-hidden="true">♡</span></div><div class="stats"><div class="stat"><span class="stat-icon"><svg><use href="#form"/></svg></span><div><strong>${pending}</strong><small>份待填寫</small><p>依醫護指定問卷填寫</p></div></div><div class="stat"><span class="stat-icon"><svg><use href="#form"/></svg></span><div><strong>${draft}</strong><small>份草稿</small><p>可接續先前填答</p></div></div><div class="stat"><span class="stat-icon"><svg><use href="#shield"/></svg></span><div><strong>${done}</strong><small>份已提交</small><p>提交後保留原始答案</p></div></div></div><div class="home-grid"><section class="panel"><div class="section-title"><h2>本次待辦問卷</h2><button class="text-button" data-page="tasks">查看全部 →</button></div>${taskRows(currentTasks().filter(t=>t.status!=='done')) || '<p class="modal-copy">本輪目前沒有待辦問卷。</p>'}<p class="privacy-note">問卷種類、版本與複評輪次由醫護指定，不需自行挑選。</p></section><aside class="aside-stack"><section class="panel"><h2>邀請老師一起完成</h2><p>${state().grant?.status==='active'?'目前有一組示範授權，可提供給老師。':teacherTasks().length?'授權老師填寫本輪指定的教師問卷。':'本輪尚未指派教師問卷；請與醫護確認。'}</p><button class="btn" data-page="teacher">教師授權與進度 →</button></section><section class="panel"><h2>教師問卷完成進度</h2>${teacherRows()}<p class="privacy-note">家長只查看完成狀態；不提供教師答案、分數或補充觀察。</p></section></aside></div>`;
    } else if(page==='tasks'){
      const scoped=state().tasks.filter(t=>taskScope==='current'?t.round===state().currentRound:t.round!==state().currentRound);
      const tasks=scoped.filter(t=>filter==='all'||t.status===filter);
      content.innerHTML=`<div class="filterbar" role="group" aria-label="問卷輪次範圍">${[['current','本輪指定問卷'],['history','過往提交紀錄']].map(([key,label])=>`<button data-scope="${key}" class="${taskScope===key?'active':''}" aria-pressed="${taskScope===key}">${label}</button>`).join('')}</div>${taskScope==='current'?`<div class="filterbar" role="group" aria-label="問卷狀態篩選">${[['all','全部'],['pending','待填寫'],['draft','草稿'],['done','已提交']].map(([key,label])=>`<button data-filter="${key}" class="${filter===key?'active':''}" aria-pressed="${filter===key}">${label}</button>`).join('')}</div>`:''}<section class="panel"><div class="section-title"><h2>${esc(current().name)} · ${taskScope==='current'?esc(state().roundLabel):'過往提交紀錄'}</h2><small>全部 ${scoped.length} 份</small></div>${tasks.length?taskRows(tasks):'<p class="modal-copy">目前沒有此範圍或狀態的問卷。</p>'}</section><p class="privacy-note">${taskScope==='history'?'過往提交紀錄保留原輪次與版本，不能重開修改；服務後滿意度調查不計入本輪評估待辦。':'本輪由醫護指定問卷與版本，複評建立新任務，不覆蓋初評紀錄。'} 此為虛構清單，SNAP-IV 與克氏已接上共用前端填答；正式 API 仍待串接。</p>`;
    } else if(page==='children'){
      content.innerHTML=`<div class="split"><section class="panel"><div class="section-title"><h2>孩子基本資料</h2><span class="pill done">已綁定情境示範</span></div><dl class="detail-list"><dt>孩子姓名</dt><dd>${esc(current().name)}</dd><dt>個案代碼</dt><dd>${current().id}</dd><dt>性別／年齡</dt><dd>${current().sex} ／ ${current().age}</dd><dt>照顧者關係</dt><dd>${current().relation}</dd><dt>主要聯絡人</dt><dd>${esc(current().contact)}</dd><dt>手機號碼</dt><dd>${current().phone}</dd></dl><button class="btn ghost" data-action="contact">更新聯絡人（示範）</button><p class="privacy-note">孩子身分資料如需更正，請聯絡醫護核對。</p></section><section class="panel article"><h2>新增另一位孩子</h2><p>醫護建立個案並確認家長關係後，提供指定個案及手機的一次性邀請碼。</p><ol><li>先完成手機 OTP 登入。</li><li>輸入醫護提供的邀請碼。</li><li>核對個案後建立綁定關係。</li></ol><button class="btn" data-action="bind">輸入邀請碼（示範）</button><p class="privacy-note">後续登入即可看到已綁定孩子，不用重複輸入邀請碼。</p></section></div>`;
    } else if(page==='teacher'){
      const grant=state().grant;
      content.innerHTML=`<div class="split"><section class="panel article"><div class="section-title"><h2>本輪教師填答授權</h2><span class="pill ${grant?.status==='active'?'done':'pending'}">${grant?grant.status==='active'?'示範有效':grant.status==='used'?'示範已完成':'示範已撤銷':'尚未建立'}</span></div><p>填答對象：${esc(current().name)} · ${current().id}</p><p>${esc(state().roundLabel)} · 指定問卷：${teacherTasks().length?teacherTasks().map(t=>esc(t.name)).join('、'):'尚未指派'}。</p>${grant?`<div class="grant-code">${grant.code}</div>`:''}<p>教師授權不限時；全部指定問卷提交或家長撤銷後失效。撤銷不會刪除已提交的答案。</p><div class="actions">${grantActions(grant)}</div><p class="privacy-note">此示範碼可在共用教師入口使用，僅適用同一瀏覽器的示範資料。</p></section><section class="panel"><h2>指定問卷完成進度</h2>${teacherRows(false)}<p class="privacy-note">僅查看是否完成，家長無法取得教師答案、分數、判讀摘要或草稿。</p><p class="privacy-note">測試方式：建立示範授權後，到教師登入頁輸入示範碼。第一份完成時仍有效，全部完成才結束；完成狀態會同步到本頁，不寫入資料庫。</p><div class="article"><h3>提供授權前請先確認</h3><p>正式流程需核對家長與孩子的有效綁定，以及知情同意紀錄。本次僅示範操作。</p><button class="text-button" data-page="consent">查看同意與資料使用 →</button></div></section></div>`;
    } else if(page==='consent'){
      content.innerHTML=`<div class="split"><section class="panel article"><h2>資料用途與可見範圍</h2><p>家長與教師提供的資料，供獲授權的醫療團隊作為評估參考。</p><ul><li>家長僅查看已綁定孩子的指定問卷與教師完成狀態。</li><li>教師僅填寫本次授權的指定問卷。</li><li>家長與教師提交後不能修改原始答案。</li><li>醫護若修改匯出的 Excel，不會覆蓋系統原始答案。</li></ul></section><section class="panel article"><h2>知情同意紀錄</h2><p>目前沒有正式同意紀錄可查詢。正式同意書內容、版本與取得方式，需要與團隊及院方確認。</p><span class="pill pending">尚未串接</span><p>本頁閱讀說明、勾選或建立示範授權，都不代表已完成正式知情同意。</p><button class="btn ghost" data-page="teacher">返回教師授權</button></section></div>`;
    } else {
      content.innerHTML=`<div class="split"><section class="panel article"><h2>開始使用的四個步驟</h2><ol><li><strong>綁定孩子：</strong>首次使用醫護提供的邀請碼。</li><li><strong>完成指定問卷：</strong>醫護決定表單及版本，填寫途中可保存草稿。</li><li><strong>邀請老師：</strong>由已綁定家長提供指定教師問卷授權。</li><li><strong>確認完成進度：</strong>檢查家長問卷與教師問卷是否完成。</li></ol></section><section class="panel article"><h2>常見問題</h2><h3>沒有看到問卷？</h3><p>請先核對目前選擇的孩子，再與醫護確認是否已指派表單。</p><h3>提交後可以修改嗎？</h3><p>不能直接修改。如需補充資訊，請聯絡醫療團隊。</p><h3>邀請码無法使用？</h3><p>正式流程會核對有效期限、指定手機與使用狀態，請向原提供邀請的醫護確認。</p></section></div>`;
    }
  }
  function navigate(next){page=next;render();document.querySelector('#content').scrollTop=0;document.querySelector('#page-title').focus({preventScroll:true});}
  document.querySelector('#close-modal').addEventListener('click',()=>modal.close());
  select.addEventListener('change',()=>{selected=select.value;filter='all';taskScope='current';navigate('home');});
  document.addEventListener('click',event=>{
    const nav=event.target.closest('[data-page]');if(nav){navigate(nav.dataset.page);return;}
    const scope=event.target.closest('[data-scope]');if(scope){taskScope=scope.dataset.scope;filter='all';render();return;}
    const teacherTask=event.target.closest('[data-teacher-task]');if(teacherTask){
      const grant=state().grant,t=teacherTasks().find(x=>x.taskId===teacherTask.dataset.teacherTask);
      if(!t||t.isCompleted||grant?.status!=='active'||!grant.taskIds.includes(t.taskId))return;
      t.isCompleted=true;if(grantTasks(grant).every(x=>x.isCompleted))grant.status='used';
      render();toast(grant.status==='used'?'全部授權問卷已示範完成，這組授權已結束。':'此份已示範完成；其他指定問卷未完成，授權仍有效。');return;
    }
    const f=event.target.closest('[data-filter]');if(f){filter=f.dataset.filter;render();return;}
    const task=event.target.closest('[data-task]');if(task&&SharedDemo.tasks('parent').some(t=>t.id===task.dataset.task)){location.assign('/parent-questionnaire.html?mode=demo&task='+encodeURIComponent(task.dataset.task));return;}if(task){const t=state().tasks.find(x=>x.id===task.dataset.task);dialog(t.status==='done'?'示範提交紀錄':t.name,`<p class="modal-copy">${t.status==='done'?'此問卷顯示為已提交，家長不可修改。這是虛構狀態，並非正式提交收據。':'此處將接上 16 號的家長版共用問卷填答模組，包含暫存草稿、確認提交及提交紀錄。'}<br><br>${t.kind} · ${esc(current().name)} · ${labels[t.status]}<br>${esc(t.meta)}${t.submittedAt?`<br>示範提交：${esc(t.submittedAt)}（臺灣時間）`:""}<br>目前未載入正式題目或傳送答案。</p><button class="btn" data-action="close">我知道了</button>`);return;}
    const action=event.target.closest('[data-action]')?.dataset.action;if(!action)return;
    if(action==='close')modal.close();
    if(action==='share')showShare();
    if(action==='copy-code')void copyShare('code');
    if(action==='copy-link')void copyShare('link');
    if(action==='reset-state'){previewState='normal';document.querySelector('#preview-state').value='normal';render();}
    if(action==='bind')beginBinding();
    if(action==='confirm-binding')finishBinding();
    if(action==='binding-home'){modal.close();navigate('home');}
    if(action==='binding-child'){modal.close();navigate('children');}
    if(action==='contact')dialog('聯絡人資料（前端示範）',`<p class="modal-copy">僅更新本頁示範聯絡人名稱，不變更手機登入身分或保存至資料庫。</p><form id="contact-form"><label class="modal-field">主要聯絡人<input name="contact" value="${esc(current().contact)}" maxlength="30" required></label><button class="btn" type="submit">套用至本頁示範</button></form>`);
    if(action==='grant'){
      if(!pendingTeacherTasks().length||state().grant?.status==='active')return;
      dialog('建立教師授權（前端示範）',`<p class="modal-copy">為 ${esc(current().name)} · ${esc(state().roundLabel)} 尚未完成的 ${pendingTeacherTasks().length} 份指定教師問卷建立本頁示範碼：</p><ul class="modal-copy">${pendingTeacherTasks().map(t=>`<li>${esc(t.name)}</li>`).join('')}</ul><p class="privacy-note">正式授權須由後端確認綁定與同意紀錄，已完成問卷不重新授權。</p><label class="check-label"><input id="grant-check" type="checkbox">我了解這只是前端示範，不代表正式同意或授權。</label><div class="actions"><button class="btn" id="confirm-grant" data-action="confirm-grant" disabled>建立示範碼</button></div>`);
    }
    if(action==='confirm-grant'&&document.querySelector('#grant-check')?.checked){
      if(!pendingTeacherTasks().length||state().grant?.status==='active')return;
      state().grant=SharedDemo.grant();
      modal.close();render();toast('已建立共用示範碼，請到教師入口輸入此碼。');
    }
    if(action==='revoke')dialog('撤銷教師授權（前端示範）',`<p class="modal-copy">正式撤銷後，老師將無法繼續使用這組授權。已提交答案會保留。本次僅切換本頁的示範狀態。</p><button class="btn danger" data-action="confirm-revoke">確認撤銷示範授權</button>`);
    if(action==='confirm-revoke'){if(state().grant?.status!=='active')return;SharedDemo.revoke();modal.close();render();toast('示範授權已撤銷，未變更任何資料庫紀錄。');}
  });
  document.addEventListener('change',event=>{if(event.target.id==='grant-check')document.querySelector('#confirm-grant').disabled=!event.target.checked;});
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
      if(code!=='DEMO-CHILD-002'){showInviteError('找不到此示範邀請碼，請檢查是否輸入完整。');return;}
      if(children.some(c=>c.id==='DEMO-002')){showInviteError('邀請碼已被使用，第二位示範孩子已在本頁清單中。');return;}
      pendingInvite=code;reviewBinding();
    }
    if(event.target.id==='contact-form'){event.preventDefault();const name=event.target.elements.contact.value.trim();if(!name){event.target.elements.contact.setCustomValidity('請輸入聯絡人名稱。');event.target.elements.contact.reportValidity();return;}current().contact=name;modal.close();render();toast('聯絡人只更新於本頁示範。');}
  });
  childOptions();render();
})();
