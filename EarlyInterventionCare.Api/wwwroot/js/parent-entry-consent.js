"use strict";
// UI choices only. Never use this browser state as a Consent record, Guardian
// proof, or permission to create a TeacherGrant. Formal persistence is pending.
(function (root) {
 const key = "earlycare.parent.entry-choice.ui.v1";
 let uiChoice=null;
 function readChoice() {
  if(uiChoice)return uiChoice;
  try {
   const value = JSON.parse(root.sessionStorage.getItem(key));
   return value?.version === 1 && value.dataUse === true && typeof value.teacherInvitation === "boolean" ? value : null;
  } catch { return null; }
 }
 let active = null;
 function request() {
  if (readChoice() || active) return;
  const dialog = document.createElement("dialog");
  dialog.className = "parent-entry-consent";
  dialog.setAttribute("aria-labelledby", "parent-entry-title");
  dialog.setAttribute("aria-describedby", "parent-entry-description");
  dialog.innerHTML = `<form>
   <div class="entry-heading"><span class="entry-eyebrow">開始填寫前</span><h2 id="parent-entry-title" tabindex="-1">資料使用與教師填答同意</h2><p id="parent-entry-description">請閱讀以下說明，並選擇是否同意邀請教師協助填寫。</p></div>
   <section class="entry-copy" aria-labelledby="entry-data-title"><h3 id="entry-data-title">問卷資料使用</h3><p>您提供的孩子資料及問卷答案，供獲授權的醫療團隊作為兒童發展評估與照護參考。請依實際情況填寫；提交後無法自行修改原始答案，如需更正請聯絡醫療團隊。</p></section>
   <label class="entry-check"><input name="dataUse" type="checkbox" required><span>我已閱讀並同意問卷資料供醫療團隊進行評估與照護使用。<small>必選</small></span></label>
   <section class="entry-copy" aria-labelledby="entry-teacher-title"><h3 id="entry-teacher-title">邀請教師協助填寫</h3><p>您可以選擇同意邀請教師提供孩子在校園的觀察。教師僅填寫指定問卷；家長端不顯示教師填答進度或答案。</p></section>
   <label class="entry-check"><input name="teacherInvitation" type="checkbox"><span>我同意邀請教師填寫本次指定問卷。<small>選填，不影響家長填寫問卷</small></span></label>
   <div class="entry-actions"><button class="entry-primary" type="submit" disabled>確認並進入</button><button class="entry-secondary" type="button" data-entry-exit>返回登入</button></div>
  </form>`;
  const form = dialog.querySelector("form"), confirm = form.querySelector('[type="submit"]');
  form.elements.dataUse.addEventListener("change", () => { confirm.disabled = !form.elements.dataUse.checked; });
  form.addEventListener("submit", event => {
   event.preventDefault();
   if (!form.elements.dataUse.checked) { form.elements.dataUse.focus(); return; }
   const choice = {version: 1, dataUse: true, teacherInvitation: form.elements.teacherInvitation.checked};
   uiChoice=choice;
   // Per-tab navigation continuity only; this is not a server consent receipt.
   try { root.sessionStorage.setItem(key, JSON.stringify(choice)); } catch { /* Keep this page usable without browser storage. */ }
   dialog.close();dialog.remove();active = null;
   root.dispatchEvent(new Event('parent-entry-confirmed'));
   document.querySelector("#page-title, #form-title, #home-title")?.focus({preventScroll:true});
  });
  function exit() {
   root.EarlyCareParentSession?.signOut();
   try { root.sessionStorage.removeItem(key); } catch { /* No browser state to clear. */ }
   root.location.assign("/?role=parent");
  }
  dialog.addEventListener("cancel", event => { event.preventDefault();exit(); });
  dialog.querySelector("[data-entry-exit]").addEventListener("click", exit);
  document.body.append(dialog);active = dialog;dialog.showModal();dialog.querySelector("h2").focus();
 }
 // Account mode never consults the UI-only sessionStorage flag above.
 let serverDialog=null,serverReject=null,generation=0;
 function invalidate(){generation++;if(serverDialog){serverDialog.close();serverDialog.remove();serverDialog=null;}if(serverReject){const reject=serverReject;serverReject=null;reject(Object.assign(new Error('同意流程已取消，請重新載入。'),{code:'CONSENT_CANCELLED'}));}}
 window.addEventListener('pagehide',invalidate);
 async function ensure(api) {
  invalidate();const run=generation;
  const client=root.EarlyCareParentConsentClient.create(api,()=>crypto.randomUUID());
  const context=await client.load();
  if(run!==generation)throw Object.assign(new Error('同意流程已取消。'),{code:'CONSENT_CANCELLED'});
  for(let index=0;index<context.targets.length;index++){
   const target=context.targets[index];
   if(target.dataUseDecision==='AGREED'&&target.consentId)continue;
   if(target.dataUseDecision==='DECLINED')throw Object.assign(new Error('目前沒有有效的資料使用同意，請聯絡醫療團隊。'),{status:403,code:'CONSENT_REQUIRED'});
   await new Promise((resolve,reject)=>{
    const modal=document.createElement('dialog');modal.className='parent-entry-consent';modal.setAttribute('aria-labelledby','server-consent-title');
    modal.innerHTML='<form><div class="entry-heading"><span class="entry-eyebrow">開始填寫前</span><h2 id="server-consent-title" tabindex="-1"></h2><p class="entry-version"></p></div><section class="entry-copy"><p class="entry-document"></p></section><label class="entry-check"><input name="dataUse" type="checkbox" required><span>我已閱讀並同意以上資料使用說明。<small>必選</small></span></label><fieldset class="entry-teacher"><legend>是否同意教師參與填答？</legend><label><input type="radio" name="teacher" value="UNDECIDED" checked>尚未決定</label><label><input type="radio" name="teacher" value="AGREED">同意</label><label><input type="radio" name="teacher" value="DECLINED">不同意</label><p>不同意或尚未決定，不影響家長自己的問卷填答。</p></fieldset><p class="entry-error" role="alert" hidden></p><div class="entry-actions"><button class="entry-primary" type="submit" disabled>確認並進入</button><button class="entry-secondary" type="button" data-exit>返回登入</button></div></form>';
    modal.querySelector('h2').textContent=context.document.title;
    modal.querySelector('.entry-version').textContent='版本 '+context.document.version+(context.targets.length>1?` · 第 ${index+1}／${context.targets.length} 個同意範圍`:'');
    modal.querySelector('.entry-document').textContent=context.document.content;
    serverDialog=modal;serverReject=reject;
    const finish=()=>{modal.close();modal.remove();serverDialog=null;serverReject=null;};
    const form=modal.querySelector('form'),confirm=form.querySelector('[type=submit]'),error=modal.querySelector('.entry-error');let sending=false;
    form.elements.dataUse.addEventListener('change',()=>{confirm.disabled=!form.elements.dataUse.checked||sending;});
    const exit=()=>{if(sending)return;root.EarlyCareParentSession.signOut();finish();reject(Object.assign(new Error('請先完成同意流程。'),{code:'CONSENT_CANCELLED'}));root.location.assign('/?role=parent');};
    modal.querySelector('[data-exit]').addEventListener('click',exit);modal.addEventListener('cancel',event=>{event.preventDefault();exit();});
    form.addEventListener('submit',async event=>{
     event.preventDefault();if(sending||!form.elements.dataUse.checked)return;sending=true;confirm.disabled=true;error.hidden=true;
     const inputs=[...form.querySelectorAll('input')];inputs.forEach(input=>input.disabled=true);
     try{await client.save(context.document,target,'AGREED',form.elements.teacher.value);if(run!==generation)return;finish();resolve();}
     catch(e){if(run!==generation)return;if(e.status===401){finish();reject(e);return;}error.textContent=e.message;error.hidden=false;error.tabIndex=-1;error.focus();}
     finally{sending=false;confirm.disabled=!form.elements.dataUse.checked;inputs.forEach(input=>input.disabled=false);}
    });
    document.body.append(modal);modal.showModal();modal.querySelector('h2').focus();
   });
  }
 }
 root.EarlyCareParentEntryConsent = Object.freeze({request,ensure,invalidate,hasUiChoice:()=>!!readChoice()});
 // Direct links to the browser questionnaire get the same entry UI. No API is
 // called and no grant is created by either checkbox.
 if (document.body.classList.contains("parent-fill") && !root.sessionStorage.getItem("earlycare.parent.accessToken")) request();
})(window);
