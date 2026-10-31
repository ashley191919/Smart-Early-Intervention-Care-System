"use strict";
(function(root){
 const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const relations={FATHER:'父親',MOTHER:'母親',GRANDPARENT:'祖父母',RELATIVE:'其他親屬',OTHER:'其他'};
 function create({api,modal,closeDialog,reload,onAccessError}){
  const client=root.EarlyCareParentQuestionnaireClient.create(api,()=>crypto.randomUUID());
  let version=0,definition=null,taskId='',baseline='',busy=false,pending=false,reloadArmed=false,outcomeBlocked=false;
  const body=()=>modal.querySelector('#modal-body');
  function payload(){const form=modal.querySelector('.parent-answer-form');if(!form)return null;
   return {questionnaireVersionId:definition.questionnaireVersionId,respondentName:form.elements.respondentName.value.trim(),filledOn:form.elements.filledOn.value,
    respondentRelationship:form.elements.relationship.value||null,answers:definition.questions.flatMap((q,i)=>{const selected=form.querySelector(`input[name="answer-${i}"]:checked`);return selected?[{questionId:q.questionId,optionValue:selected.value}]:[];}),observation:form.elements.observation.value};}
  function isDirty(){return !!modal.querySelector('.parent-answer-form')&&(busy||pending||JSON.stringify(payload())!==baseline);}
  function invalidate(){version++;definition=null;taskId='';baseline='';pending=false;busy=false;outcomeBlocked=false;}
  function error(message){const box=body().querySelector('.parent-form-error');box.textContent=message;box.hidden=false;box.tabIndex=-1;box.focus();}
  function lock(){modal.querySelectorAll('.parent-answer-form input,.parent-answer-form select,.parent-answer-form textarea').forEach(e=>e.disabled=busy||pending||outcomeBlocked);
   modal.querySelectorAll('[data-form-save],[data-form-submit],[data-form-reload],[data-form-receipt]').forEach(e=>e.disabled=busy||(outcomeBlocked&&!e.hasAttribute('data-form-receipt'))||(!e.hasAttribute('data-form-submit')&&!e.hasAttribute('data-form-receipt')&&pending));}
  function render(draft){
   if(draft.questionnaireVersionId!==definition.questionnaireVersionId)throw new Error('草稿版本與指定問卷不符。');
   const displayedRelations={...relations};
   if(['GUARDIAN','CAREGIVER'].includes(draft.respondentRelationship))displayedRelations[draft.respondentRelationship]=draft.respondentRelationship==='GUARDIAN'?'監護人（原紀錄）':'照顧者（原紀錄）';
   const answers=new Map();for(const answer of draft.answers){const q=definition.questions.find(q=>q.questionId===answer.questionId);
    if(!q||!q.options.some(o=>o.value===answer.optionValue)||answers.has(answer.questionId))throw new Error('草稿內容無法確認，請聯絡醫療團隊。');answers.set(answer.questionId,answer.optionValue);}
   body().innerHTML=`<p class="definition-version">版本 ${esc(definition.versionNumber)} · 共 ${definition.questions.length} 題</p><p class="definition-instructions">${esc(definition.instructions)}</p><form class="parent-answer-form"><div class="parent-form-fields"><label>填表人姓名<input name="respondentName" value="${esc(draft.respondentName)}" maxlength="50" required autocomplete="name"></label><label>填表日期<input type="date" name="filledOn" value="${esc(draft.filledOn)}" required></label><label>您與孩子的關係<select name="relationship" required><option value="">請選擇</option>${Object.entries(displayedRelations).map(([key,label])=>`<option value="${key}" ${draft.respondentRelationship===key?'selected':''}>${label}</option>`).join('')}</select></label></div>${definition.questions.map((q,i)=>`<fieldset class="parent-form-question"><legend>${esc(q.order)}. ${esc(q.text)} <small>${q.requiredOnSubmit?'必填':'選填'}</small></legend><div class="parent-answer-options">${q.options.map(o=>`<label><input type="radio" name="answer-${i}" value="${esc(o.value)}" ${answers.get(q.questionId)===o.value?'checked':''} ${q.requiredOnSubmit?'required':''}><span>${esc(o.label)}</span></label>`).join('')}</div></fieldset>`).join('')}<label class="parent-observation">補充觀察（選填）<textarea name="observation" maxlength="1000">${esc(draft.observation)}</textarea></label><p class="parent-form-status" role="status">${draft.updatedAtUtc?'上次保存：'+esc(new Date(draft.updatedAtUtc).toLocaleString('zh-TW')):'尚未儲存草稿'}</p><p class="parent-form-error" role="alert" hidden></p><div class="actions"><button class="btn secondary" type="button" data-form-save>先存起來，下次再填</button><button class="btn" type="submit" data-form-submit>檢查並送出</button><button class="btn secondary" type="button" data-form-receipt>查詢提交結果</button><button class="btn secondary" type="button" data-form-reload>重新載入草稿</button><button class="btn secondary" type="button" data-close-definition>返回問卷清單</button></div><p class="privacy-note">提交成功後不能自行修改原始答案。</p></form>`;
   busy=false;pending=false;reloadArmed=false;baseline=JSON.stringify(payload());
   body().querySelector('form').addEventListener('submit',event=>{event.preventDefault();save(true);});
  }
  async function showReceipt(receipt){
   if(receipt.questionnaireVersionId!==definition.questionnaireVersionId)throw new Error('回執版本與指定問卷不符，請重新確認。');
   baseline='';pending=false;
   body().innerHTML=`<section class="parent-receipt"><h3>問卷已提交</h3><p>完成狀態：已提交</p><p>伺服器提交時間：${esc(new Date(receipt.submittedAtUtc).toLocaleString('zh-TW'))}</p><p class="definition-version">回執編號：${esc(receipt.responseId)}</p><p>答案已由伺服器確認，提交後不能修改原始答案。</p><div class="actions"><button class="btn secondary" type="button" data-close-definition>返回問卷清單</button></div></section>`;
   await reload();
  }
  async function handle(e){if(root.EarlyCareParentQuestionnaireClient.isAccessInvalidation(e)){invalidate();onAccessError(e);return;}if(e.code==='IDEMPOTENCY_VERSION_UNSUPPORTED')outcomeBlocked=true;error(e.message);}
  async function save(submit){
   if(busy)return;const form=modal.querySelector('form');if(!form)return;
   if(submit&&!form.elements.relationship.value){form.elements.relationship.reportValidity();return;}
   if(submit&&!form.reportValidity())return;
   const input=payload(),request=version;busy=true;lock();
   try{const result=submit?await client.submit(taskId,input):await client.save(taskId,input);if(request!==version||!modal.open)return;
    if(submit)await showReceipt(result);else{baseline=JSON.stringify(input);body().querySelector('.parent-form-status').textContent='草稿已保存：'+new Date(result.updatedAtUtc).toLocaleString('zh-TW');await reload();}}
   catch(e){if(request!==version||!modal.open)return;pending=submit&&!root.EarlyCareParentQuestionnaireClient.isAccessInvalidation(e)&&![400,409].includes(e.status);await handle(e);}
   finally{busy=false;if(modal.open)lock();}
  }
  async function open(id,submitted=false){
   if(!await closeDialog())return;taskId=id;const request=++version;busy=false;pending=false;outcomeBlocked=false;
   modal.classList.add('definition-dialog');modal.querySelector('#modal-title').textContent='問卷內容';body().innerHTML='<p role="status">正在載入問卷…</p>';modal.showModal();
   try{definition=await root.EarlyCareParentClient.loadDefinition(api,id);if(request!==version||!modal.open)return;
    modal.querySelector('#modal-title').textContent=definition.title;
    if(submitted){const receipt=await client.receipt(id);if(request!==version||!modal.open)return;await showReceipt(receipt);}else{const draft=await client.draft(id);if(request!==version||!modal.open)return;render(draft);}
    modal.querySelector('#modal-title').tabIndex=-1;modal.querySelector('#modal-title').focus();modal.scrollTop=0;
   }catch(e){if(request!==version||!modal.open)return;if(root.EarlyCareParentQuestionnaireClient.isAccessInvalidation(e)){invalidate();onAccessError(e);return;}
    body().innerHTML=`<p class="parent-form-error" role="alert">${esc(e.message)}</p><div class="actions"><button class="btn secondary" type="button" data-parent-task="${esc(id)}" data-submitted="${submitted}">重新載入</button><button class="btn secondary" type="button" data-close-definition>返回問卷清單</button></div>`;}
  }
  modal.addEventListener('click',async event=>{
   if(event.target.closest('[data-form-save]')){save(false);return;}
   if(event.target.closest('[data-form-receipt]')&&!busy){const request=version;busy=true;lock();try{const r=await client.receipt(taskId);if(request===version&&modal.open)await showReceipt(r);}catch(e){if(request===version&&modal.open)await handle(e);}finally{busy=false;if(modal.open)lock();}return;}
   if(event.target.closest('[data-form-reload]')&&!busy&&!pending){
    if(isDirty()&&!reloadArmed){reloadArmed=true;error('重新載入會取代目前尚未儲存的內容。若確定，請再按一次「重新載入草稿」。');return;}
    const request=version;busy=true;lock();try{const draft=await client.draft(taskId);if(request===version&&modal.open)render(draft);}catch(e){if(request===version&&modal.open)await handle(e);}finally{busy=false;if(modal.open)lock();}
   }
  });
  return Object.freeze({open,isDirty,invalidate,isBusy:()=>busy});
 }
 root.EarlyCareParentFormUI=Object.freeze({create});
})(window);
