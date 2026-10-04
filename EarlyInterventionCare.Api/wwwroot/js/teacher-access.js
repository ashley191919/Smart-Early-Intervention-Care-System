"use strict";

// Presentation states only. No grant validation, camera access or credentials are stored here.
const teacherViews = ["input", "verifying", "failure", "scan", "unavailable"];
const teacherFlow = document.querySelector("#teacher-flow");
const teacherPanel = document.querySelector("#panel-teacher");
const teacherCode = document.querySelector("#teacher-code");
const teacherPreview = document.querySelector("#teacher-preview-controls");
let teacherPreviewTimer;

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
  button.addEventListener("click", () => showTeacherView(button.dataset.teacherView));
});

teacherCode.addEventListener("input", () => teacherCode.setCustomValidity(""));
document.querySelector("#teacher-form").addEventListener("submit", event => {
  event.preventDefault();
  if (!teacherCode.value.trim()) {
    teacherCode.setCustomValidity("請輸入家長提供的授權碼。");
    teacherCode.reportValidity();
    return;
  }
  // A brief preview demonstrates the waiting state, then honestly reports the missing API.
  showTeacherView("verifying");
  event.target.setAttribute("aria-busy", "true");
  teacherPreviewTimer = setTimeout(() => showTeacherView("unavailable"), 1200);
});
document.querySelector("#scan-qr").addEventListener("click", () => showTeacherView("scan"));

resetTeacherFlow(document.querySelector("#tab-teacher").getAttribute("aria-selected") === "true");
const teacherInitialView = new URLSearchParams(location.search).get("view");
if (!teacherPreview.hidden && teacherViews.includes(teacherInitialView)) {
  showTeacherView(teacherInitialView, false);
}
