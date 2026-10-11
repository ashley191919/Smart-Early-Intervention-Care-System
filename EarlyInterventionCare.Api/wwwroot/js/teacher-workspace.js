
'use strict';
const $=id=>document.getElementById(id);
const authorizedMode=new URLSearchParams(location.search).get('mode')==='authorized';
let assignedTask=null;
const draftClient=authorizedMode?new TeacherDraftClient((...args)=>fetch(...args)):null;
let draftBusy=false,formOpenSequence=0;
if(authorizedMode)document.querySelector('.content').hidden=true;
const icon=name=>`<svg aria-hidden="true"><use href="#i-${name}"/></svg>`;
let forms=[
 {id:'snap',title:'SNAP-IV 評量表',desc:'26 題 · 依過去一個星期的狀況填答 · 四個作答選項',options:['完全沒有','有一點點','還算不少','非常的多'],instructions:'請選擇一個代碼，最能表達在過去的一個星期中，您孩子的狀況。',source:'SNAP.pdf · James M. Swanson, PhD · Translated by Susan Shur-Fen Gau（高淑芬）, M.D., PhD',color:'',groups:{0:'第 1–9 題｜注意力不足',9:'第 10–18 題｜過動、衝動',18:'第 19–26 題｜反抗對立行為'},questions:[
 '無法專注於細節的部分，或在做學校作業或其他活動時，出現粗心的錯誤',
 '很難持續專注於工作或遊戲活動',
 '看起來好像沒有在聽別人對他（她）說話的內容',
 '沒有辦法遵循指示，也無法完成學校作業或家事（並不是由於對立性行為或無法了解指示的內容）',
 '組織規劃工作及活動有困難',
 '逃避，或表達不願意，或有困難於需要持續性動腦的工作（例如學校作業或家庭作業）',
 '會弄丟工作上或活動所必須的東西（例如學校作業、鉛筆、書、工具或玩具）',
 '很容易受外在刺激影響而分心',
 '在日常生活中忘東忘西的',
 '在座位上玩弄手腳或不好好坐著',
 '在教室或其他必須持續坐著的場合，會任意離開座位',
 '在不適當的場合，亂跑或爬高爬低',
 '很難安靜地玩或參與休閒活動',
 '總是一直在動或是像被馬達所驅動',
 '話很多',
 '在問題還沒問完前就急著回答',
 '在遊戲中或團體活動中，無法排隊或等待輪流',
 '打斷或干擾別人（例如：插嘴或打斷別人的遊戲）',
 '發脾氣',
 '與大人爭論',
 '主動地反抗或拒絕大人的要求與規定',
 '故意地做一些事去干擾別人',
 '因自己犯的錯或不適當的行為而怪罪別人',
 '易怒的或很容易被別人激怒',
 '生氣的及怨恨的',
 '惡意的或有報復心的']},
 {id:'clancy',title:'克氏行為量表',desc:'14 題 · 依日常行為表現填答 · 三個作答選項',options:['從不','偶爾','經常'],instructions:'以下列出一些兒童行為的簡短描述。請根據您的孩子日常行為表現，選擇最符合的頻率：「從不」「偶爾」「經常」。原表適用年齡為二至五歲兒童。',source:'克氏行為量表.pdf · Clancy Behavior Scale · 中文版本為宋維村等人修訂',color:'',groups:{0:'日常行為觀察｜共 14 題'},questions:[
 '不易與別人混在一起玩',
 '聽而不聞，好像是聾子',
 '強烈反抗學習，譬如拒絕模仿，或說話做動作',
 '不顧危險',
 '不能接受日常習慣之變化',
 '以手勢表達需要',
 '莫名其妙的笑',
 '不喜歡被人擁抱',
 '活動量過高',
 '避免視線的接觸',
 '過度偏愛某些物品',
 '喜歡旋轉東西',
 '反覆怪異的動作或玩',
 '對周圍漠不關心']}
];
let current=null, toastTimer;
function fresh(){return {forms:{}};}
let state=fresh();
const formatter=new Intl.DateTimeFormat('zh-TW',{timeZone:'Asia/Taipei',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false});
function draft(id){return state.forms[id]||{answers:{},note:'',respondent:'',date:'',savedAt:null,submittedAt:null,dirty:false,revision:null};}
function count(id){return Object.keys(draft(id).answers).length;}
function completed(){return forms.filter(f=>draft(f.id).submittedAt).length;}
function persist(){return true;}
function toast(message){$('toast').textContent=message;$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,3300);}
function navigate(page){for(const p of document.querySelectorAll('.page'))p.hidden=p.id!==page+'-page';$('crumb').textContent={home:'任務首頁',form:'問卷填寫',success:'完成確認',help:'填寫協助'}[page];for(const b of document.querySelectorAll('[data-nav]')){const active=b.dataset.nav===(page==='help'?'help':page==='form'?'tasks':'home');b.classList.toggle('active',active);if(active)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');}document.querySelector('.content').scrollTo({top:0,behavior:'instant'});window.scrollTo({top:0,behavior:'instant'});const h=$(page+'-title');if(h)h.focus({preventScroll:true});}
function renderHome(){const done=completed();$('today').textContent=new Intl.DateTimeFormat('zh-TW',{timeZone:'Asia/Taipei',year:'numeric',month:'long',day:'numeric',weekday:'long'}).format(Date.now());$('deadline-date').textContent='授權不限時';$('task-summary').textContent=`已完成 ${done} / ${forms.length} 份`;$('nav-count').textContent=forms.length-done;if(authorizedMode)$('deadline-note').textContent='已驗證開發測試授權；請完成指定問卷並提交。';$('completion-banner').hidden=done!==forms.length;$('completion-banner').textContent='本次示範填答已全部完成。尚未傳送答案或使用正式授權。';$('session-label').textContent=authorizedMode?'開發測試授權已驗證':done===forms.length?'示範填答已完成':'教師端畫面預覽';$('deadline-note').textContent=done===forms.length?'兩份問卷的示範流程已完成，並未使用正式授權。':'正式版規則：全部提交成功或家長撤銷後失效。本頁尚未驗證或使用任何授權。';$('task-list').innerHTML=forms.map(f=>{const d=draft(f.id),n=count(f.id),sent=!!d.submittedAt;const label=sent?'已完成':d.savedAt?'填寫中':'未開始';return `<article class="panel task"><div class="task-top"><div class="task-icon ${f.color}">${icon('form')}</div><div class="task-title"><h3>${f.title}</h3><p>${f.desc}</p></div><span class="status ${sent?'done':d.savedAt?'progress':''}">${label}</span></div><div class="task-bottom"><div class="task-progress"><div class="progress-label"><span>${sent?(authorizedMode?'已保存提交 ':'本機示範提交 ')+formatter.format(d.submittedAt):`已填題數 ${n} / ${f.questions.length}`}</span><span>${Math.round(n/f.questions.length*100)}%</span></div><div class="track"><span style="width:${n/f.questions.length*100}%"></span></div></div><button type="button" class="btn ${sent?'secondary':''}" data-form="${f.id}" ${sent?'disabled':''}>${sent?'已完成':d.savedAt?'繼續填寫':'開始填寫'} ${icon(sent?'check':'arrow')}</button></div></article>`;}).join('');if(authorizedMode){$('deadline-note').textContent=done===forms.length?'本次答案已保存，授權已完成。':'已驗證開發測試授權；請完成指定問卷並提交。';$('completion-banner').textContent='本次指定問卷已提交並保存至開發資料庫。';$('session-label').textContent=done===forms.length?'本次授權已完成':'開發測試授權已驗證';}}
function openAssignedForm(id){current=forms.find(f=>f.id===id);if(!current||draft(id).submittedAt)return;const d=draft(id);$('form-title').textContent=current.title;$('form-instructions').textContent=current.instructions;const source=document.createElement('span');source.className='source-note';source.textContent='原表說明及題目來源：'+current.source+'。幼兒資料為示範，請勿輸入真實個資。';$('form-instructions').append(source);$('respondent-name').value=d.respondent||'';$('respondent-date').value=authorizedMode?(d.date||''):d.date||new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei',year:'numeric',month:'2-digit',day:'2-digit'}).format(Date.now());$('questions').innerHTML=current.questions.map((q,i)=>`${current.groups[i]?`<h2 class="group-heading">${current.groups[i]}</h2>`:''}<fieldset class="question" id="q-${i}"><legend><span class="qnum">${i+1}</span>${q}<span class="required">必填</span></legend><div class="options ${current.options.length===3?'three':''}">${current.options.map((option,value)=>`<label class="option"><input type="radio" name="q${i}" value="${value}" ${d.answers[i]===value?'checked':''} required><span>${option}</span></label>`).join('')}</div></fieldset>`).join('');$('observation').value=d.note;$('form-error').hidden=true;$('save-state').textContent=d.savedAt?(authorizedMode?'已保存至 DB · ':'本頁上次暫存 ')+formatter.format(d.savedAt):'尚未儲存草稿';
$('reload-draft').hidden=!authorizedMode;$('draft-notice').hidden=true;$('form-deadline').textContent=authorizedMode?'開發測試：提交成功後不可修改，全部指定問卷提交後授權失效。':'正式版授權不限時；本頁為未驗證授權的示範。';$('confirm-submit').disabled=false;updateProgress();navigate('form');}
async function openForm(id){
  if(draftBusy||pendingSubmission){toast('請先完成目前的保存或提交重試。');return;}
  const sequence=++formOpenSequence;
  if(authorizedMode){
    try{
      const response=await fetch('/api/dev/teacher-workspace/task',{credentials:'same-origin',cache:'no-store'});
      if(!response.ok)throw new Error('授權無法使用，請重新驗證。');
      const task=await response.json(),form=task.questionnaires.find(f=>f.id===id);
      if(!form)throw new Error('找不到可填寫的任務。');
      const record=draftClient.record(form);
      if(!record.loaded||!state.forms[id]||state.forms[id].taskId!==form.taskId){
        const saved=record.loaded?record.snapshot:await draftClient.load(form);
        if(sequence!==formOpenSequence)return;
        state.forms[id]={...saved,dirty:false};
      }
      if(sequence!==formOpenSequence)return;
      assignedTask=task;forms=task.questionnaires;
    }catch(error){toast(error.message||'無法載入 DB 草稿，請稍後重試。');return;}
  }
  openAssignedForm(id);
  if(authorizedMode)showDraftStatus();
}
function readAnswers(){const answers={};for(let i=0;i<current.questions.length;i++){const selected=document.querySelector(`input[name="q${i}"]:checked`);if(selected)answers[i]=Number(selected.value);}return answers;}
function updateProgress(){if(!current)return;const n=Object.keys(readAnswers()).length,p=Math.round(n/current.questions.length*100);$('form-progress-text').textContent=`已填 ${n} / ${current.questions.length} 題`;$('form-progress-percent').textContent=p+'%';$('form-progress-bar').style.width=p+'%';}
function captureCurrentInput(){
  if(!current||draft(current.id).submittedAt)return;
  state.forms[current.id]={...draft(current.id),answers:readAnswers(),note:$('observation').value,
    respondent:$('respondent-name').value.trim(),date:$('respondent-date').value};
}
function lockDraftControls(locked){
  draftBusy=locked;
  $('questionnaire').querySelectorAll('input,textarea,button').forEach(el=>el.disabled=locked);
  for(const id of ['reload-draft','confirm-reload-draft','back-home','form-help'])$(id).disabled=locked;
}
function showDraftStatus(message){
  if(!authorizedMode||!current)return;
  const record=draftClient.record(current),d=draft(current.id);
  $('reload-draft').hidden=false;
  $('draft-notice').hidden=!(record.conflict||record.phase==='error');
  $('draft-notice').textContent=record.conflict?'草稿衝突：DB 已有較新版本。你的輸入仍保留，請重新載入最新草稿後再繼續。':'讀寫草稿失敗，輸入仍保留；可重試保存或重新載入。';
  $('save-state').dataset.state=record.conflict?'conflict':record.phase==='error'?'error':d.dirty?'dirty':'saved';
  $('save-state').textContent=message||(record.conflict?'草稿衝突':record.phase==='error'?'儲存失敗，尚未確認保存':d.dirty?'有尚未儲存的變更':d.savedAt?'已保存至 DB · '+formatter.format(d.savedAt):'尚未儲存草稿');
}
async function saveDraft(silent=false){
  if(!current||draft(current.id).submittedAt)return true;
  if(authorizedMode){
    if(draftBusy||pendingSubmission){if(!silent)toast('請先完成目前的保存或提交重試。');return false;}
    const form=current,record=draftClient.record(form);
    if(silent&&!draft(form.id).dirty&&!record.conflict)return true;
    captureCurrentInput();
    const input=draft(form.id);
    lockDraftControls(true);$('save-state').dataset.state='saving';$('save-state').textContent='儲存中…';
    try{
      const saved=await draftClient.save(form,input);
      state.forms[form.id]={...saved,dirty:false};
      showDraftStatus();renderHome();
      if(!silent)toast('草稿已保存至 DB。');return true;
    }catch(error){
      state.forms[form.id]={...input,dirty:true};
      showDraftStatus(error.code==='DRAFT_CONFLICT'?'草稿衝突：請重新載入最新草稿':'儲存失敗，尚未確認保存');
      toast(error.message||'儲存失敗，輸入仍保留。');return false;
    }finally{lockDraftControls(false);}
  }
  captureCurrentInput();state.forms[current.id]={...draft(current.id),savedAt:Date.now()};
  $('save-state').textContent='草稿暫存於本頁 · '+formatter.format(draft(current.id).savedAt);renderHome();
  if(!silent)toast('預覽草稿僅暫存本頁，重新整理後清除。');return true;
}
async function home(){if(draftBusy)return;if(!await saveDraft(true))return;renderHome();navigate('home');}
async function help(){if(draftBusy)return;if(!await saveDraft(true))return;navigate('help');}
document.querySelectorAll('[data-nav]').forEach(b=>b.addEventListener('click',async()=>{
  if(b.dataset.nav==='help')await help();else{await home();if(b.dataset.nav==='tasks'&&!$('home-page').hidden)$('task-heading').scrollIntoView({behavior:'smooth',block:'start'});}
}));
$('reload-draft').addEventListener('click',()=>{if(!draftBusy&&!pendingSubmission)$('reload-draft-dialog').showModal();});
$('cancel-reload-draft').addEventListener('click',()=>$('reload-draft-dialog').close());
$('confirm-reload-draft').addEventListener('click',async()=>{
  if(!authorizedMode||!current||draftBusy)return;
  const form=current;captureCurrentInput();lockDraftControls(true);$('save-state').textContent='載入最新草稿中…';
  try{
    const saved=await draftClient.load(form);
    state.forms[form.id]={...saved,dirty:false};$('reload-draft-dialog').close();
    openAssignedForm(form.id);showDraftStatus();renderHome();toast('已載入 DB 最新草稿。');
  }catch(error){$('reload-draft-dialog').close();showDraftStatus('重新載入失敗，目前輸入仍保留。');toast(error.message||'無法載入最新草稿。');}
  finally{lockDraftControls(false);}
});
window.addEventListener('beforeunload',event=>{
  if(authorizedMode&&(draftBusy||pendingSubmission||Object.values(state.forms).some(d=>d.dirty&&!d.submittedAt))){event.preventDefault();event.returnValue='';}
});
$('task-list').addEventListener('click',e=>{const b=e.target.closest('[data-form]');if(b&&!b.disabled)openForm(b.dataset.form);});
$('questionnaire').addEventListener('input',()=>{
  if(current){captureCurrentInput();state.forms[current.id].dirty=true;}
  if(authorizedMode)showDraftStatus();else $('save-state').textContent='有尚未儲存的變更';updateProgress();
});
$('questionnaire').addEventListener('change',e=>{const q=e.target.closest('.question');if(q)q.classList.remove('invalid');});
$('save-draft').addEventListener('click',()=>saveDraft());$('back-home').addEventListener('click',home);
for(const id of ['help-link','form-help'])$(id).addEventListener('click',help);$('help-return').addEventListener('click',()=>{renderHome();navigate('home');});
$('questionnaire').addEventListener('submit',e=>{e.preventDefault();const person=$('respondent-name'),date=$('respondent-date');if(!person.value.trim()||!date.value||!date.validity.valid){$('form-error').textContent='請先填寫填表人姓名與有效的填表日期。';$('form-error').hidden=false;(!person.value.trim()?person:date).focus();return;}const answers=readAnswers();let first=null;for(let i=0;i<current.questions.length;i++){const missing=answers[i]===undefined;$('q-'+i).classList.toggle('invalid',missing);if(missing&&first===null)first=i;}if(first!==null){$('form-error').textContent='還有必填題目尚未完成，請填寫標示的題目後再送出。';$('form-error').hidden=false;$('q-'+first).querySelector('input').focus();return;}
if(authorizedMode&&(draftBusy||draftClient.record(current).conflict)){toast('請先完成草稿保存或重新載入衝突草稿。');return;}
$('form-error').hidden=true;captureCurrentInput();$('review-line').textContent=current.title+' · 必填題已完成 '+current.questions.length+' / '+current.questions.length;$('confirm-dialog').showModal();});
$('cancel-submit').addEventListener('click',()=>$('confirm-dialog').close());
$('confirm-submit').addEventListener('click',async()=>{if(authorizedMode){await submitAssigned();return;}if(!current||draft(current.id).submittedAt)return;$('confirm-submit').disabled=true;state.forms[current.id]={...draft(current.id),answers:readAnswers(),note:$('observation').value,submittedAt:Date.now()};persist();$('confirm-dialog').close();const n=completed();$('success-title').textContent=n===forms.length?'示範填答已全部完成':'本份問卷示範完成';$('success-description').textContent=n===forms.length?'兩份問卷的示範流程已完成，答案尚未正式提交。':`謝謝老師，尚有 ${forms.length-n} 份指定問卷待完成。`;$('receipt-form').textContent=current.title;$('receipt-respondent').textContent=draft(current.id).respondent;$('receipt-date').textContent=draft(current.id).date;$('receipt-time').textContent=formatter.format(draft(current.id).submittedAt);$('receipt-progress').textContent=`${n} / ${forms.length} 份`;$('success-actions').innerHTML='';if(n<forms.length){const next=forms.find(f=>!draft(f.id).submittedAt),b=document.createElement('button');b.type='button';b.className='btn';b.textContent='繼續下一份問卷';b.addEventListener('click',()=>openForm(next.id));$('success-actions').append(b);}const b=document.createElement('button');b.type='button';b.className='btn secondary';b.textContent=n===forms.length?'查看本次完成狀態':'返回任務首頁';b.addEventListener('click',()=>{renderHome();navigate('home');});$('success-actions').append(b);renderHome();navigate('success');});
for(const id of ['reset-demo','help-reset'])$(id).addEventListener('click',()=>$('reset-dialog').showModal());$('cancel-reset').addEventListener('click',()=>$('reset-dialog').close());$('confirm-reset').addEventListener('click',()=>{state=fresh();current=null;persist();$('reset-dialog').close();renderHome();navigate('home');toast('已重設示範，可以重新體驗。');});
async function leaveAuthorizedWorkspace(){if(!await saveDraft(true))return;try{const r=await fetch('/api/dev/teacher-workspace/logout',{method:'POST',credentials:'same-origin',cache:'no-store'});if(!r.ok)throw new Error();location.assign('/?role=teacher');}catch{toast('暫時無法登出，請稍後重試。');}}
let pendingSubmission=null;
$('confirm-dialog').addEventListener('cancel',event=>{if(pendingSubmission)event.preventDefault();});
async function submitAssigned(){
  if(!current||draft(current.id).submittedAt)return;
  const d=draft(current.id);
  const body={questionnaireVersionId:current.versionId,respondentName:d.respondent,filledOn:d.date,
    answers:current.questionIds.map((questionId,i)=>({questionId,optionValue:current.optionValues[d.answers[i]]})),observation:d.note};
  const json=JSON.stringify(body);
  if(pendingSubmission&&pendingSubmission.json!==json){toast('上次提交結果尚未確認，請先重試原內容。');return;}
  pendingSubmission??={key:crypto.randomUUID(),json};
  const button=$('confirm-submit');button.disabled=true;button.textContent='正在保存…';$('cancel-submit').disabled=true;
  try{
    const response=await fetch(`/api/dev/teacher-workspace/tasks/${current.taskId}/submit`,{method:'POST',credentials:'same-origin',cache:'no-store',
      headers:{'Content-Type':'application/json','Idempotency-Key':pendingSubmission.key,'X-Teacher-Submission':'1'},body:pendingSubmission.json});
    const receipt=await response.json();
    if(!response.ok){if(response.status<500)pendingSubmission=null;throw new Error(receipt.message||'暫時無法提交，請重試。');}
    state.forms[current.id]={...d,submittedAt:Date.parse(receipt.submittedAtUtc)};persist();pendingSubmission=null;
    $('confirm-dialog').close();$('success-title').textContent='本次指定問卷已提交';
    $('success-description').textContent='答案已保存至開發資料庫。本次授權已完成，不能再填寫或修改。';
    $('receipt-form').textContent=current.title;$('receipt-respondent').textContent=d.respondent;$('receipt-date').textContent=d.date;
    $('receipt-time').textContent=formatter.format(Date.parse(receipt.submittedAtUtc));$('receipt-progress').textContent=`${completed()} / ${forms.length} 份`;
    document.querySelector('#success-page .receipt-row:first-child strong').textContent=`${assignedTask.patient.displayName} · ${assignedTask.patient.caseCode}`;
    document.querySelector('#success-page .receipt-row:nth-child(5) span').textContent='提交時間';
    document.querySelector('#success-page .hint').textContent=`已保存開發測試答案 · 收據 ${receipt.responseId} · 授權 ${receipt.grantStatus}`;
    $('success-actions').replaceChildren();const exit=document.createElement('button');exit.type='button';exit.className='btn secondary';exit.textContent='登出並返回教師入口';exit.addEventListener('click',leaveAuthorizedWorkspace);$('success-actions').append(exit);
    $('session-label').textContent='本次授權已完成';navigate('success');
  }catch(error){toast(error instanceof TypeError?'提交結果尚未確認，請按確認送出重試；不要重新整理頁面。':error.message||'暫時無法提交，請重試。');}
  finally{button.disabled=false;button.textContent=pendingSubmission?'重試本次提交':'確認送出';$('cancel-submit').disabled=!!pendingSubmission;}
}
function applyTask(task){
  assignedTask=task;forms=task.questionnaires; $('reset-demo').hidden=true; $('help-reset').hidden=true;
  document.querySelector('.child h3').textContent=task.patient.displayName;
  document.querySelector('.child-meta').textContent=`個案代碼 ${task.patient.caseCode} · ${task.patient.sex} · ${task.patient.age} 歲`;
  document.querySelector('.child-end strong').textContent='開發測試授權已驗證';
  document.querySelector('.prototype-disclaimer').textContent='開發測試：虛構個案，未驗證家長同意；草稿及提交答案保存至開發資料庫。';
  document.querySelector('.welcome h2').textContent='已取得本次指定的 SNAP-IV 填答任務。';
  document.querySelector('.welcome p').textContent='可保存部分答案，重新開啟時讀回 DB 草稿；完整填寫後才能提交。';
  document.querySelector('#form-page .subtitle').textContent=`${task.patient.displayName} · ${task.patient.caseCode} · 虛構個案`;
  document.querySelector('.child-label').textContent='本次填答對象 · MySQL 虛構測試資料';
  document.querySelector('.side-note strong').textContent='開發測試授權';
  document.querySelector('.side-note p').textContent='草稿及提交答案保存至開發資料庫；全部指定問卷提交後授權失效。';
  document.querySelector('.demo').textContent='開發測試';document.querySelector('.deadline-time').textContent='全部指定問卷提交成功後授權失效';document.querySelector('.tips li:nth-child(2)').lastChild.textContent=' 本次指定 SNAP-IV，依過去一星期的狀況填答。';document.querySelector('.tips li:nth-child(3)').lastChild.textContent=' 請完成全部必填題；提交後不可修改。';
  document.querySelector('.footer').lastElementChild.textContent='測試授權已驗證 · 草稿保存至 DB · 提交後不可修改';
  document.querySelector('#questionnaire button[type="submit"]').textContent='檢查並送出';
  document.querySelector('#questionnaire button[type="submit"]').disabled=false;
  document.querySelector('.privacy span').textContent=`指定任務 ${task.taskId} · 問卷版本 ${forms[0].versionId} · 僅供開發測試`;
  document.querySelector('input[value="王○安 / 男 / 4 歲"]').value=`${task.patient.displayName} / ${task.patient.sex} / ${task.patient.age} 歲`;
  document.querySelector('.help-card p:last-of-type').textContent='本頁為虛構開發測試；草稿及提交答案保存至開發資料庫，不提供醫療判讀。';
  document.querySelector('#confirm-dialog .hint').textContent='確認後將保存至開發資料庫，提交成功後不可修改。';
  $('draft-help').textContent='可以保存部分答案到 DB，重新整理或重新驗證仍有效的授權後可接續。未按保存的變更不會自動保存。';
  $('draft-storage-note').textContent='按「儲存草稿」成功後，可重新整理讀回答案。提交成功後不可修改。';
  renderHome();document.querySelector('.content').hidden=false;
}
async function loadAssignedTask(){
  try{
    const response=await fetch('/api/dev/teacher-workspace/task',{credentials:'same-origin',cache:'no-store'});
    if(!response.ok)throw new Error('unavailable');
    applyTask(await response.json());
  }catch{
    const main=document.querySelector('.content');main.replaceChildren();
    const panel=document.createElement('div');panel.className='panel help-card';
    const heading=document.createElement('h1');heading.textContent='無法取得填答任務';
    const note=document.createElement('p');note.textContent='請先驗證有效的測試授權碼。會話到期時請重驗原授權碼；若授權已撤銷，請重新取得授權。';
    const link=document.createElement('a');link.className='btn';link.href='/?role=teacher';link.textContent='返回教師入口';
    panel.append(heading,note,link);main.append(panel);main.hidden=false;
    $('session-label').textContent='尚未取得有效測試授權';document.querySelectorAll('[data-nav]').forEach(b=>b.disabled=true);$('reset-demo').hidden=true;
  }
}
if(authorizedMode){
  document.querySelectorAll('a[href="/?role=teacher"]').forEach(link=>link.addEventListener('click',async event=>{
    event.preventDefault();await leaveAuthorizedWorkspace();
  }));
  loadAssignedTask();
}else renderHome();
