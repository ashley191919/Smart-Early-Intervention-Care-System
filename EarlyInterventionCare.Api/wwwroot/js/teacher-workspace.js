
'use strict';
const $=id=>document.getElementById(id);
const authorizedMode=new URLSearchParams(location.search).get('mode')==='authorized';
const sharedMode=new URLSearchParams(location.search).get('mode')==='demo';
const sharedRole=location.pathname.includes('parent-questionnaire')?'parent':'teacher';
document.querySelectorAll('[data-parent-page]').forEach(button=>button.addEventListener('click',async()=>{
  if(draftBusy||submissionBusy||!await saveDraft(true))return;
  location.assign('/parent-workspace.html?page='+encodeURIComponent(button.dataset.parentPage));
}));
if(sharedMode)window.addEventListener('pageshow',event=>{if(event.persisted)location.reload();});
const sharedCode=sessionStorage.getItem('earlycare.demo.teacher');
let sharedTasks=[],sharedRevisions={};
let assignedTask=null;
const draftClient=authorizedMode?new TeacherDraftClient((...args)=>fetch(...args)):null;
let draftBusy=false,formOpenSequence=0,accessBlocked=false,submissionBusy=false;
function blockUnavailableAccess(error){
  if(!authorizedMode||![401,403].includes(error?.status))return false;
  accessBlocked=true;formOpenSequence++;state=fresh();current=null;pendingSubmission=null;
  draftClient.records.clear();
  document.querySelectorAll('dialog[open]').forEach(dialog=>dialog.close());
  document.querySelectorAll('.page').forEach(page=>page.hidden=true);
  $('questions').replaceChildren();$('task-list').replaceChildren();
  $('questionnaire').reset();$('respondent-name').value='';$('observation').value='';
  document.querySelectorAll('[data-nav]').forEach(button=>button.disabled=true);
  const main=document.querySelector('.content');let panel=$('access-unavailable');
  if(!panel){panel=document.createElement('section');panel.id='access-unavailable';panel.className='panel help-card';main.append(panel);}
  panel.replaceChildren();const title=document.createElement('h1');title.tabIndex=-1;title.textContent='無法繼續填答';
  const note=document.createElement('p');note.textContent='授權或登入狀態已失效，請返回教師入口重新驗證。';
  const link=document.createElement('a');link.className='btn';link.href='/?role=teacher';link.textContent='返回教師入口';
  panel.append(title,note,link);main.hidden=false;title.focus();$('session-label').textContent='授權無法使用';$('nav-count').textContent='—';return true;
}
if(authorizedMode){
  document.querySelector('.content').hidden=true;
  window.addEventListener('pagehide',()=>{formOpenSequence++;document.querySelector('.content').hidden=true;});
  window.addEventListener('pageshow',event=>{if(event.persisted)loadAssignedTask();});
}
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
function persist(){
  if(!sharedMode)return true;
  try{const task=sharedTasks.find(t=>t.formId===current?.id);if(!task)return false;
    sharedRevisions[task.id]=SharedDemo.save(task.id,{...draft(current.id),questions:current.questions,options:current.options},sharedRevisions[task.id],sharedCode);return true;
  }catch(error){toast(error.message);return false;}
}
function toast(message){$('toast').textContent=message;$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,3300);}
function navigate(page){for(const p of document.querySelectorAll('.page'))p.hidden=p.id!==page+'-page';$('crumb').textContent={home:'任務首頁',form:'問卷填寫',success:'完成確認',help:'填寫協助'}[page];for(const b of document.querySelectorAll('[data-nav]')){const active=b.dataset.nav===(page==='help'?'help':page==='form'?'tasks':'home');b.classList.toggle('active',active);if(active)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');}document.querySelector('.content').scrollTo({top:0,behavior:'instant'});window.scrollTo({top:0,behavior:'instant'});const h=$(page+'-title');if(h)h.focus({preventScroll:true});}
function renderHome(){const done=completed();$('today').textContent=new Intl.DateTimeFormat('zh-TW',{timeZone:'Asia/Taipei',year:'numeric',month:'long',day:'numeric',weekday:'long'}).format(Date.now());$('deadline-date').textContent='授權不限時';$('task-summary').textContent=`已完成 ${done} / ${forms.length} 份`;$('nav-count').textContent=forms.length-done;if(authorizedMode)$('deadline-note').textContent='請完成指定問卷並提交。';$('completion-banner').hidden=done!==forms.length;$('completion-banner').textContent='本次問卷已全部完成。';$('session-label').textContent=authorizedMode?'本次填答任務':done===forms.length?'示範填答已完成':'教師填答工作台';$('deadline-note').textContent=done===forms.length?'本次問卷已完成。':'全部指定問卷提交成功或家長撤銷後失效。';$('task-list').innerHTML=forms.map(f=>{const d=draft(f.id),n=count(f.id),sent=!!d.submittedAt;const label=sent?'已完成':d.savedAt?'填寫中':'未開始';return `<article class="panel task"><div class="task-top"><div class="task-icon ${f.color}">${icon('form')}</div><div class="task-title"><h3>${f.title}</h3><p>${f.desc}</p></div><span class="status ${sent?'done':d.savedAt?'progress':''}">${label}</span></div><div class="task-bottom"><div class="task-progress"><div class="progress-label"><span>${sent?(authorizedMode?'已保存提交 ':'本機提交 ')+formatter.format(d.submittedAt):`已填題數 ${n} / ${f.questions.length}`}</span><span>${Math.round(n/f.questions.length*100)}%</span></div><div class="track"><span style="width:${n/f.questions.length*100}%"></span></div></div><button type="button" class="btn ${sent?'secondary':''}" data-form="${f.id}" ${sent?'disabled':''}>${sent?'已完成':d.savedAt?'繼續填寫':'開始填寫'} ${icon(sent?'check':'arrow')}</button></div></article>`;}).join('');if(sharedMode){$('session-label').textContent=sharedRole==='parent'?'家長共用填答':'教師填答工作台';$('deadline-note').textContent=sharedRole==='parent'?'每份問卷提交後不可修改。':'提交後不可修改；教師授權不限時。';if(sharedRole==='parent'){$('deadline-date').textContent='本輪問卷';document.querySelector('.deadline-time').textContent='依醫護指派的問卷填寫';document.querySelector('.deadline .side-title').lastChild.textContent='本輪問卷進度';}$('completion-banner').textContent='指定問卷已提交，填答狀態已更新。';}if(authorizedMode){$('deadline-note').textContent=done===forms.length?'本次答案已保存，授權已完成。':'請完成指定問卷並提交。';$('completion-banner').textContent='本次指定問卷已提交。';$('session-label').textContent=done===forms.length?'本次授權已完成':'本次填答任務';}}
function openAssignedForm(id){$('confirm-submit').disabled=false;current=forms.find(f=>f.id===id);if(!current||draft(id).submittedAt)return;const d=draft(id);$('form-title').textContent=current.title;$('form-instructions').textContent=current.instructions;const source=document.createElement('span');source.className='source-note';source.textContent='題目來源：'+current.source+'。';$('form-instructions').append(source);$('respondent-name').value=d.respondent||'';$('respondent-date').value=authorizedMode?(d.date||''):d.date||new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei',year:'numeric',month:'2-digit',day:'2-digit'}).format(Date.now());$('questions').innerHTML=current.questions.map((q,i)=>`${current.groups[i]?`<h2 class="group-heading">${current.groups[i]}</h2>`:''}<fieldset class="question" id="q-${i}"><legend><span class="qnum">${i+1}</span>${q}<span class="required">必填</span></legend><div class="options ${current.options.length===3?'three':''}">${current.options.map((option,value)=>`<label class="option"><input type="radio" name="q${i}" value="${value}" ${d.answers[i]===value?'checked':''} required><span>${option}</span></label>`).join('')}</div></fieldset>`).join('');if($('respondent-relationship'))$('respondent-relationship').value=d.relationship||'';$('observation').value=d.note;$('form-error').hidden=true;$('save-state').textContent=d.savedAt?(authorizedMode?'已保存至 DB · ':'本頁上次暫存 ')+formatter.format(d.savedAt):'尚未儲存草稿';
$('reload-draft').hidden=!authorizedMode;$('draft-notice').hidden=true;$('form-deadline').textContent=sharedRole==='parent'?'提交後不能修改原始答案。':authorizedMode?'提交成功後不可修改，全部指定問卷提交後授權失效。':'授權不限時，提交後不可修改。';$('confirm-submit').disabled=false;updateProgress();navigate('form');}
async function openForm(id){
  if(accessBlocked)return;
  if(draftBusy||pendingSubmission){toast('請先完成目前的保存或提交重試。');return;}
  const sequence=++formOpenSequence;
  if(authorizedMode){
    try{
      const response=await fetch('/api/dev/teacher-workspace/task',{credentials:'same-origin',cache:'no-store'});
      if(!response.ok)throw Object.assign(new Error('授權無法使用，請重新驗證。'),{status:response.status});
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
    }catch(error){if(!blockUnavailableAccess(error))toast(error.message||'無法載入草稿，請稍後重試。');return;}
  }
  openAssignedForm(id);
  if(authorizedMode)showDraftStatus();
}
function readAnswers(){const answers={};for(let i=0;i<current.questions.length;i++){const selected=document.querySelector(`input[name="q${i}"]:checked`);if(selected)answers[i]=Number(selected.value);}return answers;}
function updateProgress(){if(!current)return;const n=Object.keys(readAnswers()).length,p=Math.round(n/current.questions.length*100);$('form-progress-text').textContent=`已填 ${n} / ${current.questions.length} 題`;$('form-progress-percent').textContent=p+'%';$('form-progress-bar').style.width=p+'%';}
function captureCurrentInput(){
  if(!current||draft(current.id).submittedAt)return;
  state.forms[current.id]={...draft(current.id),answers:readAnswers(),note:$('observation').value,
    respondent:$('respondent-name').value.trim(),date:$('respondent-date').value,...($('respondent-relationship')?{relationship:$('respondent-relationship').value}:{})};
}
function lockDraftControls(locked){
  locked=locked||accessBlocked;
  draftBusy=locked;
  $('questionnaire').querySelectorAll('input,select,textarea,button').forEach(el=>el.disabled=locked);
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
      if(blockUnavailableAccess(error))return false;
      state.forms[form.id]={...input,dirty:true};
      showDraftStatus(error.code==='DRAFT_CONFLICT'?'草稿衝突：請重新載入最新草稿':'儲存失敗，尚未確認保存');
      toast(error.message||'儲存失敗，輸入仍保留。');return false;
    }finally{lockDraftControls(false);}
  }
  captureCurrentInput();state.forms[current.id]={...draft(current.id),savedAt:Date.now()};if(sharedMode&&!persist())return false;
  $('save-state').textContent=(sharedMode?'草稿已保存 · ':'草稿暫存於本頁 · ')+formatter.format(draft(current.id).savedAt);renderHome();
  if(!silent)toast(sharedMode?'草稿已保存，可重新開啟接續。':'草稿已暫存。');return true;
}
async function home(){if(draftBusy)return;if(!await saveDraft(true))return;if(sharedMode&&sharedRole==='parent'){location.assign('/parent-workspace.html');return;}renderHome();navigate('home');}
async function help(){if(draftBusy)return;if(!await saveDraft(true))return;navigate('help');}
document.querySelectorAll('[data-nav]').forEach(b=>b.addEventListener('click',async()=>{
  if(sharedMode&&sharedRole==='parent'&&b.dataset.nav==='tasks'){if(!await saveDraft(true))return;renderHome();navigate('home');$('task-heading').scrollIntoView({behavior:'smooth',block:'start'});return;}
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
  }catch(error){if(!blockUnavailableAccess(error)){$('reload-draft-dialog').close();showDraftStatus('重新載入失敗，目前輸入仍保留。');toast(error.message||'無法載入最新草稿。');}}
  finally{lockDraftControls(false);}
});
window.addEventListener('beforeunload',event=>{
  if(authorizedMode&&!accessBlocked&&(draftBusy||pendingSubmission||Object.values(state.forms).some(d=>d.dirty&&!d.submittedAt))){event.preventDefault();event.returnValue='';}
});
$('task-list').addEventListener('click',e=>{const b=e.target.closest('[data-form]');if(b&&!b.disabled)openForm(b.dataset.form);});
$('questionnaire').addEventListener('input',()=>{
  if(current){captureCurrentInput();state.forms[current.id].dirty=true;}
  if(authorizedMode)showDraftStatus();else $('save-state').textContent='有尚未儲存的變更';updateProgress();
});
$('questionnaire').addEventListener('change',e=>{const q=e.target.closest('.question');if(q)q.classList.remove('invalid');});
$('save-draft').addEventListener('click',()=>saveDraft());$('back-home').addEventListener('click',home);
for(const id of ['help-link','form-help'])$(id).addEventListener('click',help);$('help-return').addEventListener('click',()=>{renderHome();navigate('home');});
$('questionnaire').addEventListener('submit',e=>{e.preventDefault();const person=$('respondent-name'),date=$('respondent-date');if(!person.value.trim()||!date.value||!date.validity.valid){$('form-error').textContent='請先填寫填表人姓名與有效的填表日期。';$('form-error').hidden=false;(!person.value.trim()?person:date).focus();return;}const relationship=$('respondent-relationship');if(relationship&&!['FATHER','MOTHER','GRANDPARENT','RELATIVE','OTHER'].includes(relationship.value)){$('form-error').textContent='請選擇您與孩子的關係。';$('form-error').hidden=false;relationship.focus();return;}const answers=readAnswers();let first=null;for(let i=0;i<current.questions.length;i++){const missing=answers[i]===undefined;$('q-'+i).classList.toggle('invalid',missing);if(missing&&first===null)first=i;}if(first!==null){$('form-error').textContent='還有必填題目尚未完成，請填寫標示的題目後再送出。';$('form-error').hidden=false;$('q-'+first).querySelector('input').focus();return;}
if(authorizedMode&&(draftBusy||draftClient.record(current).conflict)){toast('請先完成草稿保存或重新載入衝突草稿。');return;}
$('form-error').hidden=true;captureCurrentInput();$('review-line').textContent=current.title+' · 必填題已完成 '+current.questions.length+' / '+current.questions.length;$('confirm-dialog').showModal();});
$('cancel-submit').addEventListener('click',()=>$('confirm-dialog').close());
$('confirm-submit').addEventListener('click',async()=>{if(authorizedMode){await submitAssigned();return;}if(!current||draft(current.id).submittedAt)return;$('confirm-submit').disabled=true;state.forms[current.id]={...draft(current.id),answers:readAnswers(),note:$('observation').value,submittedAt:Date.now()};if(!persist()){state.forms[current.id].submittedAt=null;$('confirm-submit').disabled=false;return;}$('confirm-dialog').close();const n=completed();$('success-title').textContent=n===forms.length?'本次填答已全部完成':'本份問卷已完成';$('success-description').textContent=sharedMode?'答案已保存。':n===forms.length?'本次問卷已完成。':`謝謝老師，尚有 ${forms.length-n} 份指定問卷待完成。`;$('receipt-form').textContent=current.title;$('receipt-respondent').textContent=draft(current.id).respondent;$('receipt-date').textContent=draft(current.id).date;$('receipt-time').textContent=formatter.format(draft(current.id).submittedAt);$('receipt-progress').textContent=`${n} / ${forms.length} 份`;$('success-actions').innerHTML='';if(n<forms.length){const next=forms.find(f=>!draft(f.id).submittedAt),b=document.createElement('button');b.type='button';b.className='btn';b.textContent='繼續下一份問卷';b.addEventListener('click',()=>openForm(next.id));$('success-actions').append(b);}const b=document.createElement('button');b.type='button';b.className='btn secondary';b.textContent=sharedMode&&sharedRole==='parent'?'返回家長儀表板':n===forms.length?'查看本次完成狀態':'返回任務首頁';b.addEventListener('click',()=>{if(sharedMode&&sharedRole==='parent'){home();return;}renderHome();navigate('home');});$('success-actions').append(b);renderHome();navigate('success');});
for(const id of ['reset-demo','help-reset'])$(id).addEventListener('click',()=>$('reset-dialog').showModal());$('cancel-reset').addEventListener('click',()=>$('reset-dialog').close());$('confirm-reset').addEventListener('click',()=>{state=fresh();current=null;persist();$('reset-dialog').close();renderHome();navigate('home');toast('已重設示範，可以重新體驗。');});
async function leaveAuthorizedWorkspace(){if(!await saveDraft(true))return;try{const r=await fetch('/api/dev/teacher-workspace/logout',{method:'POST',credentials:'same-origin',cache:'no-store'});if(!r.ok)throw new Error();location.assign('/?role=teacher');}catch{toast('暫時無法登出，請稍後重試。');}}
let pendingSubmission=null;
$('confirm-dialog').addEventListener('cancel',event=>{if(pendingSubmission)event.preventDefault();});
async function submitAssigned(){
  if(accessBlocked||submissionBusy||!current||draft(current.id).submittedAt)return;
  submissionBusy=true;
  const d=draft(current.id);
  const body={questionnaireVersionId:current.versionId,respondentName:d.respondent,filledOn:d.date,
    answers:current.questionIds.map((questionId,i)=>({questionId,optionValue:current.optionValues[d.answers[i]]})),observation:d.note};
  const json=JSON.stringify(body);
  if(pendingSubmission&&pendingSubmission.json!==json){submissionBusy=false;toast('上次提交結果尚未確認，請先重試原內容。');return;}
  pendingSubmission??={key:crypto.randomUUID(),json};
  const button=$('confirm-submit');button.disabled=true;button.textContent='正在保存…';$('cancel-submit').disabled=true;
  try{
    const response=await fetch(`/api/dev/teacher-workspace/tasks/${current.taskId}/submit`,{method:'POST',credentials:'same-origin',cache:'no-store',
      headers:{'Content-Type':'application/json','Idempotency-Key':pendingSubmission.key,'X-Teacher-Submission':'1'},body:pendingSubmission.json});
    if([401,403].includes(response.status))throw Object.assign(new Error('授權無法使用，請重新驗證。'),{status:response.status});
    let receipt;try{receipt=await response.json();}catch{throw new Error('無法確認提交結果，請重試本次提交。');}
    if(!response.ok){if(response.status<500)pendingSubmission=null;throw Object.assign(new Error(receipt.message||'暫時無法提交，請重試。'),{status:response.status});}
    if(receipt.taskId!==current.taskId||receipt.questionnaireVersionId!==current.versionId||!receipt.responseId||!Number.isFinite(Date.parse(receipt.submittedAtUtc))||!['ACTIVE','USED'].includes(receipt.grantStatus))throw new Error('提交回執無法確認，請重試本次提交。');
    state.forms[current.id]={...d,submittedAt:Date.parse(receipt.submittedAtUtc)};persist();pendingSubmission=null;renderHome();$('toast').hidden=true;
    $('confirm-dialog').close();$('success-title').textContent='本次指定問卷已提交';
    const allDone=receipt.grantStatus==='USED';
    $('success-title').textContent=allDone?'本次指定問卷已全部提交':'本份問卷已提交';
    $('success-description').textContent=allDone?'答案已保存，本次授權已完成。':'答案已保存，請繼續完成其他指定問卷。';
    $('receipt-form').textContent=current.title;$('receipt-respondent').textContent=d.respondent;$('receipt-date').textContent=d.date;
    $('receipt-time').textContent=formatter.format(Date.parse(receipt.submittedAtUtc));$('receipt-progress').textContent=`${completed()} / ${forms.length} 份`;
    document.querySelector('#success-page .receipt-row:first-child strong').textContent=`${assignedTask.patient.displayName} · ${assignedTask.patient.caseCode}`;
    document.querySelector('#success-page .receipt-row:nth-child(5) span').textContent='提交時間';
    document.querySelector('#success-page .hint').textContent=`答案已保存 · 收據 ${receipt.responseId} · 授權 ${receipt.grantStatus}`;
    $('success-actions').replaceChildren();
    if(!allDone){const next=document.createElement('button');next.type='button';next.className='btn';next.textContent='返回其他待填問卷';next.addEventListener('click',()=>{renderHome();navigate('home');});$('success-actions').append(next);}
    const exit=document.createElement('button');exit.type='button';exit.className='btn secondary';exit.textContent='登出並返回教師入口';exit.addEventListener('click',leaveAuthorizedWorkspace);$('success-actions').append(exit);
    $('session-label').textContent=allDone?'本次授權已完成':'本次填答任務';navigate('success');
  }catch(error){if(!blockUnavailableAccess(error))toast(error instanceof TypeError?'提交結果尚未確認，請按確認送出重試；不要重新整理頁面。':error.message||'暫時無法提交，請重試。');}
  finally{submissionBusy=false;button.disabled=accessBlocked;button.textContent=pendingSubmission?'重試本次提交':'確認送出';$('cancel-submit').disabled=accessBlocked||!!pendingSubmission;}
}
function applyTask(task){
  assignedTask=task;forms=task.questionnaires; $('reset-demo').hidden=true; $('help-reset').hidden=true;
  document.querySelector('.child h3').textContent=task.patient.displayName;
  document.querySelector('.child-meta').textContent=`個案代碼 ${task.patient.caseCode} · ${task.patient.sex} · ${task.patient.age} 歲`;
  document.querySelector('.child-end strong').textContent='本次填答任務';
  document.querySelector('.prototype-disclaimer').textContent='開發測試：，未驗證家長同意；草稿及提交答案保存至開發資料庫。';
  document.querySelector('.welcome h2').textContent='已取得本次指定的 SNAP-IV 填答任務。';
  document.querySelector('.welcome p').textContent='可保存部分答案，重新開啟時讀回 DB 草稿；完整填寫後才能提交。';
  document.querySelector('#form-page .subtitle').textContent=`${task.patient.displayName} · ${task.patient.caseCode} · `;
  document.querySelector('.child-label').textContent='本次填答對象';
  document.querySelector('.side-note strong').textContent='指定問卷授權';
  document.querySelector('.side-note p').textContent='草稿及提交答案保存至開發資料庫；全部指定問卷提交後授權失效。';
  document.querySelector('.demo').textContent='開發測試';document.querySelector('.deadline-time').textContent='全部指定問卷提交成功後授權失效';document.querySelector('.tips li:nth-child(2)').lastChild.textContent=' 本次指定 SNAP-IV，依過去一星期的狀況填答。';document.querySelector('.tips li:nth-child(3)').lastChild.textContent=' 請完成全部必填題；提交後不可修改。';
  document.querySelector('.footer').lastElementChild.textContent='測試授權已驗證 · 草稿保存至 DB · 提交後不可修改';
  document.querySelector('#questionnaire button[type="submit"]').textContent='檢查並送出';
  document.querySelector('#questionnaire button[type="submit"]').disabled=false;
  document.querySelector('.privacy span').textContent=`指定任務 ${task.taskId} · 問卷版本 ${forms[0].versionId}`;
  document.querySelector('input[value="王○安 / 男 / 4 歲"]').value=`${task.patient.displayName} / ${task.patient.sex} / ${task.patient.age} 歲`;
  document.querySelector('.help-card p:last-of-type').textContent='如需填答協助，請聯絡承辦窗口。';
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
    accessBlocked=true;current=null;state=fresh();pendingSubmission=null;draftBusy=false;draftClient.records.clear();
    const main=document.querySelector('.content');main.replaceChildren();
    const panel=document.createElement('div');panel.className='panel help-card';
    const heading=document.createElement('h1');heading.textContent='無法取得填答任務';
    const note=document.createElement('p');note.textContent='請先驗證有效的測試授權碼。會話到期時請重驗原授權碼；若授權已撤銷，請重新取得授權。';
    const link=document.createElement('a');link.className='btn';link.href='/?role=teacher';link.textContent='返回教師入口';
    panel.append(heading,note,link);main.append(panel);main.hidden=false;
    $('session-label').textContent='尚未取得有效測試授權';document.querySelectorAll('[data-nav]').forEach(b=>b.disabled=true);$('reset-demo').hidden=true;
  }
}
if(sharedRole==='parent'&&sessionStorage.getItem('earlycare.parent.accessToken')){
  document.querySelector('.content').hidden=true;location.replace('/parent-workspace.html?mode=account');
}else if(sharedRole==='parent'&&!sharedMode){
  const main=document.querySelector('.content');main.hidden=false;main.replaceChildren();
  const note=document.createElement('p');note.textContent='請從家長儀表板選擇指定問卷。';
  const link=document.createElement('a');link.href='/parent-workspace.html';link.className='btn';link.textContent='返回家長儀表板';main.append(note,link);
}else if(authorizedMode){
  document.querySelectorAll('a[href="/?role=teacher"]').forEach(link=>link.addEventListener('click',async event=>{
    event.preventDefault();await leaveAuthorizedWorkspace();
  }));
  loadAssignedTask();
}else if(sharedMode){
  const requested=new URLSearchParams(location.search).get('task');
  const grant=sharedRole==='teacher'?SharedDemo.verify(sharedCode):null;
  sharedTasks=sharedRole==='parent'?SharedDemo.parentTaskScope(requested):SharedDemo.tasks(sharedRole).filter(t=>grant?.taskIds.includes(t.id));
  if(!sharedTasks.length){document.querySelector('.content').textContent='目前沒有可填寫的任務，請返回登入入口。';}
  else{
    forms=forms.filter(f=>sharedTasks.some(t=>t.formId===f.id));
    sharedTasks.forEach(t=>{sharedRevisions[t.id]=t.revision;if(t.draft)state.forms[t.formId]=t.draft;});
    $('reset-demo').hidden=true;$('help-reset').hidden=true;
    document.querySelector('.prototype-disclaimer').textContent='共用前端示範：草稿與提交同步醫療端；僅保存在此瀏覽器，不是正式登入或 MySQL 資料。';
    $('draft-help').textContent='保存草稿後可重新開啟接續；提交後不能修改。';document.querySelector('.welcome h2').textContent='請完成本次指定問卷';document.querySelector('.welcome p').textContent='填寫途中可儲存草稿，完成後再確認提交。';document.querySelector('.child-end strong').textContent=sharedRole==='parent'?'家長填答任務':'教師授權已確認';
    document.querySelector('#confirm-dialog .hint').textContent='確認提交後不能修改。';
    $('draft-storage-note').textContent='儲存草稿後可接續填寫；提交後不可修改。';document.querySelector('.footer').lastElementChild.textContent='提交後不可修改';renderHome();
    const requestedTask=sharedTasks.find(t=>t.id===requested);
    if(sharedRole==='parent'&&requestedTask&&!draft(requestedTask.formId).submittedAt)openForm(requestedTask.formId);
  }
}else renderHome();
