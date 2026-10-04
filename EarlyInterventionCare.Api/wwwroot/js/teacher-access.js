"use strict";

// Development-only code verification. Codes and sessions are never saved in browser storage.
const teacherViews = ["input", "verifying", "failure", "scan", "unavailable"];
const teacherFlow = document.querySelector("#teacher-flow");
const teacherPanel = document.querySelector("#panel-teacher");
const teacherCode = document.querySelector("#teacher-code");
const teacherPreview = document.querySelector("#teacher-preview-controls");
let teacherPreviewTimer;
let teacherRequest;
let teacherAttempt = 0;
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
  const status=document.querySelector("#test-code-status");createTestCode.disabled=true;status.hidden=false;status.textContent="正在建立虛構個案的測試授權……";
  try{
    const response=await fetch("/api/dev/teacher-workspace/grants",{method:"POST",credentials:"same-origin",cache:"no-store"});
    if(!response.ok)throw new Error("unavailable");
    const result=await response.json();
    if(document.querySelector("#tab-teacher").getAttribute("aria-selected")!=="true")return;
    teacherCode.value=result.authorizationCode;teacherCode.setCustomValidity("");showTeacherView("input");
    status.textContent="測試授權碼已填入。請按「驗證授權碼」。僅含虛構個案與 SNAP-IV；重啟服務後失效。";
  }catch{status.textContent="無法建立測試授權，請確認服務已更新且以 Development 啟動。";}
  finally{createTestCode.disabled=false;}
});
document.querySelector("#scan-qr").addEventListener("click", () => showTeacherView("scan"));

resetTeacherFlow(document.querySelector("#tab-teacher").getAttribute("aria-selected") === "true");
const teacherInitialView = new URLSearchParams(location.search).get("view");
if (!teacherPreview.hidden && teacherViews.includes(teacherInitialView)) {
  showTeacherView(teacherInitialView, false);
}
