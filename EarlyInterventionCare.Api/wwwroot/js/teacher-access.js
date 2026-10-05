"use strict";

// Consume fragments synchronously, before verification or any awaited work.
let importedTeacherCode = new URLSearchParams(location.hash.slice(1)).get('code');
if (location.hash) history.replaceState(null, '', location.pathname + location.search);

// Development-only code verification. Codes and sessions are never saved in browser storage.
const teacherViews = ["input", "verifying", "failure", "scan", "unavailable"];
const teacherFlow = document.querySelector("#teacher-flow");
const teacherPanel = document.querySelector("#panel-teacher");
const teacherCode = document.querySelector("#teacher-code");
const teacherPreview = document.querySelector("#teacher-preview-controls");
let teacherPreviewTimer;
let teacherRequest;
let teacherAttempt = 0;
const invitationDialog = document.querySelector('#teacher-invitation-dialog');
const invitationButton = document.querySelector('#show-teacher-invitation');
function clearTeacherInvitation(){
  if(!invitationDialog)return;
  invitationButton.hidden=true;
  invitationDialog.close();
  document.querySelector('#teacher-invitation-url').value='';
  document.querySelector('#teacher-invitation-qr').removeAttribute('src');
  document.querySelector('#open-teacher-invitation').removeAttribute('href');
}
function setTeacherInvitation(result){
  if(!invitationDialog)return;
  const url=new URL(result.invitationUrl);
  if(url.origin!==location.origin||url.pathname!=='/'||url.search!=='?role=teacher'||!result.qrCodeDataUrl.startsWith('data:image/png;base64,'))throw new Error('invalid-invitation');
  document.querySelector('#teacher-invitation-url').value=url.href;
  document.querySelector('#open-teacher-invitation').href=url.href;
  document.querySelector('#teacher-invitation-qr').src=result.qrCodeDataUrl;
  document.querySelector('#teacher-invitation-status').textContent='';
  invitationButton.hidden=false;
  invitationDialog.showModal();
}
invitationButton?.addEventListener('click',()=>invitationDialog.showModal());
for(const id of ['close-teacher-invitation','teacher-invitation-done'])document.querySelector('#'+id)?.addEventListener('click',()=>invitationDialog.close());
document.querySelector('#copy-teacher-invitation')?.addEventListener('click',async()=>{
  const field=document.querySelector('#teacher-invitation-url');
  const status=document.querySelector('#teacher-invitation-status');
  try{await navigator.clipboard.writeText(field.value);status.textContent='邀請連結已複製。';}
  catch{field.focus();field.select();status.textContent='請按 Ctrl+C（Mac 為 ⌘C）複製已選取的連結。';}
});
function cancelTeacherRequest(){teacherAttempt++;teacherRequest?.abort();teacherRequest=null;}

function showTeacherView(view, focus = true) {
  clearTimeout(teacherPreviewTimer);
  const current = teacherViews.includes(view) ? view : "input";
  const isInput = current === "input";
  teacherPanel.hidden = !isInput;
  teacherFlow.hidden = isInput;
  document.querySelector(".role-tabs").hidden = !isInput;
  document.querySelector("#faq-section").hidden = isInput;
  document.querySelector("#teacher-form").removeAttribute("aria-busy");
  document.querySelectorAll(".teacher-state").forEach(state => { state.hidden = state.id !== `teacher-${current}`; });
  const heading = isInput ? null : document.querySelector(`#teacher-${current} h2`);
  if (heading) teacherFlow.setAttribute("aria-labelledby", heading.id);
  if (focus) {
    if (isInput) teacherCode.focus();
    else heading.focus();
  }
}

function resetTeacherFlow(isTeacher) {
  cancelTeacherRequest();
  clearTimeout(teacherPreviewTimer);
  teacherFlow.hidden = true;
  document.querySelectorAll(".teacher-state").forEach(state => { state.hidden = true; });
  document.querySelector(".role-tabs").hidden = false;
  teacherPreview.hidden = !isTeacher;
  teacherCode.setCustomValidity("");
  document.querySelector("#teacher-form").removeAttribute("aria-busy");
  if (isTeacher) showTeacherView("input", false);
  else clearTeacherInvitation();
}

document.addEventListener("login-role-change", event => resetTeacherFlow(event.detail === "tab-teacher"));

document.querySelectorAll("[data-teacher-view]").forEach(button => {
  button.addEventListener("click", () => { cancelTeacherRequest(); showTeacherView(button.dataset.teacherView); });
});

teacherCode.addEventListener("input", () => teacherCode.setCustomValidity(""));
document.querySelector("#teacher-form").addEventListener("submit", async event => {
  event.preventDefault();
  const code=teacherCode.value.trim();
  if(!code){teacherCode.setCustomValidity("請輸入測試授權碼。");teacherCode.reportValidity();return;}
  cancelTeacherRequest();
  const attempt=teacherAttempt;
  teacherRequest=new AbortController();
  showTeacherView("verifying");event.target.setAttribute("aria-busy","true");
  try{
    const response=await fetch("/api/dev/teacher-workspace/verify",{method:"POST",headers:{"Content-Type":"application/json"},credentials:"same-origin",cache:"no-store",body:JSON.stringify({code}),signal:teacherRequest.signal});
    if(attempt!==teacherAttempt)return;
    if(response.ok){const result=await response.json();teacherCode.value="";location.assign(result.workspaceUrl);}
    else if(response.status===400||response.status===401){showTeacherView("failure");}
    else{showTeacherView("unavailable");}
  }catch(error){if(error.name!=="AbortError"&&attempt===teacherAttempt)showTeacherView("unavailable");}
});
const createTestCode=document.querySelector("#create-test-code");
createTestCode.addEventListener("click",async()=>{
  const status=document.querySelector("#test-code-status");clearTeacherInvitation();createTestCode.disabled=true;status.hidden=false;status.textContent="正在建立虛構個案的測試授權……";
  try{
    const response=await fetch("/api/dev/teacher-workspace/grants",{method:"POST",credentials:"same-origin",cache:"no-store"});
    if(!response.ok)throw new Error("unavailable");
    const result=await response.json();
    if(document.querySelector("#tab-teacher").getAttribute("aria-selected")!=="true")return;
    teacherCode.value=result.authorizationCode;teacherCode.setCustomValidity("");showTeacherView("input");
    status.textContent="測試授權碼已填入。請按「驗證授權碼」。僅含虛構個案與 SNAP-IV；已存入 MySQL，重啟後可使用原碼。再次建立會撤銷舊測試碼。";
    setTeacherInvitation(result);
  }catch{status.textContent="無法建立測試授權，請確認服務已更新且以 Development 啟動。";}
  finally{createTestCode.disabled=false;}
});
document.querySelector("#scan-qr").addEventListener("click", () => showTeacherView("scan"));

resetTeacherFlow(document.querySelector("#tab-teacher").getAttribute("aria-selected") === "true");
const teacherInitialView = new URLSearchParams(location.search).get("view");
if (!teacherPreview.hidden && teacherViews.includes(teacherInitialView)) {
  showTeacherView(teacherInitialView, false);
}
if(importedTeacherCode!==null){
  if(!teacherPreview.hidden && /^[0-9a-fA-F]{4}(?:-?[0-9a-fA-F]{4}){3}$/.test(importedTeacherCode)){
    teacherCode.value=importedTeacherCode;
    showTeacherView('input');
    const status=document.querySelector('#test-code-status');
    status.hidden=false;status.textContent='邀請授權碼已帶入，請按「驗證授權碼」。仍需由後端確認授權有效。';
  }else if(!teacherPreview.hidden)showTeacherView('failure');
  importedTeacherCode=null;
}
